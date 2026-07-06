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
	///     Each registered service type paired with the instance that produces it, deduplicated on the service type
	///     so the typed base list and its explicit implementations stay in lockstep (emitting either twice would
	///     fail to compile). Parameterized services (reachable only through their <c>Func&lt;TArg…, T&gt;</c>) and
	///     keyed registrations (reached only by <c>[FromKey]</c>) are excluded, since neither has a typed fast path.
	/// </summary>
	private static IEnumerable<(string Service, int Index)> UniqueServices(InstanceModel[] instances, bool strict, bool syncResolveAfterInit)
	{
		HashSet<string> seen = new(StringComparer.Ordinal);
		for (int i = 0; i < instances.Length; i++)
		{
			// No typed fast path for: parameterized services, strictly-withheld disposables (reached only through
			// Owned<T>), async-tainted services in the strict default (reached only through ResolveAsync), and
			// requesting-type factories (the typed Resolve<T>() has nowhere to supply the requesting type, so they
			// go through the Type-based dispatch, which passes null).
			if (instances[i].IsParameterized || instances[i].IsRequestingTypeFactory || IsWithheld(instances[i], strict) || !EmitsSync(instances[i], syncResolveAfterInit))
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
			// An instance method on the Scope, resolving over `this`: a singleton through its Root-hosted resolver
			// over the root, a scoped/transient through its Scope-hosted resolver over this scope.
			if (IsRootOwned(instances[index]))
			{
				// A singleton's static resolver guards the root, not this scope, so guard `this` here (a block
				// body, unlike the self-guarding scoped/transient impl below) so resolving from a disposed scope
				// throws. The generic Resolve<T> fast path takes this route, bypassing the Type-based TryResolve guard.
				Indent(builder, depth).Append(service).Append(" global::Awaiten.IAwaitenResolver<")
					.Append(service).AppendLine(">.Resolve()");
				Indent(builder, depth).AppendLine("{");
				EmitDisposedGuard(builder, depth + 1);
				Indent(builder, depth + 1).Append("return Root.").Append(names.Resolver(index)).AppendLine("(__root);");
				Indent(builder, depth).AppendLine("}");
				continue;
			}

			Indent(builder, depth).Append(service).Append(" global::Awaiten.IAwaitenResolver<")
				.Append(service).Append(">.Resolve() => ").Append(names.Resolver(index)).AppendLine("(this);");
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
	///     Emits the compile-time, reflection-free <c>IAwaitenContainerMetadata.Registrations</c> list on the Root:
	///     public (unkeyed) registrations with their lifetimes, which the
	///     <c>Awaiten.Extensions.DependencyInjection</c> companion projects into a service collection. Parameterized
	///     services are omitted (not resolvable by service type alone); a user-keyed registration is advertised with
	///     its <c>[Key]</c>, while the synthetic decorator/contextual keys stay internal. An async-tainted service is
	///     flagged <c>requiresAsync</c> so the bridge projects it as a <c>Task&lt;T&gt;</c>, unless
	///     <paramref name="syncResolveAfterInit" /> makes it synchronously resolvable after warm-up.
	/// </summary>
	private static void EmitRegistrations(StringBuilder fields, StringBuilder members, int depth, InstanceModel[] instances, bool syncResolveAfterInit)
	{
		// The registrations are compile-time constants for the container type, so they live in one static
		// array rather than being rebuilt per Root construction.
		StringBuilder builder = fields;
		Separate(fields);
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
			// The synthetic decorator/contextual keys are internal wiring, never advertised. Unkeyed and
			// user-keyed registrations are both advertised, the latter carrying its user-declared [Key].
			foreach (ServiceKey service in instance.Services.AsArray()
				         .Where(service => service.Key is null || AwaitenGenerator.IsUserKey(service.Key)))
			{
				EmitRegistrationEntry(builder, depth + 1, service, instance, requiresAsync, externallyOwned);
			}
		}

		Indent(builder, depth).AppendLine("};");
		Separate(members);
		Indent(members, depth).AppendLine("global::System.Collections.Generic.IReadOnlyList<global::Awaiten.AwaitenRegistration> global::Awaiten.IAwaitenContainerMetadata.Registrations => __registrations;");
	}

	/// <summary>
	///     Emits one <c>AwaitenRegistration</c> initializer: the service type and lifetime, then the async <c>Task&lt;T&gt;</c>
	///     projection (for an async-tainted registration) or the <c>externallyOwned</c> flag, and finally the
	///     user-declared <c>[Key]</c> for a keyed registration.
	/// </summary>
	private static void EmitRegistrationEntry(StringBuilder builder, int depth, ServiceKey service, InstanceModel instance, bool requiresAsync, bool externallyOwned)
	{
		Indent(builder, depth).Append("new global::Awaiten.AwaitenRegistration(typeof(").Append(service.Service)
			.Append("), ").Append(AwaitenLifetimeOf(instance.Lifetime));
		if (requiresAsync)
		{
			// The async ctor carries the closed generics the bridge projects with, so it never
			// constructs Task<TService> or the Task<object>->Task<T> converter reflectively.
			builder.Append(", typeof(global::System.Threading.Tasks.Task<").Append(service.Service).Append(">), ")
				.Append("__t => global::Awaiten.AwaitenTaskProjection.AsTask<").Append(service.Service).Append(">(__t)");
		}
		else if (externallyOwned)
		{
			builder.Append(", externallyOwned: true");
		}

		// A user-keyed registration advertises its [Key] as the last (named) argument on whichever ctor.
		if (service.Key is not null)
		{
			builder.Append(", key: ").Append(AwaitenGenerator.KeyLiteral(service.Key));
		}

		builder.AppendLine("),");
	}

	/// <summary>
	///     Emits the Root's advertised <c>ExternalDependencies</c> list (the distinct <c>[FromServices]</c> /
	///     <c>[ImportServices]</c> service types, empty when there are none) that <c>IAwaitenContainerMetadata</c>
	///     requires. The <c>ExternalResolver</c> the container routes those dependencies through is emitted on the
	///     base <c>Scope</c> (inherited by the Root), so a host can wire each scope independently.
	/// </summary>
	private static void EmitExternalMetadata(StringBuilder fields, StringBuilder members, int depth, InstanceModel[] instances)
	{
		(string Type, string? Key)[] external = ExternalDependencies(instances);
		Separate(members);
		if (external.Length == 0)
		{
			Indent(members, depth).AppendLine(
				"global::System.Collections.Generic.IReadOnlyList<global::Awaiten.AwaitenExternalDependency> global::Awaiten.IAwaitenContainerMetadata.ExternalDependencies => global::System.Array.Empty<global::Awaiten.AwaitenExternalDependency>();");
			return;
		}

		Separate(fields);
		Indent(fields, depth).AppendLine("private static readonly global::Awaiten.AwaitenExternalDependency[] __externalDependencies =");
		Indent(fields, depth).AppendLine("{");
		foreach ((string type, string? key) in external)
		{
			Indent(fields, depth + 1).Append("new global::Awaiten.AwaitenExternalDependency(typeof(").Append(type).Append("), ")
				.Append(ExternalKeyLiteral(key)).AppendLine("),");
		}

		Indent(fields, depth).AppendLine("};");
		Indent(members, depth).AppendLine(
			"global::System.Collections.Generic.IReadOnlyList<global::Awaiten.AwaitenExternalDependency> global::Awaiten.IAwaitenContainerMetadata.ExternalDependencies => __externalDependencies;");
	}

	private static string AwaitenLifetimeOf(Lifetime lifetime) => lifetime switch
	{
		Lifetime.Singleton => "global::Awaiten.AwaitenLifetime.Singleton",
		Lifetime.Transient => "global::Awaiten.AwaitenLifetime.Transient",
		_ => "global::Awaiten.AwaitenLifetime.Scoped",
	};

	private static void EmitResolutionApi(ApiRegions regions, int depth, EmitContext context, bool strict, bool syncResolveAfterInit, string[] varianceCandidates)
	{
		(StringBuilder members, StringBuilder fields, StringBuilder helpers) = regions;
		InstanceModel[] instances = context.Instances;
		Names names = context.Names;
		Dictionary<ServiceKey, int> serviceToIndex = context.ServiceToIndex;
		StringBuilder builder = members;
		Separate(members);

		List<DispatchEntry> entries = BuildDispatchEntries(instances, names, serviceToIndex, strict, syncResolveAfterInit);
		// The runtime variance-fallback candidates: the registered variant closed-generic-interface service
		// types that are actually by-type dispatchable, in registration order. A candidate excluded from the
		// synchronous dispatch (async-tainted under the strict default, or failed to build) is dropped. The
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
		// A disposed scope rejects every by-type resolution (including a root-owned singleton reached through
		// Root.ResolveX(__s.__root)): the per-resolver guards only see the target's owner, so the resolving
		// scope's own disposal is enforced here, the shared entry the public Resolve(Type) also flows through.
		EmitDisposedGuard(builder, depth + 1);
		if (entries.Count == 0)
		{
			Indent(builder, depth + 1).AppendLine("instance = null;");
			Indent(builder, depth + 1).AppendLine("return false;");
			Indent(builder, depth).AppendLine("}");
			return;
		}

		// Open-addressed probe: hash the requested type into its bucket window and scan the (small, fixed-width)
		// window for an identity match. Each slot carries its resolver delegate directly, so a hit invokes it with
		// no switch, so the dispatch is O(1) and constant-size in IL regardless of the registration count. Matching
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
			// exact-match fast path above is untouched. The fallback only ever runs on what would otherwise
			// be a failed resolution.
			Indent(builder, depth + 1).AppendLine("return __TryResolveVariant(serviceType, out instance);");
			Indent(builder, depth).AppendLine("}");
			EmitVarianceFallback(fields, helpers, depth, varianceEntries, hasWithheld);
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
	///     Emits the runtime variance fallback <c>TryResolve</c> consults after the exact-match probe misses: a
	///     closed generic interface with no bucket entry is satisfied by the nearest variance-compatible registered
	///     service, using the same conversion rule and nearest-wins selection as the compile-time redirect, so
	///     imperative and injected resolution agree even for a closed type no consumer ever requested. Successful
	///     routes are memoized in <c>__varianceRoutes</c>; failures are not (they throw anyway, and junk types must
	///     not grow the cache). A type with withheld guidance keeps its targeted error instead of being routed.
	/// </summary>
	private static void EmitVarianceFallback(StringBuilder fields, StringBuilder helpers, int depth, List<string> candidates, bool hasWithheld)
	{
		// The candidate service types, in registration order, mirroring the compile-time candidate order so
		// the registration-order tie-break picks the same target at runtime.
		Separate(fields);
		Indent(fields, depth).AppendLine("private static readonly global::System.Type[] __varianceCandidates = new global::System.Type[]");
		Indent(fields, depth).AppendLine("{");
		foreach (string candidate in candidates)
		{
			Indent(fields, depth + 1).Append("typeof(").Append(candidate).AppendLine("),");
		}

		Indent(fields, depth).AppendLine("};");
		fields.AppendLine();
		Indent(fields, depth).AppendLine(
			"private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::System.Type> __varianceRoutes = new global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::System.Type>();");

		StringBuilder builder = helpers;
		Separate(helpers);
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
		// definition and an instance of it IS-A the request. IsAssignableFrom is exactly the identity-or-
		// implicit-reference-conversion check C# variance defines (a value-type argument never converts).
		Indent(builder, depth + 2).AppendLine("if ((object)__candidate == (object)serviceType");
		Indent(builder, depth + 2).AppendLine("    || __candidate.GetGenericTypeDefinition() != __definition");
		Indent(builder, depth + 2).AppendLine("    || !serviceType.IsAssignableFrom(__candidate))");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("continue;");
		Indent(builder, depth + 2).AppendLine("}");
		builder.AppendLine();
		// Nearest candidate wins: replace the current best when it converts to the candidate, falling back
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

		CollectionMembership membership = AwaitenGenerator.MembershipIndices(names.Collections, names.KeyedCollections, implToIndex);

		// Explicit service registrations first, so a directly registered relationship type (e.g. a
		// registered Lazy<T>) wins the dispatch slot over the synthetic relationship entry below. Service
		// types are unique across instances (registrations are coalesced), so each is added exactly once.
		// Seeding 'seen' here lets the synthetic pass skip any key an explicit registration already claimed.
		// An async-tainted service in the strict default is excluded from synchronous dispatch entirely. It
		// is reachable only through ResolveAsync, so neither its bare type nor any relationship over it is added.
		for (int i = 0; i < instances.Length; i++)
		{
			if (!EmitsSync(instances[i], syncResolveAfterInit))
			{
				continue;
			}

			AddServiceEntries(instances[i], names.Resolver(i), entries, seen, IsWithheld(instances[i], strict), IsFuncWithheld(instances, i, serviceToIndex, membership, strict));
		}

		// Synthetic Func<T>/Lazy<T> over each service, skipping any key an explicit registration already
		// claimed so the static dispatch table never contains a duplicate key (which would otherwise throw
		// from the dictionary initializer at runtime). A parameterized service is skipped: its only entry is
		// the Func<TArg…, T> added above.
		for (int i = 0; i < instances.Length; i++)
		{
			if (!instances[i].IsParameterized && EmitsSync(instances[i], syncResolveAfterInit))
			{
				AddRelationshipEntries(instances[i], names.Resolver(i), entries, seen, IsFuncWithheld(instances, i, serviceToIndex, membership, strict));
			}
		}

		// Collection dispatch: every unkeyed, sync-materializable collection is publicly resolvable as all six
		// shapes (IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T>, IList<T>, ICollection<T>, T[]). The
		// eagerly materialized array satisfies each. A keyed collection is reached only by [FromKey] injection; an
		// element type whose collection was explicitly registered is not synthesized (all-or-nothing); a collection
		// with an async-tainted member has no synchronous materialization (its shapes throw AWT122-style guidance
		// via __withheld, and injecting one is AWT122). The seen guard is belt-and-braces against a slot an
		// explicit registration already claimed.
		AddCollectionEntries(instances, names, serviceToIndex, membership, strict, entries, seen);

		// The awaited-collection view (every Task<C> shape) joins the synchronous dispatch too: it always hands
		// back a Task synchronously, even for a collection whose members are async-tainted (it awaits them behind
		// the task), so it is offered by type alongside the synchronous shapes rather than through an async arm.
		AddAwaitedCollectionEntries(instances, names, serviceToIndex, membership, strict, entries, seen);

		// Keyed-collection dispatch: every keyed-collection-resolvable service is publicly resolvable as
		// IReadOnlyDictionary<string, T>, materialized synchronously from its keyed members' resolvers (like the
		// synchronous collection shapes). A keyed collection with an async-tainted member has no synchronous
		// materialization (injecting one is AWT122), so it is omitted.
		AddKeyedCollectionEntries(instances, names, serviceToIndex, membership, strict, entries, seen);

		// The awaited keyed-dictionary view (Task<IReadOnlyDictionary<string, T>>) joins the synchronous dispatch
		// too, exactly like the awaited collection: it always hands back a Task synchronously, even for a dictionary
		// whose members are async-tainted (it awaits them behind the task), so it is offered by type rather than
		// through an async arm.
		AddAwaitedKeyedCollectionEntries(instances, names, serviceToIndex, membership, strict, entries, seen);

		return entries;
	}

	/// <summary>
	///     Adds the public dispatch entry for the awaited keyed-dictionary view
	///     (<c>Task&lt;IReadOnlyDictionary&lt;string, T&gt;&gt;</c>) of each keyed-collection-resolvable service.
	///     The awaited view is always synchronously obtainable (it hands back a <c>Task</c>, awaiting any
	///     async-tainted members behind it), so it joins the synchronous dispatch even when the synchronous shape
	///     cannot. By-type resolution has no ambient token, so awaited async members receive <c>default</c>. An
	///     explicitly registered <c>Task&lt;IReadOnlyDictionary&lt;…&gt;&gt;</c> owns its own slot (checked against
	///     the registrations directly, since <paramref name="seen" /> is seeded only from the synchronous dispatch),
	///     and a registered synchronous dictionary suppresses the awaited view all-or-nothing. A build-on-demand
	///     disposable member is withheld from the Root (resolvable from a child scope, which bounds it).
	/// </summary>
	private static void AddAwaitedKeyedCollectionEntries(
		InstanceModel[] instances,
		Names names,
		Dictionary<ServiceKey, int> serviceToIndex,
		CollectionMembership membership,
		bool strict,
		List<DispatchEntry> entries,
		HashSet<string> seen)
	{
		foreach (KeyedServiceMembers keyed in names.KeyedCollections)
		{
			// Mixed key kinds have no coherent by-type dictionary; only [FromKey] injection reaches those members.
			if (keyed.KeyType is null)
			{
				continue;
			}

			string service = keyed.Service;
			string dictionaryType = $"global::System.Collections.Generic.IReadOnlyDictionary<{keyed.KeyType}, {service}>";
			string awaitedType = $"global::System.Threading.Tasks.Task<{dictionaryType}>";

			// A registered synchronous dictionary suppresses the awaited view (all-or-nothing); a registered
			// Task<IReadOnlyDictionary<…>> of this exact shape owns its own slot. The seen guard alone would not
			// hold that slot when the registration is async-tainted (excluded from the sync dispatch that seeds
			// seen). The synthesized dictionary would silently shadow it and mask its ResolveAsync guidance, so
			// the registration is checked directly, mirroring AsyncShapeRegistered for IAsyncEnumerable<T>.
			if (serviceToIndex.ContainsKey(new ServiceKey(dictionaryType, null))
			    || serviceToIndex.ContainsKey(new ServiceKey(awaitedType, null))
			    || !seen.Add(awaitedType))
			{
				continue;
			}

			bool rootWithheld = membership.Keyed.TryGetValue(service, out List<int>? members)
			                    && members.Any(member => IsFuncWithheld(instances, member, serviceToIndex, membership, strict));

			string value = AwaitedKeyedCollectionExpression(service, keyed.KeyType, names, instances, asynchronous: false);
			entries.Add(rootWithheld
				? new DispatchEntry(awaitedType, value, AwaitedKeyedCollectionWithheldMessage(awaitedType))
				: new DispatchEntry(awaitedType, value));
		}
	}

	/// <summary>
	///     Adds the public dispatch entry for each keyed-collection-resolvable service, resolvable by type as
	///     <c>IReadOnlyDictionary&lt;string, T&gt;</c>. The dictionary is materialized synchronously from its keyed
	///     members' resolvers, mirroring the synchronous collection dispatch. A keyed collection with an
	///     async-tainted member has no synchronous materialization (injecting one is AWT122) and is omitted. Like a
	///     synchronous collection, one whose members include a build-on-demand disposable is root-withheld under
	///     strict lifetime safety. Materializing it by type off the Root would accumulate those disposables for the
	///     container's lifetime, so it is resolvable from a child scope but carries the withheld guidance on the
	///     Root. An explicitly registered dictionary of the exact type suppresses the synthesized entry outright
	///     (mirroring <see cref="SynthesisSuppressed" />): the registration is dispatched as an ordinary service, and
	///     no second dictionary is synthesized behind it even when the registration itself has no synchronous entry.
	///     The seen guard is belt-and-braces against a slot an explicit registration already claimed.
	/// </summary>
	private static void AddKeyedCollectionEntries(
		InstanceModel[] instances,
		Names names,
		Dictionary<ServiceKey, int> serviceToIndex,
		CollectionMembership membership,
		bool strict,
		List<DispatchEntry> entries,
		HashSet<string> seen)
	{
		foreach (KeyedServiceMembers keyed in names.KeyedCollections)
		{
			// Mixed key kinds have no coherent by-type dictionary; only [FromKey] injection reaches those members.
			if (keyed.KeyType is null)
			{
				continue;
			}

			string service = keyed.Service;
			string type = $"global::System.Collections.Generic.IReadOnlyDictionary<{keyed.KeyType}, {service}>";
			if (serviceToIndex.ContainsKey(new ServiceKey(type, null)) || !names.IsSyncKeyedCollection(service) || !seen.Add(type))
			{
				continue;
			}

			bool rootWithheld = membership.Keyed.TryGetValue(service, out List<int>? members)
			                    && members.Any(member => IsFuncWithheld(instances, member, serviceToIndex, membership, strict));

			string literal = KeyedCollectionLiteral(service, keyed.KeyType, names);
			entries.Add(rootWithheld
				? new DispatchEntry(type, literal, KeyedCollectionWithheldMessage(type))
				: new DispatchEntry(type, literal));
		}
	}

	/// <summary>
	///     Adds the public dispatch entries (all six collection shapes) for each unkeyed, sync-materializable
	///     collection whose synthesis is not suppressed by an explicit registration. A collection with a
	///     build-on-demand disposable member is root-withheld, since materializing it by type off the Root would
	///     accumulate those disposables for the container's lifetime. It stays resolvable from a child scope, which
	///     bounds its members. There is no <c>Owned&lt;T&gt;</c> form for a collection, so the guidance steers to a
	///     child scope, direct injection, or LifetimeSafety.Loose.
	/// </summary>
	private static void AddCollectionEntries(
		InstanceModel[] instances,
		Names names,
		Dictionary<ServiceKey, int> serviceToIndex,
		CollectionMembership membership,
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

			bool rootWithheld = membership.Collections.TryGetValue(collectionKey, out List<int>? members)
			                    && members.Any(member => IsFuncWithheld(instances, member, serviceToIndex, membership, strict));

			string array = CollectionLiteral(collectionKey, names, instances);
			foreach (string shape in AwaitenGenerator.CollectionShapeTypes(collection.Service))
			{
				AddCollectionShape(shape, array, rootWithheld, entries, seen);
			}

			// The async view of the same collection: every member is synchronous, so IAsyncEnumerable<T> is also
			// synchronously constructible (it wraps the same members in the __AsyncArray<T> replay enumerator), and
			// is offered by type alongside the synchronous shapes. An async-member collection has no synchronous
			// materialization; its IAsyncEnumerable<T> shape is an asynchronous arm instead (AsyncByTypeCollections).
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
	///     Adds the public dispatch entries for the awaited-collection view (every <c>Task&lt;C&gt;</c> shape) of
	///     each unkeyed, non-synthesis-suppressed collection. The awaited collection is always synchronously
	///     obtainable (it hands back a <c>Task&lt;C&gt;</c>, awaiting any async-tainted members behind it), so it
	///     joins the synchronous dispatch even when the synchronous shapes cannot. By-type resolution has no ambient
	///     token, so awaited async members receive <c>default</c>. An explicitly registered <c>Task&lt;C&gt;</c>
	///     shape owns its own slot, and a registered synchronous shape suppresses the whole element all-or-nothing.
	///     A build-on-demand disposable member is withheld from the Root (resolvable from a child scope, which bounds it).
	/// </summary>
	private static void AddAwaitedCollectionEntries(
		InstanceModel[] instances,
		Names names,
		Dictionary<ServiceKey, int> serviceToIndex,
		CollectionMembership membership,
		bool strict,
		List<DispatchEntry> entries,
		HashSet<string> seen)
	{
		foreach (ServiceMembers collection in names.Collections)
		{
			ServiceKey collectionKey = new(collection.Service, collection.Key);

			// A keyed collection has no by-type surface; a synthesis-suppressed element is served by its explicit
			// registration. An async-tainted member does NOT withhold the awaited view (it awaits that member behind
			// the returned task), so, unlike AddCollectionEntries, IsSyncCollection is deliberately not consulted.
			if (collection.Key is not null || SynthesisSuppressed(serviceToIndex, collection.Service))
			{
				continue;
			}

			bool rootWithheld = membership.Collections.TryGetValue(collectionKey, out List<int>? members)
			                    && members.Any(member => IsFuncWithheld(instances, member, serviceToIndex, membership, strict));

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

	/// <summary>
	///     Under all-or-nothing synthesis, an element type with any collection shape explicitly registered
	///     (unkeyed) is not synthesized at all. The registered shape is dispatched as an ordinary service and the
	///     other shapes are unresolvable. Mirrors the injection-side suppression in
	///     <c>AwaitenGenerator.ClassifyParameters</c>.
	/// </summary>
	private static bool SynthesisSuppressed(Dictionary<ServiceKey, int> serviceToIndex, string elementType)
		=> AwaitenGenerator.CollectionShapeTypes(elementType).Any(shape => serviceToIndex.ContainsKey(new ServiceKey(shape, null)));

	/// <summary>
	///     A registered <c>IAsyncEnumerable&lt;T&gt;</c> claims only its own async shape: the synthesized async
	///     view steps aside for it (on the sync dispatch, the async arm and the withheld guidance alike) while the
	///     synchronous shapes stay synthesized. Mirrors the injection-side <c>asyncShapeRegistered</c> gate in
	///     <c>AwaitenGenerator.ClassifyParameters</c>.
	/// </summary>
	private static bool AsyncShapeRegistered(Dictionary<ServiceKey, int> serviceToIndex, string elementType)
		=> serviceToIndex.ContainsKey(new ServiceKey(AwaitenGenerator.AsyncEnumerableShapeType(elementType), null));

	/// <summary>
	///     Adds one collection shape's dispatch entry, unless an explicit registration already claimed the slot
	///     (the <c>seen</c> guard). A root-withheld collection carries the guidance thrown by <c>Resolve(Type)</c>
	///     on the Root.
	/// </summary>
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

			// A requesting-type factory's resolver takes the requesting type; a top-level resolve has no
			// requesting consumer, so the by-type dispatch passes null (the factory decides what a null context
			// means). It is built fresh per call, so it routes through a forwarder (its call is not a bare
			// parameterless resolver), and has no Owned<T> form and no root-withholding. The factory itself may
			// dedup, exactly as the canonical logger factory does.
			if (instance.IsRequestingTypeFactory)
			{
				seen.Add(service);
				entries.Add(new DispatchEntry(service, $"{resolver}(__s, null)"));
				continue;
			}

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
				? new DispatchEntry(service, resolver + "()", BareWithheldMessage(service), resolver, rootOwned)
				: new DispatchEntry(service, resolver + "()", directResolver: resolver, rootOwnedDirect: rootOwned));
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

			if (instance.IsRequestingTypeFactory)
			{
				AddRequestingTypeRelationshipEntries(serviceKey.Service, resolver, entries, seen);
			}
			else
			{
				AddStandardRelationshipEntries(serviceKey.Service, resolver, rootOwned, funcWithheld, entries, seen);
			}
		}
	}

	/// <summary>
	///     Adds the synthetic <c>Func&lt;T&gt;</c> and <c>Lazy&lt;T&gt;</c> entries over a requesting-type
	///     factory's service type: each closes over the null top-level requesting type (the factory is
	///     Scope-hosted and reached over <c>__s</c>). There is no <c>Owned&lt;T&gt;</c> form: the requesting
	///     type has no owner scope and the factory decides its own disposal.
	/// </summary>
	private static void AddRequestingTypeRelationshipEntries(string service, string resolver, List<DispatchEntry> entries, HashSet<string> seen)
	{
		string requestingCall = $"{resolver}(__s, null)";
		string requestingFunc = $"global::System.Func<{service}>";
		if (seen.Add(requestingFunc))
		{
			entries.Add(new DispatchEntry(requestingFunc, $"new global::System.Func<{service}>(() => {requestingCall})"));
		}

		string requestingLazy = $"global::System.Lazy<{service}>";
		if (seen.Add(requestingLazy))
		{
			entries.Add(new DispatchEntry(requestingLazy, $"new global::System.Lazy<{service}>(() => {requestingCall})"));
		}
	}

	/// <summary>
	///     Adds the synthetic <c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>, <c>Owned&lt;T&gt;</c> and
	///     <c>Func&lt;Owned&lt;T&gt;&gt;</c> entries over an ordinary service type, skipping any key an explicit
	///     registration already claimed (tracked in <paramref name="seen" />).
	/// </summary>
	private static void AddStandardRelationshipEntries(string service, string resolver, bool rootOwned, bool funcWithheld, List<DispatchEntry> entries, HashSet<string> seen)
	{
		// The plain Func<T> factory accumulates on its owner; under strict safety it is root-withheld
		// (resolving it by type off the Root throws guidance, steering to Func<Owned<T>>), but it stays
		// resolvable from a child scope, where the disposables it builds are bounded by the scope.
		// The deferred call binds the target's static resolver over the forwarder's scope __s (Root-hosted for
		// a singleton, Scope-hosted otherwise).
		string call = ResolveCall(resolver, rootOwned);
		string func = $"global::System.Func<{service}>";
		if (seen.Add(func))
		{
			entries.Add(funcWithheld
				? new DispatchEntry(func, $"new global::System.Func<{service}>(() => {call})", FuncWithheldMessage(service))
				: new DispatchEntry(func, $"new global::System.Func<{service}>(() => {call})"));
		}

		// Lazy<T> is memoized: it builds at most once and never accumulates, so it stays resolvable even
		// for a withheld disposable service.
		string lazy = $"global::System.Lazy<{service}>";
		if (seen.Add(lazy))
		{
			entries.Add(new DispatchEntry(lazy, $"new global::System.Lazy<{service}>(() => {call})"));
		}

		// Owned<T> hands the caller a disposal handle over a single resolution; Func<Owned<T>> is the
		// leak-free factory that produces one per call. Both build into a throwaway child scope, and both
		// stay resolvable under strict safety. They are the sanctioned way to reach a withheld service.
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

	/// <summary>
	///     Emits the data-driven by-type dispatch: a nested <c>__Bucket</c> slot type, the static
	///     <c>__buckets</c> / <c>__bucketSize</c> table (built once in the static constructor and shared by every
	///     scope and the <c>Root</c>), the <c>__BuildBuckets</c> distributor, and a <c>__R</c> forwarder per
	///     compound entry. It lives on the base <c>Scope</c>; <c>TryResolve</c> probes the table by Type identity
	///     hash. The table grows in data, never in a single method's IL, so it cannot hit the JIT optimization
	///     guards no matter how many registrations there are. The table fields and their <c>static Scope()</c>
	///     initializer are routed into <paramref name="fields" /> (the fields region); the <c>__Bucket</c> slot
	///     type, <c>__BuildBuckets</c> and the <c>__R</c> forwarders into <paramref name="helpers" />.
	/// </summary>
	private static void EmitBucketDispatch(StringBuilder fields, StringBuilder helpers, int depth, List<DispatchEntry> entries)
	{
		int bucketCount = BucketCount(entries.Count);

		// Assign a forwarder to each UNIQUE compound value expression. Identical values share one __R method rather
		// than emitting a body each. Notably the six collection shapes of one element type (IEnumerable<T>,
		// IReadOnlyList<T>, IReadOnlyCollection<T>, IList<T>, ICollection<T>, T[]) all materialize the very same
		// array, so they collapse from six method bodies to one. Bare services bind a direct delegate and need none.
		Dictionary<string, int> forwarderOf = new(StringComparer.Ordinal);
		List<string> forwarders = new();
		foreach (string value in entries.Where(entry => entry.DirectResolver is null && !forwarderOf.ContainsKey(entry.Value)).Select(entry => entry.Value))
		{
			forwarderOf[value] = forwarders.Count;
			forwarders.Add(value);
		}

		Separate(fields);
		Indent(fields, depth).Append("private const int __bucketCount = ").Append(bucketCount).AppendLine(";");
		Indent(fields, depth).AppendLine("private static readonly __Bucket[] __buckets;");
		Indent(fields, depth).AppendLine("private static readonly int __bucketSize;");
		fields.AppendLine();

		// The static ctor lists every entry with its resolver delegate, then distributes them into fixed-width
		// buckets by the runtime Type identity hash (unknown at generation time). A bare service binds a direct
		// delegate to its existing virtual resolver; a compound value routes through a __R forwarder that preserves
		// the expression verbatim, so all owner-context construction (throwaway scopes, __root) is unchanged.
		Indent(fields, depth).AppendLine("static Scope()");
		Indent(fields, depth).AppendLine("{");
		Indent(fields, depth + 1).AppendLine("__Bucket[] __entries =");
		Indent(fields, depth + 1).AppendLine("{");
		for (int i = 0; i < entries.Count; i++)
		{
			string resolve;
			if (entries[i].DirectResolver is { } direct)
			{
				// A bare service binds directly to its static resolver: a singleton on the Root over the shared
				// root, a scoped/transient on the Scope over the resolving scope.
				resolve = entries[i].RootOwnedDirect
					? $"static __s => Root.{direct}(__s.__root)"
					: $"static __s => {direct}(__s)";
			}
			else
			{
				// A compound value routes through a static __R forwarder over the resolving scope.
				resolve = $"static __s => __R{forwarderOf[entries[i].Value]}(__s)";
			}

			Indent(fields, depth + 2).Append("new __Bucket(typeof(").Append(entries[i].Type).Append("), ")
				.Append(resolve).Append(", ").Append(entries[i].RootWithheld ? "true" : "false").AppendLine("),");
		}

		Indent(fields, depth + 1).AppendLine("};");
		Indent(fields, depth + 1).AppendLine("__buckets = __BuildBuckets(__entries);");
		Indent(fields, depth + 1).AppendLine("__bucketSize = __buckets.Length / __bucketCount;");
		Indent(fields, depth).AppendLine("}");

		// One table slot: the key type, its resolver delegate, and whether the slot is withheld from by-type
		// resolution on the Root. A default slot (Key == null) is an empty probe cell and never matches a request.
		Separate(helpers);
		AppendXmlSummary(helpers, depth, "One slot of the by-type dispatch table.");
		Indent(helpers, depth).AppendLine("private readonly struct __Bucket");
		Indent(helpers, depth).AppendLine("{");
		Indent(helpers, depth + 1).AppendLine("public readonly global::System.Type? Key;");
		Indent(helpers, depth + 1).AppendLine("public readonly global::System.Func<Scope, object> Resolve;");
		Indent(helpers, depth + 1).AppendLine("public readonly bool RootWithheld;");
		Indent(helpers, depth + 1).AppendLine("public __Bucket(global::System.Type? key, global::System.Func<Scope, object> resolve, bool rootWithheld)");
		Indent(helpers, depth + 1).AppendLine("{");
		Indent(helpers, depth + 2).AppendLine("Key = key;");
		Indent(helpers, depth + 2).AppendLine("Resolve = resolve;");
		Indent(helpers, depth + 2).AppendLine("RootWithheld = rootWithheld;");
		Indent(helpers, depth + 1).AppendLine("}");
		Indent(helpers, depth).AppendLine("}");
		helpers.AppendLine();

		AppendXmlSummary(helpers, depth, "Builds the by-type dispatch table.");
		Indent(helpers, depth).AppendLine("private static __Bucket[] __BuildBuckets(__Bucket[] __entries)");
		Indent(helpers, depth).AppendLine("{");
		EmitBucketDistribution(helpers, depth + 1, "__Bucket", "__bucketCount");
		Indent(helpers, depth).AppendLine("}");

		// One forwarder per unique compound value, preserving the value expression verbatim over the resolving
		// scope __s. Each is tiny and individually optimizable; deduplication (above) keeps the six collection
		// shapes of an element type to one.
		for (int k = 0; k < forwarders.Count; k++)
		{
			helpers.AppendLine();
			Indent(helpers, depth).Append("private static object __R").Append(k).Append("(Scope __s) => ").Append(forwarders[k]).AppendLine(";");
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
	///     keeps the load factor at or below one half, so collisions, and thus the fixed probe window, stay small.
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
		Indent(builder, depth + 1).AppendLine("global::Awaiten.IExternalResolver? resolver = __externalResolver ?? __root.__externalResolver;");
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
	///     instance's constructor parameters, in first-seen order, advertised by the Root as
	///     <c>ExternalDependencies</c> and used to decide whether the external-resolution surface is emitted.
	/// </summary>
	private static (string Type, string? Key)[] ExternalDependencies(InstanceModel[] instances)
	{
		List<(string Type, string? Key)> external = new();
		HashSet<(string, string?)> seen = new();
		foreach (InstanceModel instance in instances)
		{
			foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
			{
				if (parameter.Kind == DependencyKind.External && seen.Add((parameter.ServiceType, parameter.Key)))
				{
					external.Add((parameter.ServiceType, parameter.Key));
				}
			}
		}

		return external.ToArray();
	}

	private static bool HasExternalDependencies(InstanceModel[] instances) => ExternalDependencies(instances).Length > 0;

	/// <summary>
	///     A single dispatch case: the service <see cref="Type" /> requested and the <see cref="Value" /> assigned to
	///     <c>instance</c> (the resolver call). When <see cref="Guidance" /> is set the entry is root-withheld under
	///     strict lifetime safety: it resolves normally from a child scope, but on the Root <c>TryResolve</c> returns
	///     <see langword="false" /> and <c>Resolve</c> throws the <see cref="Guidance" /> message, so the
	///     root-accumulation leak stays impossible while the safe scope-bound resolution is allowed.
	/// </summary>
	private readonly struct DispatchEntry(string type, string value, string? guidance = null, string? directResolver = null, bool rootOwnedDirect = false)
	{
		public string Type { get; } = type;

		public string Value { get; } = value;

		public string? Guidance { get; } = guidance;

		/// <summary>
		///     A bare-service entry keeps its resolver method name so the bucket table can bind a direct delegate
		///     to the static resolver (<c>__s =&gt; ResolveX(__s)</c>, or <c>__s =&gt; Root.ResolveX(__s.__root)</c>
		///     for a singleton) instead of routing through a per-entry forwarder. Null for compound values
		///     (<c>Func</c>/<c>Lazy</c>/<c>Owned</c>/collection literals), which need their expression preserved
		///     verbatim in a <c>__R</c> forwarder.
		/// </summary>
		public string? DirectResolver { get; } = directResolver;

		/// <summary>
		///     Whether a bare-service <c>DirectResolver</c> is root-owned (a singleton or pre-built Instance): its
		///     static resolver lives on the Root and is bound as <c>Root.ResolveX(__s.__root)</c>; otherwise it
		///     lives on the Scope.
		/// </summary>
		public bool RootOwnedDirect { get; } = rootOwnedDirect;

		public bool RootWithheld => Guidance is not null;
	}
}
