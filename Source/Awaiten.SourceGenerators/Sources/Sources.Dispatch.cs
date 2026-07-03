using System.Text;
using Awaiten.SourceGenerators.Entities;

namespace Awaiten.SourceGenerators;

internal static partial class Sources
{
	private static Dictionary<ServiceKey, int> BuildServiceMap(ContainerModel model)
	{
		Dictionary<ServiceKey, int> serviceToIndex = new();
		InstanceModel[] instances = model.Instances.AsArray();
		for (int i = 0; i < instances.Length; i++)
		{
			foreach (ServiceKey service in instances[i].Services.AsArray())
			{
				serviceToIndex[service] = i;
			}
		}

		return serviceToIndex;
	}

	/// <summary>
	///     Each registered service type paired with the instance that produces it, deduplicated on the service
	///     type. Registrations are coalesced upstream so service types are already unique, but deduping here
	///     keeps the typed base list and its explicit implementations in lockstep: a duplicate base or a
	///     duplicate explicit implementation would each fail to compile, so neither must be emitted twice.
	///     A parameterized service is excluded: it is not directly resolvable (only through its
	///     <c>Func&lt;TArg…, T&gt;</c> factory), so it gets no typed resolution fast path. A keyed registration
	///     is likewise excluded: it is reached only by <c>[FromKey]</c> injection, never the typed path.
	/// </summary>
	private static IEnumerable<(string Service, int Index)> UniqueServices(InstanceModel[] instances, bool strict, bool syncResolveAfterInit)
	{
		HashSet<string> seen = new(StringComparer.Ordinal);
		for (int i = 0; i < instances.Length; i++)
		{
			// A parameterized service has no typed fast path; a strictly-withheld disposable service is reached
			// only through Owned<T>, so it gets none either. An async-tainted service in the strict default is
			// reached only through ResolveAsync, so it gets no synchronous typed resolver either.
			if (instances[i].IsParameterized || IsWithheld(instances[i], strict) || !EmitsSync(instances[i], syncResolveAfterInit))
			{
				continue;
			}

			foreach (ServiceKey service in instances[i].Services.AsArray())
			{
				// Keyed registrations are reached only by [FromKey] injection, never the typed resolver fast
				// path, so they get no IAwaitenResolver<T> base or explicit typed resolution.
				if (service.Key is not null || !seen.Add(service.Service))
				{
					continue;
				}

				yield return (service.Service, i);
			}
		}
	}

	/// <summary>
	///     Appends <c>, global::Awaiten.IAwaitenResolver&lt;S&gt;</c> for each registered service type to a
	///     just-written base list, so the scope (and its root) exposes a typed resolution fast path.
	/// </summary>
	private static void EmitGenericResolverBases(StringBuilder builder, InstanceModel[] instances, bool strict, bool syncResolveAfterInit)
	{
		foreach ((string service, int _) in UniqueServices(instances, strict, syncResolveAfterInit))
		{
			builder.Append(", global::Awaiten.IAwaitenResolver<").Append(service).Append('>');
		}
	}

	/// <summary>
	///     Emits the explicit <c>IAwaitenResolver&lt;S&gt;.Resolve()</c> implementation for each registered
	///     service type, delegating to that instance's resolver method. Only the explicitly registered service
	///     types get a typed path; the synthetic <c>Func&lt;T&gt;</c>/<c>Lazy&lt;T&gt;</c> relationship entries
	///     remain reachable through the Type-based dispatch table.
	/// </summary>
	private static void EmitGenericResolverImpls(StringBuilder builder, int depth, InstanceModel[] instances, Names names, bool strict, bool syncResolveAfterInit)
	{
		foreach ((string service, int index) in UniqueServices(instances, strict, syncResolveAfterInit))
		{
			Indent(builder, depth).Append(service).Append(" global::Awaiten.IAwaitenResolver<")
				.Append(service).Append(">.Resolve() => ").Append(names.Resolver(index)).AppendLine("();");
		}
	}

	/// <summary>
	///     Emits a generic <c>Resolve&lt;T&gt;()</c> instance method on the owner. Called through the concrete
	///     (sealed) container or scope, the <c>this is IAwaitenResolver&lt;T&gt;</c> test dispatches to the
	///     typed resolver; relationship types and unregistered services fall back to the Type-based path.
	/// </summary>
	private static void EmitGenericResolveMethod(StringBuilder builder, int depth)
	{
		AppendXmlSummary(builder, depth, "Resolves <typeparamref name=\"T\" />.");
		Indent(builder, depth).AppendLine("public T Resolve<T>()");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("if (this is global::Awaiten.IAwaitenResolver<T> __typed)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return __typed.Resolve();");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("return (T)Resolve(typeof(T));");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits the <c>IAwaitenContainerMetadata.Registrations</c> list on the Root: the compile-time,
	///     reflection-free surface of public (unkeyed) registrations with their lifetimes. This is what the
	///     <c>Awaiten.Extensions.DependencyInjection</c> companion projects into a service collection. A
	///     parameterized service is omitted (it cannot be resolved by service type without its runtime
	///     arguments, only through its factory), as are keyed registrations (reached solely by <c>[FromKey]</c>
	///     injection). Open generic registrations contribute only their expanded closed instances, so every
	///     advertised service type is a concrete, resolvable type. An async-tainted service (one with no
	///     synchronous resolution path) is flagged <c>requiresAsync</c> so the bridge projects it as a
	///     <c>Task&lt;T&gt;</c>; when <paramref name="syncResolveAfterInit" /> is set the container makes such a
	///     service synchronously resolvable after warm-up, so it is advertised as an ordinary synchronous service.
	/// </summary>
	private static void EmitRegistrations(StringBuilder builder, int depth, InstanceModel[] instances, bool syncResolveAfterInit)
	{
		// The registrations are compile-time constants for the container type, so they live in one static
		// array rather than being rebuilt per Root construction.
		AppendXmlSummary(builder, depth,
			"The registration metadata advertised through <c>IAwaitenContainerMetadata.Registrations</c>.");
		Indent(builder, depth).AppendLine("private static readonly global::Awaiten.AwaitenRegistration[] __registrations =");
		Indent(builder, depth).AppendLine("{");
		foreach (InstanceModel instance in instances)
		{
			if (instance.IsParameterized)
			{
				continue;
			}

			bool requiresAsync = instance.IsAsyncTainted && !syncResolveAfterInit;
			bool externallyOwned = instance.Production == ProductionKind.Instance;
			foreach (ServiceKey service in instance.Services.AsArray())
			{
				if (service.Key is not null)
				{
					continue;
				}

				Indent(builder, depth + 1).Append("new global::Awaiten.AwaitenRegistration(typeof(").Append(service.Service)
					.Append("), ").Append(AwaitenLifetimeOf(instance.Lifetime));
				if (requiresAsync)
				{
					builder.Append(", requiresAsync: true");
				}

				if (externallyOwned)
				{
					builder.Append(", externallyOwned: true");
				}

				builder.AppendLine("),");
			}
		}

		Indent(builder, depth).AppendLine("};");
		builder.AppendLine();
		Indent(builder, depth).AppendLine("global::System.Collections.Generic.IReadOnlyList<global::Awaiten.AwaitenRegistration> global::Awaiten.IAwaitenContainerMetadata.Registrations => __registrations;");
	}

	/// <summary>
	///     Emits the Root's advertised <c>ExternalDependencies</c> list (the distinct <c>[FromServices]</c> /
	///     <c>[ImportServices]</c> service types, empty when there are none) that <c>IAwaitenContainerMetadata</c>
	///     requires. The <c>ExternalResolver</c> the container routes those dependencies through is emitted on the
	///     base <c>Scope</c> (inherited by the Root), so a host can wire each scope independently.
	/// </summary>
	private static void EmitExternalMetadata(StringBuilder builder, int depth, InstanceModel[] instances)
	{
		string[] external = ExternalDependencies(instances);
		if (external.Length == 0)
		{
			Indent(builder, depth).AppendLine(
				"global::System.Collections.Generic.IReadOnlyList<global::System.Type> global::Awaiten.IAwaitenContainerMetadata.ExternalDependencies => global::System.Array.Empty<global::System.Type>();");
			return;
		}

		Indent(builder, depth).AppendLine("private static readonly global::System.Type[] __externalDependencies =");
		Indent(builder, depth).AppendLine("{");
		foreach (string type in external)
		{
			Indent(builder, depth + 1).Append("typeof(").Append(type).AppendLine("),");
		}

		Indent(builder, depth).AppendLine("};");
		builder.AppendLine();
		Indent(builder, depth).AppendLine(
			"global::System.Collections.Generic.IReadOnlyList<global::System.Type> global::Awaiten.IAwaitenContainerMetadata.ExternalDependencies => __externalDependencies;");
	}

	private static string AwaitenLifetimeOf(Lifetime lifetime) => lifetime switch
	{
		Lifetime.Singleton => "global::Awaiten.AwaitenLifetime.Singleton",
		Lifetime.Transient => "global::Awaiten.AwaitenLifetime.Transient",
		_ => "global::Awaiten.AwaitenLifetime.Scoped",
	};

	private static void EmitResolutionApi(StringBuilder builder, int depth, EmitContext context, bool strict, bool syncResolveAfterInit, string[] varianceCandidates)
	{
		InstanceModel[] instances = context.Instances;
		Names names = context.Names;
		Dictionary<ServiceKey, int> serviceToIndex = context.ServiceToIndex;

		List<DispatchEntry> entries = BuildDispatchEntries(instances, names, serviceToIndex, strict, syncResolveAfterInit);
		// The runtime variance-fallback candidates: the registered variant closed-generic-interface service
		// types that are actually by-type dispatchable, in registration order. A candidate excluded from the
		// synchronous dispatch (async-tainted under the strict default, or failed to build) is dropped - the
		// fallback can only route a request to an existing bucket.
		List<string> varianceEntries = VarianceDispatchTypes(varianceCandidates, entries);
		List<DispatchEntry> rootWithheld = Withheld(entries);
		// hasWithheld gates the __withheld guidance lookup in Resolve (root-withheld disposables plus
		// async-only services); hasRootWithheld gates the RootWithheld slot-flag check in TryResolve, which only
		// the dispatchable root-withheld disposables need (async-only services have no dispatch entry).
		bool hasRootWithheld = rootWithheld.Count > 0;
		bool hasWithheld = WithheldTypes(rootWithheld, instances, names, serviceToIndex, syncResolveAfterInit).Count > 0;

		AppendXmlSummary(builder, depth,
			"Resolves the service registered for <paramref name=\"serviceType\" />, throwing when it is not resolvable.");
		Indent(builder, depth).AppendLine("public object Resolve(global::System.Type serviceType)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("if (TryResolve(serviceType, out object? instance))");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return instance!;");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		if (hasWithheld)
		{
			// The type was not resolved synchronously: either it is root-withheld and this is the Root (a child
			// scope resolved it through TryResolve and returned above), or it is an async-only service excluded
			// from synchronous resolution on every scope. Surface the targeted guidance (Owned<T> / ResolveAsync)
			// instead of the generic "no registration" message. TryResolve stays non-throwing in both cases.
			Indent(builder, depth + 1).AppendLine("if (__withheld.TryGetValue(serviceType, out string? __guidance))");
			Indent(builder, depth + 1).AppendLine("{");
			Indent(builder, depth + 2).AppendLine("throw new global::System.InvalidOperationException(__guidance);");
			Indent(builder, depth + 1).AppendLine("}");
			builder.AppendLine();
		}

		Indent(builder, depth + 1).AppendLine(
			"throw new global::System.InvalidOperationException($\"No registration for type '{serviceType}' on this container.\");");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();

		AppendXmlSummary(builder, depth,
			"Attempts to resolve <paramref name=\"serviceType\" />, returning <see langword=\"false\" /> when it is not resolvable.");
		Indent(builder, depth).AppendLine("public bool TryResolve(global::System.Type serviceType, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out object? instance)");
		Indent(builder, depth).AppendLine("{");
		if (entries.Count == 0)
		{
			Indent(builder, depth + 1).AppendLine("instance = null;");
			Indent(builder, depth + 1).AppendLine("return false;");
			Indent(builder, depth).AppendLine("}");
			return;
		}

		// Open-addressed probe: hash the requested type into its bucket window and scan the (small, fixed-width)
		// window for an identity match. Each slot carries its resolver delegate directly, so a hit invokes it with
		// no switch - the dispatch is O(1) and constant-size in IL regardless of the registration count. Matching
		// is by runtime Type identity (the contract of this dispatch): a Type wrapper such as TypeDelegator, or a
		// Type without a runtime handle, does not dispatch.
		Indent(builder, depth + 1).AppendLine("int __i = (int)((uint)serviceType.TypeHandle.GetHashCode() % (uint)__bucketCount) * __bucketSize;");
		Indent(builder, depth + 1).AppendLine("int __end = __i + __bucketSize;");
		Indent(builder, depth + 1).AppendLine("for (; __i < __end; __i++)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("ref readonly __Bucket __b = ref __buckets[__i];");
		Indent(builder, depth + 2).AppendLine("if ((object?)__b.Key == (object?)serviceType)");
		Indent(builder, depth + 2).AppendLine("{");
		if (hasRootWithheld)
		{
			// Root-only withholding: a child scope resolves the slot (its lifetime bounded by the scope), but the
			// Root returns false (and Resolve then throws the guidance), so the root-accumulation leak stays
			// impossible. The flag rides on the slot, so it is a bare field read after the identity match.
			Indent(builder, depth + 3).AppendLine("if (__b.RootWithheld && this is Root)");
			Indent(builder, depth + 3).AppendLine("{");
			Indent(builder, depth + 4).AppendLine("instance = null;");
			Indent(builder, depth + 4).AppendLine("return false;");
			Indent(builder, depth + 3).AppendLine("}");
			builder.AppendLine();
		}

		Indent(builder, depth + 3).AppendLine("instance = __b.Resolve(this);");
		Indent(builder, depth + 3).AppendLine("return true;");
		Indent(builder, depth + 2).AppendLine("}");
		builder.AppendLine();
		// __BuildBuckets fills each window front-first, so an empty slot ends the window: a miss stops at the
		// first empty slot instead of always paying the full (global-max) window width.
		Indent(builder, depth + 2).AppendLine("if (__b.Key is null)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("break;");
		Indent(builder, depth + 2).AppendLine("}");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		if (varianceEntries.Count > 0)
		{
			// The exact-match probe missed: hand the request to the variance fallback, which satisfies a
			// differently-closed generic interface request through a variance-compatible registration. The
			// exact-match fast path above is untouched - the fallback only ever runs on what would otherwise
			// be a failed resolution.
			Indent(builder, depth + 1).AppendLine("return __TryResolveVariant(serviceType, out instance);");
			Indent(builder, depth).AppendLine("}");
			builder.AppendLine();
			EmitVarianceFallback(builder, depth, varianceEntries, hasWithheld);
			return;
		}

		Indent(builder, depth + 1).AppendLine("instance = null;");
		Indent(builder, depth + 1).AppendLine("return false;");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     The variance candidates that have a by-type dispatch entry (in candidate registration order): the
	///     service types the runtime variance fallback may route a differently-closed request to. Empty when the
	///     container registers no variant closed generic interface, so typical containers emit no fallback at all.
	/// </summary>
	private static List<string> VarianceDispatchTypes(string[] varianceCandidates, List<DispatchEntry> entries)
	{
		if (varianceCandidates.Length == 0)
		{
			return new List<string>();
		}

		HashSet<string> dispatchable = new(entries.Select(entry => entry.Type), StringComparer.Ordinal);
		return varianceCandidates.Where(dispatchable.Contains).ToList();
	}

	/// <summary>
	///     Emits the runtime variance fallback consulted by <c>TryResolve</c> after the exact-match probe missed:
	///     an imperative <c>Resolve&lt;T&gt;()</c> / <c>Resolve(Type)</c> of a closed generic interface with no
	///     bucket entry is satisfied by the nearest variance-compatible registered service - the same candidates,
	///     conversion rule (identity or implicit reference conversion; value-type arguments never convert) and
	///     nearest-wins selection as the compile-time redirect, so imperative and injected resolution agree even
	///     for a closed type no consumer parameter ever requested (which a compile-time dispatch alias cannot
	///     cover). A successful route is memoized in <c>__varianceRoutes</c>, so repeated requests pay one
	///     dictionary hit plus the target's O(1) probe instead of re-scanning; failures are not memoized (they
	///     throw from <c>Resolve</c> anyway, and unbounded junk types must not grow the cache). A type with
	///     withheld guidance keeps its targeted error instead of being silently variance-routed.
	/// </summary>
	private static void EmitVarianceFallback(StringBuilder builder, int depth, List<string> candidates, bool hasWithheld)
	{
		// The candidate service types, in registration order - mirroring the compile-time candidate order so
		// the registration-order tie-break picks the same target at runtime.
		Indent(builder, depth).AppendLine("private static readonly global::System.Type[] __varianceCandidates = new global::System.Type[]");
		Indent(builder, depth).AppendLine("{");
		foreach (string candidate in candidates)
		{
			Indent(builder, depth + 1).Append("typeof(").Append(candidate).AppendLine("),");
		}

		Indent(builder, depth).AppendLine("};");
		builder.AppendLine();
		Indent(builder, depth).AppendLine(
			"private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::System.Type> __varianceRoutes = new global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::System.Type>();");
		builder.AppendLine();

		Indent(builder, depth).AppendLine("private bool __TryResolveVariant(global::System.Type serviceType, out object? instance)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("if (__varianceRoutes.TryGetValue(serviceType, out global::System.Type? __route))");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return TryResolve(__route, out instance);");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("instance = null;");
		// Only a constructed generic interface can be variance-satisfied. A type with withheld guidance (a
		// root-withheld disposable on the Root, an async-only service) keeps its targeted error from Resolve.
		Indent(builder, depth + 1).Append("if (!serviceType.IsConstructedGenericType || !serviceType.IsInterface")
			.AppendLine(hasWithheld ? " || __withheld.ContainsKey(serviceType))" : ")");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return false;");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("global::System.Type __definition = serviceType.GetGenericTypeDefinition();");
		Indent(builder, depth + 1).AppendLine("global::System.Type? __match = null;");
		Indent(builder, depth + 1).AppendLine("foreach (global::System.Type __candidate in __varianceCandidates)");
		Indent(builder, depth + 1).AppendLine("{");
		// A candidate satisfies the request when it is a different closure of the same generic interface
		// definition and an instance of it IS-A the request - IsAssignableFrom is exactly the identity-or-
		// implicit-reference-conversion check C# variance defines (a value-type argument never converts).
		Indent(builder, depth + 2).AppendLine("if ((object)__candidate == (object)serviceType");
		Indent(builder, depth + 2).AppendLine("    || __candidate.GetGenericTypeDefinition() != __definition");
		Indent(builder, depth + 2).AppendLine("    || !serviceType.IsAssignableFrom(__candidate))");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("continue;");
		Indent(builder, depth + 2).AppendLine("}");
		builder.AppendLine();
		// Nearest candidate wins - replace the current best when it converts to the candidate - falling back
		// to registration order, mirroring the generator's FindVarianceMatch.
		Indent(builder, depth + 2).AppendLine("if (__match is null || __candidate.IsAssignableFrom(__match))");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("__match = __candidate;");
		Indent(builder, depth + 2).AppendLine("}");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("if (__match is null || !TryResolve(__match, out instance))");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return false;");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("__varianceRoutes.TryAdd(serviceType, __match);");
		Indent(builder, depth + 1).AppendLine("return true;");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     The ordered dispatch cases for an owner: each registered service type mapped to its resolver
	///     call, plus the relationship types <c>Func&lt;T&gt;</c> and <c>Lazy&lt;T&gt;</c> over it (a fresh
	///     factory/lazy bound to this owner's resolver). The order is identical on the container and its
	///     scope, so a single static table built on the container is valid for both.
	/// </summary>
	private static List<DispatchEntry> BuildDispatchEntries(InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool strict, bool syncResolveAfterInit)
	{
		List<DispatchEntry> entries = new();
		HashSet<string> seen = new(StringComparer.Ordinal);

		// Impl-to-index and collection membership, so the withholding analysis can follow a service's collection
		// dependencies (BuildsFreshDisposable) and a collection with a build-on-demand disposable member can be
		// withheld from by-type resolution on the Root, just like the singular resolution of such a member.
		Dictionary<string, int> implToIndex = new(StringComparer.Ordinal);
		for (int i = 0; i < instances.Length; i++)
		{
			implToIndex[instances[i].ImplementationType] = i;
		}

		Dictionary<ServiceKey, List<int>> collectionMembers = AwaitenGenerator.CollectionMemberIndices(names.Collections, implToIndex);

		// Explicit service registrations first, so a directly registered relationship type (e.g. a
		// registered Lazy<T>) wins the dispatch slot over the synthetic relationship entry below. Service
		// types are unique across instances (registrations are coalesced), so each is added exactly once.
		// Seeding 'seen' here lets the synthetic pass skip any key an explicit registration already claimed.
		// An async-tainted service in the strict default is excluded from synchronous dispatch entirely - it
		// is reachable only through ResolveAsync - so neither its bare type nor any relationship over it is added.
		for (int i = 0; i < instances.Length; i++)
		{
			if (!EmitsSync(instances[i], syncResolveAfterInit))
			{
				continue;
			}

			AddServiceEntries(instances[i], names.Resolver(i), entries, seen, IsWithheld(instances[i], strict), IsFuncWithheld(instances, i, serviceToIndex, collectionMembers, strict));
		}

		// Synthetic Func<T>/Lazy<T> over each service, skipping any key an explicit registration already
		// claimed so the static dispatch table never contains a duplicate key (which would otherwise throw
		// from the dictionary initializer at runtime). A parameterized service is skipped: its only entry is
		// the Func<TArg…, T> added above.
		for (int i = 0; i < instances.Length; i++)
		{
			if (!instances[i].IsParameterized && EmitsSync(instances[i], syncResolveAfterInit))
			{
				AddRelationshipEntries(instances[i], names.Resolver(i), entries, seen, IsFuncWithheld(instances, i, serviceToIndex, collectionMembers, strict));
			}
		}

		// Collection dispatch: every unkeyed, sync-materializable collection is publicly resolvable as all six
		// shapes (IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T>, IList<T>, ICollection<T>, T[]) - the
		// eagerly materialized array satisfies each. A keyed collection is reached only by [FromKey] injection; an
		// element type whose collection was explicitly registered is not synthesized (all-or-nothing); a collection
		// with an async-tainted member has no synchronous materialization (its shapes throw AWT122-style guidance
		// via __withheld, and injecting one is AWT122). The seen guard is belt-and-braces against a slot an
		// explicit registration already claimed.
		AddCollectionEntries(instances, names, serviceToIndex, collectionMembers, strict, entries, seen);

		// The awaited-collection view (every Task<C> shape) joins the synchronous dispatch too: it always hands
		// back a Task synchronously, even for a collection whose members are async-tainted (it awaits them behind
		// the task), so it is offered by type alongside the synchronous shapes rather than through an async arm.
		AddAwaitedCollectionEntries(instances, names, serviceToIndex, collectionMembers, strict, entries, seen);

		return entries;
	}

	/// <summary>
	///     Adds the public dispatch entries (all six collection shapes) for each unkeyed, sync-materializable
	///     collection whose synthesis is not suppressed by an explicit registration. A collection materializes its
	///     members eagerly on the resolving scope, so if any member is a build-on-demand disposable service (its
	///     plain resolver tracks a fresh disposable on the owner), re-resolving the collection by type off the Root
	///     would accumulate those disposables for the container's lifetime - the exact leak the singular resolution
	///     of such a member is root-withheld to prevent. Such a collection is therefore root-withheld too:
	///     resolvable from a child scope (which bounds its members), but withheld from by-type resolution on the
	///     Root. There is no <c>Owned&lt;T&gt;</c> form for a collection, so the guidance steers to a child scope,
	///     direct injection, or LifetimeSafety.Loose.
	/// </summary>
	private static void AddCollectionEntries(
		InstanceModel[] instances,
		Names names,
		Dictionary<ServiceKey, int> serviceToIndex,
		IReadOnlyDictionary<ServiceKey, List<int>> collectionMembers,
		bool strict,
		List<DispatchEntry> entries,
		HashSet<string> seen)
	{
		foreach (ServiceMembers collection in names.Collections)
		{
			ServiceKey collectionKey = new(collection.Service, collection.Key);

			// A keyed collection has no by-type surface; a synthesis-suppressed element is served by its explicit
			// registration; a non-sync-materializable collection is handled by AsyncWithheldCollections instead.
			if (collection.Key is not null
			    || SynthesisSuppressed(serviceToIndex, collection.Service)
			    || !names.IsSyncCollection(collectionKey))
			{
				continue;
			}

			bool rootWithheld = collectionMembers.TryGetValue(collectionKey, out List<int>? members)
			                    && members.Any(member => IsFuncWithheld(instances, member, serviceToIndex, collectionMembers, strict));

			string array = CollectionLiteral(collectionKey, names);
			foreach (string shape in AwaitenGenerator.CollectionShapeTypes(collection.Service))
			{
				AddCollectionShape(shape, array, rootWithheld, entries, seen);
			}

			// The async view of the same collection: every member is synchronous, so IAsyncEnumerable<T> is also
			// synchronously constructible (it wraps the same members in the __AsyncArray<T> replay enumerator), and
			// is offered by type alongside the synchronous shapes. An async-member collection has no synchronous
			// materialization - its IAsyncEnumerable<T> shape is an asynchronous arm instead (AsyncByTypeCollections).
			// An explicitly registered IAsyncEnumerable<T> claims the slot instead; the seen guard alone would not
			// hold it when that registration is async-tainted (excluded from the sync dispatch that seeds seen).
			if (!AsyncShapeRegistered(serviceToIndex, collection.Service))
			{
				string asyncArray = AsyncCollectionExpression(collectionKey, names, instances, asynchronous: false);
				AddCollectionShape(AwaitenGenerator.AsyncEnumerableShapeType(collection.Service), asyncArray, rootWithheld, entries, seen);
			}
		}
	}

	/// <summary>
	///     Adds the public dispatch entries for the awaited-collection view - every <c>Task&lt;C&gt;</c> shape - of
	///     each unkeyed, non-synthesis-suppressed collection. Unlike the synchronous shapes and the
	///     <c>IAsyncEnumerable&lt;T&gt;</c> view, the awaited collection is ALWAYS synchronously obtainable: it
	///     produces a <c>Task&lt;C&gt;</c> (a completed <c>Task.FromResult</c> when every member is synchronous, an
	///     already-started task that awaits its async-tainted members otherwise), laundering their taint exactly as
	///     an injected awaited collection does - so it joins the synchronous dispatch even for a collection whose
	///     members are async-tainted, and <see cref="Names.IsSyncCollection" /> is not consulted. By-type resolution
	///     has no ambient token, so awaited async members receive <c>default</c> (mirroring an awaited collection
	///     injected into a synchronously built consumer). An explicitly registered <c>Task&lt;C&gt;</c> shape owns
	///     its own slot (the <paramref name="seen" /> guard, seeded from the explicit registrations); a registered
	///     synchronous shape suppresses the whole element, all-or-nothing, exactly as on the injection side. The
	///     root-withholding of a build-on-demand disposable member applies here too: materializing the awaited
	///     collection by type off the Root would accumulate its members for the container's lifetime, so it is
	///     withheld from the Root (resolvable from a child scope, which bounds them).
	/// </summary>
	private static void AddAwaitedCollectionEntries(
		InstanceModel[] instances,
		Names names,
		Dictionary<ServiceKey, int> serviceToIndex,
		IReadOnlyDictionary<ServiceKey, List<int>> collectionMembers,
		bool strict,
		List<DispatchEntry> entries,
		HashSet<string> seen)
	{
		foreach (ServiceMembers collection in names.Collections)
		{
			ServiceKey collectionKey = new(collection.Service, collection.Key);

			// A keyed collection has no by-type surface; a synthesis-suppressed element is served by its explicit
			// registration. An async-tainted member does NOT withhold the awaited view (it awaits that member behind
			// the returned task), so - unlike AddCollectionEntries - IsSyncCollection is deliberately not consulted.
			if (collection.Key is not null || SynthesisSuppressed(serviceToIndex, collection.Service))
			{
				continue;
			}

			bool rootWithheld = collectionMembers.TryGetValue(collectionKey, out List<int>? members)
			                    && members.Any(member => IsFuncWithheld(instances, member, serviceToIndex, collectionMembers, strict));

			foreach (string shape in AwaitenGenerator.CollectionShapeTypes(collection.Service))
			{
				string awaitedType = $"global::System.Threading.Tasks.Task<{shape}>";

				// A registered Task<C> of this exact shape (its bare entry seeded 'seen') owns the slot; the sibling
				// shapes still synthesize, matching the injection-side awaitedShapeRegistered gate.
				if (!seen.Add(awaitedType))
				{
					continue;
				}

				string value = AwaitedCollectionExpression(collectionKey, shape, names, instances, asynchronous: false);
				entries.Add(rootWithheld
					? new DispatchEntry(awaitedType, value, AwaitedCollectionWithheldMessage(awaitedType))
					: new DispatchEntry(awaitedType, value));
			}
		}
	}

	// Under all-or-nothing synthesis, an element type with any collection shape explicitly registered (unkeyed) is
	// not synthesized at all - the registered shape is dispatched as an ordinary service and the other shapes are
	// unresolvable. Mirrors the injection-side suppression in AwaitenGenerator.ClassifyParameters.
	private static bool SynthesisSuppressed(Dictionary<ServiceKey, int> serviceToIndex, string elementType)
		=> AwaitenGenerator.CollectionShapeTypes(elementType).Any(shape => serviceToIndex.ContainsKey(new ServiceKey(shape, null)));

	// A registered IAsyncEnumerable<T> claims only its own async shape: the synthesized async view steps aside for
	// it (on the sync dispatch, the async arm and the withheld guidance alike) while the synchronous shapes stay
	// synthesized. Mirrors the injection-side asyncShapeRegistered gate in AwaitenGenerator.ClassifyParameters.
	private static bool AsyncShapeRegistered(Dictionary<ServiceKey, int> serviceToIndex, string elementType)
		=> serviceToIndex.ContainsKey(new ServiceKey(AwaitenGenerator.AsyncEnumerableShapeType(elementType), null));

	// Adds one collection shape's dispatch entry, unless an explicit registration already claimed the slot (the
	// seen guard). A root-withheld collection carries the guidance thrown by Resolve(Type) on the Root.
	private static void AddCollectionShape(
		string serviceType, string array, bool rootWithheld, List<DispatchEntry> entries, HashSet<string> seen)
	{
		if (seen.Add(serviceType))
		{
			entries.Add(rootWithheld
				? new DispatchEntry(serviceType, array, CollectionWithheldMessage(serviceType))
				: new DispatchEntry(serviceType, array));
		}
	}

	/// <summary>
	///     Adds the directly-dispatchable entry for each of an instance's service types: a parameterized
	///     service contributes only its <c>Func&lt;TArg…, T&gt;</c> factory (its bare type cannot be built
	///     without the runtime arguments); any other service maps to its resolver call.
	/// </summary>
	private static void AddServiceEntries(InstanceModel instance, string resolver, List<DispatchEntry> entries, HashSet<string> seen, bool bareWithheld, bool funcWithheld)
	{
		bool rootOwned = IsRootOwned(instance);
		string[] argTypes = instance.ArgTypes();
		foreach (ServiceKey serviceKey in instance.Services.AsArray())
		{
			// Keyed registrations are reached only by [FromKey] injection, never the public dispatch table.
			if (serviceKey.Key is not null)
			{
				continue;
			}

			string service = serviceKey.Service;
			if (argTypes.Length > 0)
			{
				// The plain Func<TArg…, T> factory accumulates on its owner; under strict safety it is root-withheld
				// (resolving it by type off the Root throws guidance, steering to the Owned form), but it stays
				// resolvable from a child scope, where the disposables it builds are bounded by the scope.
				string parameterizedFunc = $"global::System.Func<{string.Join(", ", argTypes)}, {service}>";
				seen.Add(parameterizedFunc);
				entries.Add(funcWithheld
					? new DispatchEntry(parameterizedFunc, FuncFactory(argTypes, service, resolver), FuncWithheldMessage(service))
					: new DispatchEntry(parameterizedFunc, FuncFactory(argTypes, service, resolver)));

				// The leak-free factory for a parameterized service: Func<TArg…, Owned<T>> hands each built
				// instance back as a disposal handle instead of accumulating it on the owner.
				string parameterizedOwnedFunc = $"global::System.Func<{string.Join(", ", argTypes)}, global::Awaiten.Owned<{service}>>";
				seen.Add(parameterizedOwnedFunc);
				entries.Add(new DispatchEntry(parameterizedOwnedFunc, OwnedFuncFactory(argTypes, service, resolver, rootOwned)));
				continue;
			}

			seen.Add(service);
			// A strictly-withheld disposable transient's bare type is root-withheld: resolving it by type off the
			// Root throws guidance (steering to injection or Owned<T>), but it stays resolvable from a child scope,
			// which bounds its lifetime. A non-disposable service is not withheld at the bare type even when its
			// Func is (its single bare resolution is bounded).
			entries.Add(bareWithheld
				? new DispatchEntry(service, resolver + "()", BareWithheldMessage(service), resolver)
				: new DispatchEntry(service, resolver + "()", directResolver: resolver));
		}
	}

	/// <summary>
	///     Adds the synthetic <c>Func&lt;T&gt;</c> and <c>Lazy&lt;T&gt;</c> entries over each of an instance's
	///     service types, skipping any key an explicit registration already claimed (tracked in
	///     <paramref name="seen" />) so the dispatch table never contains a duplicate key.
	/// </summary>
	private static void AddRelationshipEntries(InstanceModel instance, string resolver, List<DispatchEntry> entries, HashSet<string> seen, bool funcWithheld)
	{
		bool rootOwned = IsRootOwned(instance);
		foreach (ServiceKey serviceKey in instance.Services.AsArray())
		{
			// Keyed registrations are reached only by [FromKey] injection, so they get no synthetic
			// relationship entries either.
			if (serviceKey.Key is not null)
			{
				continue;
			}

			string service = serviceKey.Service;

			// The plain Func<T> factory accumulates on its owner; under strict safety it is root-withheld
			// (resolving it by type off the Root throws guidance, steering to Func<Owned<T>>), but it stays
			// resolvable from a child scope, where the disposables it builds are bounded by the scope.
			string func = $"global::System.Func<{service}>";
			if (seen.Add(func))
			{
				entries.Add(funcWithheld
					? new DispatchEntry(func, $"new global::System.Func<{service}>(() => {resolver}())", FuncWithheldMessage(service))
					: new DispatchEntry(func, $"new global::System.Func<{service}>(() => {resolver}())"));
			}

			// Lazy<T> is memoized - it builds at most once and never accumulates - so it stays resolvable even
			// for a withheld disposable service.
			string lazy = $"global::System.Lazy<{service}>";
			if (seen.Add(lazy))
			{
				entries.Add(new DispatchEntry(lazy, $"new global::System.Lazy<{service}>(() => {resolver}())"));
			}

			// Owned<T> hands the caller a disposal handle over a single resolution; Func<Owned<T>> is the
			// leak-free factory that produces one per call. Both build into a throwaway child scope, and both
			// stay resolvable under strict safety - they are the sanctioned way to reach a withheld service.
			string owned = $"global::Awaiten.Owned<{service}>";
			if (seen.Add(owned))
			{
				entries.Add(new DispatchEntry(owned, OwnedBare(service, resolver, rootOwned)));
			}

			string ownedFunc = $"global::System.Func<{owned}>";
			if (seen.Add(ownedFunc))
			{
				entries.Add(new DispatchEntry(ownedFunc, OwnedFuncFactory([], service, resolver, rootOwned)));
			}
		}
	}

	/// <summary>
	///     Emits the data-driven by-type dispatch: a nested <c>__Bucket</c> slot type, the static
	///     <c>__buckets</c> / <c>__bucketSize</c> table (built once in the static constructor and shared by every
	///     scope and the <c>Root</c>), the <c>__BuildBuckets</c> distributor, and a <c>__R</c> forwarder per
	///     compound entry. It lives on the base <c>Scope</c>; <c>TryResolve</c> probes the table by Type identity
	///     hash. The table grows in data, never in a single method's IL, so it cannot hit the JIT optimization
	///     guards no matter how many registrations there are.
	/// </summary>
	private static void EmitBucketDispatch(StringBuilder builder, int depth, List<DispatchEntry> entries)
	{
		int bucketCount = BucketCount(entries.Count);

		// Assign a forwarder to each UNIQUE compound value expression. Identical values share one __R method rather
		// than emitting a body each - notably the six collection shapes of one element type (IEnumerable<T>,
		// IReadOnlyList<T>, IReadOnlyCollection<T>, IList<T>, ICollection<T>, T[]) all materialize the very same
		// array, so they collapse from six method bodies to one. Bare services bind a direct delegate and need none.
		Dictionary<string, int> forwarderOf = new(StringComparer.Ordinal);
		List<string> forwarders = new();
		foreach (string value in entries.Where(entry => entry.DirectResolver is null && !forwarderOf.ContainsKey(entry.Value)).Select(entry => entry.Value))
		{
			forwarderOf[value] = forwarders.Count;
			forwarders.Add(value);
		}

		// One table slot: the key type, its resolver delegate, and whether the slot is withheld from by-type
		// resolution on the Root. A default slot (Key == null) is an empty probe cell and never matches a request.
		AppendXmlSummary(builder, depth, "One slot of the by-type dispatch table.");
		Indent(builder, depth).AppendLine("private readonly struct __Bucket");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("public readonly global::System.Type? Key;");
		Indent(builder, depth + 1).AppendLine("public readonly global::System.Func<Scope, object> Resolve;");
		Indent(builder, depth + 1).AppendLine("public readonly bool RootWithheld;");
		Indent(builder, depth + 1).AppendLine("public __Bucket(global::System.Type? key, global::System.Func<Scope, object> resolve, bool rootWithheld)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("Key = key;");
		Indent(builder, depth + 2).AppendLine("Resolve = resolve;");
		Indent(builder, depth + 2).AppendLine("RootWithheld = rootWithheld;");
		Indent(builder, depth + 1).AppendLine("}");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();

		Indent(builder, depth).Append("private const int __bucketCount = ").Append(bucketCount).AppendLine(";");
		Indent(builder, depth).AppendLine("private static readonly __Bucket[] __buckets;");
		Indent(builder, depth).AppendLine("private static readonly int __bucketSize;");
		builder.AppendLine();

		// The static ctor lists every entry with its resolver delegate, then distributes them into fixed-width
		// buckets by the runtime Type identity hash (unknown at generation time). A bare service binds a direct
		// delegate to its existing virtual resolver; a compound value routes through a __R forwarder that preserves
		// the expression verbatim, so all owner-context construction (throwaway scopes, __root) is unchanged.
		Indent(builder, depth).AppendLine("static Scope()");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("__Bucket[] __entries =");
		Indent(builder, depth + 1).AppendLine("{");
		for (int i = 0; i < entries.Count; i++)
		{
			string resolve = entries[i].DirectResolver is { } direct
				? $"static __s => __s.{direct}()"
				: $"static __s => __s.__R{forwarderOf[entries[i].Value]}()";
			Indent(builder, depth + 2).Append("new __Bucket(typeof(").Append(entries[i].Type).Append("), ")
				.Append(resolve).Append(", ").Append(entries[i].RootWithheld ? "true" : "false").AppendLine("),");
		}

		Indent(builder, depth + 1).AppendLine("};");
		Indent(builder, depth + 1).AppendLine("__buckets = __BuildBuckets(__entries);");
		Indent(builder, depth + 1).AppendLine("__bucketSize = __buckets.Length / __bucketCount;");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();

		AppendXmlSummary(builder, depth, "Builds the by-type dispatch table.");
		Indent(builder, depth).AppendLine("private static __Bucket[] __BuildBuckets(__Bucket[] __entries)");
		Indent(builder, depth).AppendLine("{");
		EmitBucketDistribution(builder, depth + 1, "__Bucket", "__bucketCount");
		Indent(builder, depth).AppendLine("}");

		// One forwarder per unique compound value, preserving the value expression verbatim. Each is tiny and
		// individually optimizable; deduplication (above) keeps the six collection shapes of an element type to one.
		for (int k = 0; k < forwarders.Count; k++)
		{
			builder.AppendLine();
			Indent(builder, depth).Append("private object __R").Append(k).Append("() => ").Append(forwarders[k]).AppendLine(";");
		}
	}

	/// <summary>
	///     Emits the shared bucket-distribution body used by both the synchronous and the async table builders:
	///     distributes the local <c>__entries</c> into the fixed number of buckets by Type identity hash, sizing
	///     every bucket to the largest collision count so a lookup probes a small constant window, and returns the
	///     table. Runs once at static initialization (the Type hashes are only known at runtime). The window size
	///     is derived by the caller as table length / bucket count.
	/// </summary>
	private static void EmitBucketDistribution(StringBuilder builder, int depth, string slotType, string countConst)
	{
		Indent(builder, depth).Append("int[] __counts = new int[").Append(countConst).AppendLine("];");
		Indent(builder, depth).Append("foreach (").Append(slotType).AppendLine(" __e in __entries)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).Append("__counts[(int)((uint)__e.Key!.TypeHandle.GetHashCode() % (uint)").Append(countConst).AppendLine(")]++;");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth).AppendLine("int __max = 1;");
		Indent(builder, depth).AppendLine("foreach (int __c in __counts)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("if (__c > __max) { __max = __c; }");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth).Append(slotType).Append("[] __arr = new ").Append(slotType).Append("[").Append(countConst).AppendLine(" * __max];");
		Indent(builder, depth).Append("int[] __fill = new int[").Append(countConst).AppendLine("];");
		Indent(builder, depth).Append("foreach (").Append(slotType).AppendLine(" __e in __entries)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).Append("int __b = (int)((uint)__e.Key!.TypeHandle.GetHashCode() % (uint)").Append(countConst).AppendLine(");");
		Indent(builder, depth + 1).AppendLine("__arr[(__b * __max) + __fill[__b]++] = __e;");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth).AppendLine("return __arr;");
	}

	/// <summary>
	///     The bucket count for a table of <paramref name="entryCount" /> entries: the smallest power of two that
	///     keeps the load factor at or below one half, so collisions - and thus the fixed probe window - stay small.
	///     A power of two lets the runtime lower the <c>% __bucketCount</c> in the probe to a bitwise-and.
	/// </summary>
	private static int BucketCount(int entryCount)
	{
		int target = entryCount * 2;
		int count = 4;
		while (count < target)
		{
			count <<= 1;
		}

		return count;
	}

	/// <summary>
	///     Emits the <c>__ResolveExternal</c> helper that routes a <c>[FromServices]</c> / <c>[ImportServices]</c>
	///     dependency (optionally under a <c>[FromKey]</c> key) through the external resolver, throwing a clear
	///     message when no resolver is wired or the service is unavailable. Emitted on the base <c>Scope</c> and
	///     inherited by the <c>Root</c>. It prefers this scope's own <c>ExternalResolver</c> (a host wires each
	///     scope to its aligned provider, so a scoped external dependency resolves per scope) and falls back to
	///     the root's (<c>__root.ExternalResolver</c>; on the Root the two are the same).
	/// </summary>
	private static void EmitResolveExternal(StringBuilder builder, int depth)
	{
		Indent(builder, depth).AppendLine("protected object __ResolveExternal(global::System.Type serviceType, object? serviceKey)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("global::Awaiten.IExternalResolver? resolver = ExternalResolver ?? __root.ExternalResolver;");
		Indent(builder, depth + 1).AppendLine("if (resolver != null && resolver.TryResolve(serviceType, serviceKey, out object? instance) && instance != null)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return instance;");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		// Name the [FromKey] key in the message when there is one, so a keyed miss is not mistaken for a
		// missing unkeyed registration.
		Indent(builder, depth + 1).AppendLine(
			"throw new global::System.InvalidOperationException($\"Awaiten: the external dependency '{serviceType}'{(serviceKey == null ? \"\" : $\" (key: {serviceKey})\")} is not available; register it in the host provider or set ExternalResolver.\");");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     The distinct external (<c>[FromServices]</c> / <c>[ImportServices]</c>) service types across every
	///     instance's constructor parameters, in first-seen order - advertised by the Root as
	///     <c>ExternalDependencies</c> and used to decide whether the external-resolution surface is emitted.
	/// </summary>
	private static string[] ExternalDependencies(InstanceModel[] instances)
	{
		List<string> external = new();
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (InstanceModel instance in instances)
		{
			foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
			{
				if (parameter.Kind == DependencyKind.External && seen.Add(parameter.ServiceType))
				{
					external.Add(parameter.ServiceType);
				}
			}
		}

		return external.ToArray();
	}

	private static bool HasExternalDependencies(InstanceModel[] instances) => ExternalDependencies(instances).Length > 0;

	/// <summary>
	///     A single dispatch case: the service <see cref="Type" /> requested and the <see cref="Value" />
	///     assigned to <c>instance</c> (the resolver call). Every entry is dispatchable - it always carries a
	///     real resolver. When <see cref="Guidance" /> is set the entry is additionally <em>root-withheld</em>
	///     under strict lifetime safety: it resolves normally from a child scope (where its lifetime is bounded
	///     by the scope), but the <c>__rootWithheld</c> mask makes <c>TryResolve</c> return
	///     <see langword="false" /> for it on the Root and <c>Resolve</c> throw the <see cref="Guidance" />
	///     message (placed in the <c>__withheld</c> table) - so the root-accumulation leak stays impossible
	///     while the safe scope-bound resolution is allowed.
	/// </summary>
	private readonly struct DispatchEntry(string type, string value, string? guidance = null, string? directResolver = null)
	{
		public string Type { get; } = type;

		public string Value { get; } = value;

		public string? Guidance { get; } = guidance;

		// A bare-service entry's value is a plain `resolver()` call over the current owner; the resolver method name
		// is kept so the bucket table can bind a direct delegate (__s => __s.ResolveX()) to the existing virtual
		// resolver instead of routing through a per-entry forwarder. Null for compound values (Func/Lazy/Owned/
		// collection literals), which need their expression preserved verbatim in a __R forwarder.
		public string? DirectResolver { get; } = directResolver;

		public bool RootWithheld => Guidance is not null;
	}
}
