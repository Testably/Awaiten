using System.Text;
using Awaiten.SourceGenerators.Entities;

namespace Awaiten.SourceGenerators;

internal static partial class Sources
{
	/// <summary>
	///     The async collections resolvable by type through <c>ResolveAsync</c>: each unkeyed, non-synthesis-
	///     suppressed collection whose async shape is not itself registered (a registered
	///     <c>IAsyncEnumerable&lt;T&gt;</c> owns its slot on both surfaces) and that is not synchronously
	///     materializable (it holds an async-tainted member) - outside
	///     pragmatic <c>SyncResolveAfterInit</c> mode, where every collection is synchronously materializable and so
	///     served by the synchronous dispatch. Each is paired with the async resolver method that materializes it,
	///     named by the collection's position in <see cref="Names.Collections" /> so the method emission and the
	///     async dispatch arm derive the same name.
	/// </summary>
	private static IEnumerable<(ServiceMembers Collection, string Method)> AsyncByTypeCollections(
		Names names, Dictionary<ServiceKey, int> serviceToIndex, bool syncResolveAfterInit)
	{
		if (syncResolveAfterInit)
		{
			yield break;
		}

		ServiceMembers[] collections = names.Collections;
		for (int i = 0; i < collections.Length; i++)
		{
			ServiceMembers collection = collections[i];
			if (collection.Key is null
			    && !SynthesisSuppressed(serviceToIndex, collection.Service)
			    && !AsyncShapeRegistered(serviceToIndex, collection.Service)
			    && !names.IsSyncCollection(new ServiceKey(collection.Service, collection.Key)))
			{
				yield return (collection, "__ResolveAsyncCollection" + i);
			}
		}
	}

	/// <summary>
	///     Emits the async by-type resolver for one async collection: it materializes the
	///     <c>IAsyncEnumerable&lt;T&gt;</c> exactly as an injected async collection is built - awaiting each
	///     async-tainted member off the scope the resolver runs on, resolving each synchronous member directly - so
	///     <c>ResolveAsync(typeof(IAsyncEnumerable&lt;T&gt;))</c> hands back the initialized stream.
	/// </summary>
	private static void EmitAsyncCollectionResolver(StringBuilder builder, int depth, ServiceMembers collection, string method, Names names, InstanceModel[] instances)
	{
		ServiceKey collectionKey = new(collection.Service, collection.Key);
		AppendXmlSummary(builder, depth,
			$"Asynchronously materializes the {XmlTypeRef(collection.Service)} collection, awaiting each member.");
		Indent(builder, depth).Append("internal async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IAsyncEnumerable<")
			.Append(collection.Service).Append(">> ").Append(method).AppendLine("(global::System.Threading.CancellationToken cancellationToken)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).Append("return ").Append(AsyncCollectionExpression(collectionKey, names, instances, asynchronous: true)).AppendLine(";");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits a resolver on the base <c>Scope</c>. A parameterized service (with <c>[Arg]</c> parameters)
	///     takes those runtime arguments and is built fresh per call (regardless of its declared lifetime),
	///     so it lives here as a <c>protected</c> method the <c>Root</c> can also call. Otherwise
	///     singleton-owned services (singletons and pre-built Instances) become a <c>protected virtual</c>
	///     delegator to <c>__root</c> (overridden by the <c>Root</c>); scoped services cache on the scope;
	///     transients construct fresh.
	/// </summary>
	private static void EmitScopeResolver(StringBuilder builder, int depth, int index, EmitContext context)
	{
		InstanceModel instance = context.Instances[index];
		Names names = context.Names;
		bool asyncDisposal = context.AsyncDisposal;
		string type = instance.ConstructedType;
		string resolver = names.Resolver(index);

		// A deferred [Inject(Deferred = true)] member is wired after the instance is constructed (a scoped instance
		// after it is cached, so a mutual cycle terminates; a transient right after construction). Null when the
		// instance has no deferred member, so a plain resolver's emitted code is unchanged.
		Action<int>? DeferredFor(string variable) => HasDeferredMembers(instance)
			? d => EmitDeferredAssignments(builder, d, instance, variable, context, asynchronous: false)
			: null;

		if (instance.IsParameterized)
		{
			string[] argTypes = instance.ArgTypes();
			string signature = string.Join(", ", argTypes.Select((t, i) => $"{t} a{i}"));
			string parameterizedConstruction = EmitConstruction(instance, context.Instances, names, context.ServiceToIndex);
			string parameterizedSummary = $"Resolves {XmlTypeRef(type)} from its <c>[Arg]</c> arguments (a new instance per call).";
			// Reachable from the Root (a singleton's Func<TArg…, T> binds it there) and from a throwaway
			// Owned<T> scope built off any owner (__s.ResolveX(args)), so it is internal rather than protected -
			// protected would not be callable through a base-typed scope reference from the derived Root (CS1540).
			EmitFreshResolver(builder, depth, new FreshResolver("internal", type, resolver, signature, parameterizedConstruction, DisposalOf(instance), parameterizedSummary), asyncDisposal, DeferredFor("created"));
			return;
		}

		if (instance.Lifetime == Lifetime.Singleton || instance.Production == ProductionKind.Instance)
		{
			AppendXmlSummary(builder, depth, instance.Production == ProductionKind.Instance
				? $"Resolves the pre-built {XmlTypeRef(type)} from the root."
				: $"Resolves the singleton {XmlTypeRef(type)} from the root.");
			Indent(builder, depth).Append("protected virtual ").Append(type).Append(' ').Append(resolver).AppendLine("()");
			Indent(builder, depth).AppendLine("{");
			EmitDisposedGuard(builder, depth + 1);
			Indent(builder, depth + 1).Append("return __root.").Append(resolver).AppendLine("();");
			Indent(builder, depth).AppendLine("}");
			return;
		}

		string construction = EmitConstruction(instance, context.Instances, names, context.ServiceToIndex);

		// Transient and scoped resolvers are internal (not private/protected) so a throwaway Owned<T> scope can
		// call them directly through a base-typed scope reference (__s.ResolveX()) - this bypasses the strict
		// by-type withholding, which only blocks the public dispatch/typed surface, not these resolver methods.
		// Internal also covers the case the Root (a subclass) reaches a captured scoped/transient's resolver.
		if (instance.Lifetime == Lifetime.Transient)
		{
			string transientSummary = $"Resolves the transient {XmlTypeRef(type)} (a new instance per call).";
			EmitFreshResolver(builder, depth, new FreshResolver("internal", type, resolver, string.Empty, construction, DisposalOf(instance), transientSummary), asyncDisposal, DeferredFor("created"));
			return;
		}

		string scopedSummary = $"Resolves the scoped {XmlTypeRef(type)} (one instance per scope).";
		string scopedWiredFlag = HasDeferredMembers(instance) ? names.WiredField(index) : string.Empty;
		EmitCachingResolver(builder, depth, new CachingResolver("internal", type, resolver, names.Field(index), construction, DisposalOf(instance), scopedSummary, scopedWiredFlag), asyncDisposal, DeferredFor(names.Field(index)));
	}

	/// <summary>
	///     Emits a resolver that constructs a fresh instance on every call (a transient, or a parameterized
	///     service that also takes the runtime arguments named in its signature). A disposable instance is
	///     registered for teardown on the owner under the lock, re-checking <c>__disposed</c> so one built
	///     during a concurrent dispose is disposed here rather than leaked.
	/// </summary>
	private static void EmitFreshResolver(StringBuilder builder, int depth, in FreshResolver resolver, bool asyncDisposal, Action<int>? emitDeferred = null)
	{
		string type = resolver.Type;
		string construction = resolver.Construction;
		AppendXmlSummary(builder, depth, resolver.Summary);
		Indent(builder, depth).Append(resolver.Modifiers).Append(' ').Append(type).Append(' ').Append(resolver.Method)
			.Append('(').Append(resolver.Signature).AppendLine(")");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1);

		// A transient is not cached, so its deferred members never participate in a terminating cycle (AWT139
		// rejects a transient deferred cycle); they are still wired after construction here. Deferred members also
		// force the `created` variable form so there is an instance to assign through.
		if (resolver.Disposal != DisposalTracking.None || emitDeferred is not null)
		{
			Indent(builder, depth + 1).Append(type).Append(" created = ").Append(construction).AppendLine(";");
			if (resolver.Disposal != DisposalTracking.None)
			{
				EmitFreshDisposalTracking(builder, depth + 1, resolver.Disposal == DisposalTracking.Runtime, asyncDisposal, asyncContext: false);
				builder.AppendLine();
			}

			emitDeferred?.Invoke(depth + 1);
			Indent(builder, depth + 1).AppendLine("return created;");
		}
		else
		{
			Indent(builder, depth + 1).Append("return ").Append(construction).AppendLine(";");
		}

		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits a singleton resolver on the <c>Root</c> as a <c>protected override</c>: a pre-built Instance
	///     returns the static container member by simple name; a constructed/factory singleton is cached once
	///     under the lock.
	/// </summary>
	private static void EmitRootResolver(StringBuilder builder, int depth, int index, EmitContext context)
	{
		InstanceModel instance = context.Instances[index];
		Names names = context.Names;
		string type = instance.ConstructedType;
		string resolver = names.Resolver(index);

		if (instance.Production == ProductionKind.Instance)
		{
			AppendXmlSummary(builder, depth, $"Returns the pre-built {XmlTypeRef(type)}.");
			// The container is a static class, so its pre-built instance member is in scope by simple name.
			Indent(builder, depth).Append("protected override ").Append(type).Append(' ').Append(resolver).AppendLine("()");
			Indent(builder, depth).AppendLine("{");
			EmitDisposedGuard(builder, depth + 1);
			Indent(builder, depth + 1).Append("return ").Append(instance.ProductionMember).AppendLine(";");
			Indent(builder, depth).AppendLine("}");
			return;
		}

		string construction = EmitConstruction(instance, context.Instances, names, context.ServiceToIndex);
		string singletonSummary = $"Resolves the singleton {XmlTypeRef(type)} (one instance per container).";
		// A deferred [Inject(Deferred = true)] member is wired after the singleton is stored in its field, so a
		// mutual cycle terminates (the re-entrant resolve returns this cached instance). Null when there is none.
		Action<int>? emitDeferred = HasDeferredMembers(instance)
			? d => EmitDeferredAssignments(builder, d, instance, names.Field(index), context, asynchronous: false)
			: null;
		string singletonWiredFlag = HasDeferredMembers(instance) ? names.WiredField(index) : string.Empty;
		EmitCachingResolver(builder, depth, new CachingResolver("protected override", type, resolver, names.Field(index), construction, DisposalOf(instance), singletonSummary, singletonWiredFlag), context.AsyncDisposal, emitDeferred);
	}

	/// <summary>
	///     The by-type async dispatch arms: one per unkeyed service key of each async-tainted, non-parameterized
	///     service (routed to its memoizing async resolver), plus one <c>IAsyncEnumerable&lt;T&gt;</c> arm per async
	///     collection (routed to its generated async collection resolver). Each carries the guidance a root-withheld
	///     arm throws off the Root (null when not withheld). They are collected up front so a large set can be split
	///     across chunk methods, staying under RyuJIT's optimization guards - the same cliff the synchronous dispatch
	///     hit before it was chunked.
	/// </summary>
	private static List<(string Service, string AsyncResolver, string? RootWithheldMessage)> BuildAsyncArms(
		InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool strict, bool syncResolveAfterInit)
	{
		List<(string Service, string AsyncResolver, string? RootWithheldMessage)> arms = new();
		for (int i = 0; i < instances.Length; i++)
		{
			// A parameterized service is built fresh from its runtime arguments, so it is reached only through
			// its Func<TArg…, T> / Func<TArg…, Task<T>> factory; by-type ResolveAsync cannot supply those [Arg]s,
			// so it gets no entry here (it does have an async resolver - the async factory relationship binds it).
			if (!instances[i].IsAsyncTainted || instances[i].IsParameterized)
			{
				continue;
			}

			string asyncResolver = names.AsyncResolver(i);
			// A disposable async transient is withheld from by-type resolution on the Root: each Root resolution
			// would track a fresh disposable on the root for the container's lifetime (an unbounded leak), so the
			// Root throws guidance toward a child scope while a child scope still resolves it (its disposal bounds
			// the instance). Injection into a singleton stays allowed - that is bounded to one instance.
			bool rootWithheld = IsWithheld(instances[i], strict);
			// Keyed registrations are reached only by [FromKey] injection, never by-type resolution.
			foreach (string service in instances[i].Services.AsArray().Where(serviceKey => serviceKey.Key is null).Select(serviceKey => serviceKey.Service))
			{
				arms.Add((service, asyncResolver, rootWithheld ? AsyncRootWithheldMessage(service) : null));
			}
		}

		// The IAsyncEnumerable<T> arm of each async collection, materialized through its generated async collection
		// resolver. A collection with a build-on-demand disposable member is root-withheld, mirroring the sync side.
		Dictionary<string, int> implToIndex = new(StringComparer.Ordinal);
		for (int i = 0; i < instances.Length; i++)
		{
			implToIndex[instances[i].ImplementationType] = i;
		}

		Dictionary<ServiceKey, List<int>> collectionMembers = AwaitenGenerator.CollectionMemberIndices(names.Collections, implToIndex);
		foreach ((ServiceMembers collection, string method) in AsyncByTypeCollections(names, serviceToIndex, syncResolveAfterInit))
		{
			ServiceKey collectionKey = new(collection.Service, collection.Key);
			string shape = AwaitenGenerator.AsyncEnumerableShapeType(collection.Service);
			bool rootWithheld = collectionMembers.TryGetValue(collectionKey, out List<int>? members)
			                    && members.Any(member => IsFuncWithheld(instances, member, serviceToIndex, collectionMembers, strict));
			arms.Add((shape, method, rootWithheld ? CollectionAsyncRootWithheldMessage(shape) : null));
		}

		return arms;
	}

	/// <summary>
	///     Emits the asynchronous <c>ResolveAsync(Type)</c> on the base <c>Scope</c> (inherited by the
	///     <c>Root</c>): async-tainted services are routed to their memoizing async resolver and converted to
	///     <c>Task&lt;object&gt;</c>, while everything that needs no asynchronous initialization resolves
	///     synchronously and completes immediately (deferring to <c>Resolve</c> for the same registration /
	///     withholding errors as the synchronous path).
	/// </summary>
	private static void EmitAsyncResolutionApi(StringBuilder builder, int depth, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool strict, bool syncResolveAfterInit)
	{
		const string task = "global::System.Threading.Tasks.Task";

		List<(string Service, string AsyncResolver, string? RootWithheldMessage)> arms = BuildAsyncArms(instances, names, serviceToIndex, strict, syncResolveAfterInit);

		AppendXmlSummary(builder, depth,
			"Asynchronously resolves the service registered for <paramref name=\"serviceType\" />, awaiting any async initialization.");
		Indent(builder, depth).Append("public ").Append(task)
			.AppendLine("<object> ResolveAsync(global::System.Type serviceType, global::System.Threading.CancellationToken cancellationToken = default)");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1);

		if (arms.Count == 0)
		{
			// No async-tainted service: everything resolves synchronously and completes immediately. Resolve
			// throws the same registration / strict-withholding guidance as the synchronous path.
			Indent(builder, depth + 1).Append("return ").Append(task).AppendLine(".FromResult(Resolve(serviceType));");
			Indent(builder, depth).AppendLine("}");
			return;
		}

		// Bucket probe, mirroring the synchronous dispatch: hash the type into its window and invoke the matched
		// async resolver delegate. A type with no async arm resolves synchronously and completes immediately.
		Indent(builder, depth + 1).AppendLine("int __i = (int)((uint)serviceType.TypeHandle.GetHashCode() % (uint)__asyncBucketCount) * __asyncBucketSize;");
		Indent(builder, depth + 1).AppendLine("int __end = __i + __asyncBucketSize;");
		Indent(builder, depth + 1).AppendLine("for (; __i < __end; __i++)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("ref readonly __AsyncBucket __b = ref __asyncBuckets[__i];");
		Indent(builder, depth + 2).AppendLine("if ((object?)__b.Key == (object?)serviceType)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("return __b.Resolve(this, cancellationToken);");
		Indent(builder, depth + 2).AppendLine("}");
		builder.AppendLine();
		// The distributor fills each window front-first, so an empty slot ends the window: the (common) miss of
		// a synchronously-resolvable type stops at the first empty slot instead of scanning the full width.
		Indent(builder, depth + 2).AppendLine("if (__b.Key is null)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("break;");
		Indent(builder, depth + 2).AppendLine("}");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 1).Append("return ").Append(task).AppendLine(".FromResult(Resolve(serviceType));");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();

		EmitAsyncBucketDispatch(builder, depth, arms);
		EmitAsObjectHelper(builder, depth);
	}

	/// <summary>
	///     Emits the async dispatch table: an <c>__AsyncBucket</c> slot type and the <c>__asyncBuckets</c> /
	///     <c>__asyncBucketSize</c> table, built once through field initializers (which coexist with the
	///     synchronous static constructor). Each slot's delegate awaits the async resolver and converts the result
	///     to <c>Task&lt;object&gt;</c>; a root-withheld arm bakes its guidance throw into the delegate (the Root
	///     throws, a child scope resolves). No forwarder methods are needed - the delegates are inline lambdas.
	/// </summary>
	private static void EmitAsyncBucketDispatch(StringBuilder builder, int depth, List<(string Service, string AsyncResolver, string? RootWithheldMessage)> arms)
	{
		const string func = "global::System.Func<Scope, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<object>>";
		int bucketCount = BucketCount(arms.Count);

		AppendXmlSummary(builder, depth, "One slot of the async by-type dispatch table.");
		Indent(builder, depth).AppendLine("private readonly struct __AsyncBucket");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("public readonly global::System.Type? Key;");
		Indent(builder, depth + 1).Append("public readonly ").Append(func).AppendLine(" Resolve;");
		Indent(builder, depth + 1).Append("public __AsyncBucket(global::System.Type? key, ").Append(func).AppendLine(" resolve)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("Key = key;");
		Indent(builder, depth + 2).AppendLine("Resolve = resolve;");
		Indent(builder, depth + 1).AppendLine("}");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();

		Indent(builder, depth).Append("private const int __asyncBucketCount = ").Append(bucketCount).AppendLine(";");
		Indent(builder, depth).AppendLine("private static readonly __AsyncBucket[] __asyncBuckets = __BuildAsyncBuckets();");
		Indent(builder, depth).AppendLine("private static readonly int __asyncBucketSize = __asyncBuckets.Length / __asyncBucketCount;");
		builder.AppendLine();

		AppendXmlSummary(builder, depth, "Builds the async by-type dispatch table.");
		Indent(builder, depth).AppendLine("private static __AsyncBucket[] __BuildAsyncBuckets()");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("__AsyncBucket[] __entries =");
		Indent(builder, depth + 1).AppendLine("{");
		foreach ((string service, string asyncResolver, string? rootWithheldMessage) in arms)
		{
			string resolve = rootWithheldMessage is not null
				? $"static (__s, __ct) => __s is Root ? throw new global::System.InvalidOperationException({rootWithheldMessage}) : __AsObject(__s.{asyncResolver}(__ct))"
				: $"static (__s, __ct) => __AsObject(__s.{asyncResolver}(__ct))";
			Indent(builder, depth + 2).Append("new __AsyncBucket(typeof(").Append(service).Append("), ").Append(resolve).AppendLine("),");
		}

		Indent(builder, depth + 1).AppendLine("};");
		builder.AppendLine();
		EmitBucketDistribution(builder, depth + 1, "__AsyncBucket", "__asyncBucketCount");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits the <c>__AsObject&lt;T&gt;</c> helper that converts a <c>Task&lt;T&gt;</c> async resolver result to
	///     the <c>Task&lt;object&gt;</c> the by-type <c>ResolveAsync</c> returns. Called only when at least one
	///     async-tainted service exists (otherwise nothing references it).
	/// </summary>
	private static void EmitAsObjectHelper(StringBuilder builder, int depth)
	{
		const string task = "global::System.Threading.Tasks.Task";
		builder.AppendLine();
		Indent(builder, depth).Append("private static async ").Append(task)
			.AppendLine("<object> __AsObject<T>(global::System.Threading.Tasks.Task<T> __task) => (object)(await __task.ConfigureAwait(false))!;");
	}

	/// <summary>
	///     Emits the async resolver for an async-tainted instance on the base <c>Scope</c>: a singleton becomes
	///     a <c>protected virtual</c> delegator to <c>__root</c> (the <c>Root</c> overrides it with the real
	///     caching creator); a scoped service memoizes its construction-and-initialization <c>Task</c> on the
	///     scope; a transient constructs, initializes and returns each call.
	/// </summary>
	private static void EmitAsyncScopeResolver(StringBuilder builder, int depth, int index, EmitContext context)
	{
		InstanceModel instance = context.Instances[index];
		Names names = context.Names;
		const string task = "global::System.Threading.Tasks.Task";
		const string ct = "global::System.Threading.CancellationToken cancellationToken";

		// A deferred [Inject(Deferred = true)] member is wired after the instance is constructed, awaiting an
		// async-tainted member exactly like an async-tainted constructor argument. Null when there is none.
		Action<int>? emitDeferred = HasDeferredMembers(instance)
			? d => EmitDeferredAssignments(builder, d, instance, "created", context, asynchronous: true)
			: null;

		// A parameterized async service is built fresh per call from its runtime arguments AND awaits
		// initialization, so it is reached only through Func<TArg…, Task<T>>. Its async resolver takes the
		// arguments alongside the token; like the synchronous parameterized resolver it lives on the base Scope
		// (internal) and the Root inherits it, never caching (a parameterized service is always transient).
		if (instance.IsParameterized)
		{
			string[] argTypes = instance.ArgTypes();
			string argSignature = string.Join("", argTypes.Select((t, i) => $"{t} a{i}, "));
			string parameterizedConstruction = EmitConstruction(instance, context.Instances, names, context.ServiceToIndex, asynchronous: true);
			EmitAsyncFreshResolver(builder, depth, index, context, parameterizedConstruction, argSignature, emitDeferred);
			return;
		}

		if (instance.Lifetime == Lifetime.Singleton)
		{
			AppendXmlSummary(builder, depth,
				$"Asynchronously resolves the singleton {XmlTypeRef(instance.ConstructedType)} from the root.");
			Indent(builder, depth).Append("protected virtual ").Append(task).Append('<').Append(instance.ConstructedType)
				.Append("> ").Append(names.AsyncResolver(index)).Append('(').Append(ct).Append(") => __root.")
				.Append(names.AsyncResolver(index)).AppendLine("(cancellationToken);");
			return;
		}

		string construction = EmitConstruction(instance, context.Instances, names, context.ServiceToIndex, asynchronous: true);
		if (instance.Lifetime == Lifetime.Transient)
		{
			EmitAsyncFreshResolver(builder, depth, index, context, construction, emitDeferred: emitDeferred);
			return;
		}

		// Scoped: a memoized Task on the scope guards construction-and-initialization.
		EmitAsyncCachingResolver(builder, depth, index, context, construction, "internal", emitDeferred);
	}

	/// <summary>
	///     Emits the synchronous resolver for an async-tainted service in pragmatic mode
	///     (<c>SyncResolveAfterInit</c>): it blocks on the memoizing async resolver - the single
	///     construction-and-initialization path - rather than building a second, uninitialized instance. Once
	///     the service has been warmed (through <c>InitializeAsync</c> / <c>CreateScopeAsync</c> / an earlier
	///     <c>ResolveAsync</c>) the async resolver returns a completed task, so this returns the cached
	///     instance without blocking; resolving before warm-up blocks the caller until initialization
	///     completes. (Strict mode emits no synchronous resolver for an async-tainted service at all.)
	/// </summary>
	private static void EmitDelegatingSyncResolver(StringBuilder builder, int depth, int index, InstanceModel instance, Names names)
	{
		// A parameterized service forwards its runtime arguments to the async resolver: the synchronous resolver
		// takes the same arguments, blocks on the (per-call) async resolver, and so still drives initialization.
		string[] argTypes = instance.ArgTypes();
		string signature = string.Join(", ", argTypes.Select((t, i) => $"{t} a{i}"));
		string forward = string.Join("", argTypes.Select((_, i) => "a" + i + ", "));

		AppendXmlSummary(builder, depth,
			$"Resolves {XmlTypeRef(instance.ConstructedType)} by blocking on its async resolver (<c>SyncResolveAfterInit</c>).");
		Indent(builder, depth).Append("internal ").Append(instance.ConstructedType).Append(' ')
			.Append(names.Resolver(index)).Append('(').Append(signature).AppendLine(")");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1);
		Indent(builder, depth + 1).Append("return ").Append(names.AsyncResolver(index))
			.Append('(').Append(forward).AppendLine("default).GetAwaiter().GetResult();");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits the singleton async resolver on the <c>Root</c> as a <c>protected override</c>, plus its
	///     creator: the memoized <c>Task</c> guarantees the construction and <c>InitializeAsync</c> run at most
	///     once, thread-safely under <c>__gate</c>.
	/// </summary>
	private static void EmitAsyncRootResolver(StringBuilder builder, int depth, int index, EmitContext context)
	{
		InstanceModel instance = context.Instances[index];
		string construction = EmitConstruction(instance, context.Instances, context.Names, context.ServiceToIndex, asynchronous: true);
		// A deferred [Inject(Deferred = true)] member is wired in the creator after construction (awaiting an
		// async-tainted member like an async-tainted constructor argument). Null when there is none.
		Action<int>? emitDeferred = HasDeferredMembers(instance)
			? d => EmitDeferredAssignments(builder, d, instance, "created", context, asynchronous: true)
			: null;
		EmitAsyncCachingResolver(builder, depth, index, context, construction, "protected override", emitDeferred);
	}

	/// <summary>
	///     Emits a memoizing async resolver and its creator. The resolver returns the cached <c>Task</c> if it
	///     exists (lock-free), otherwise assigns it under <c>__gate</c> so the creator runs once; the creator
	///     constructs, registers for disposal and awaits <c>InitializeAsync</c>. A task that faults or is
	///     canceled is evicted from the cache so a later call retries rather than replaying the same failure
	///     (and so one caller's cancellation does not permanently poison a shared singleton).
	/// </summary>
	private static void EmitAsyncCachingResolver(StringBuilder builder, int depth, int index, EmitContext context, string construction, string modifiers, Action<int>? emitDeferred = null)
	{
		InstanceModel instance = context.Instances[index];
		Names names = context.Names;
		string type = instance.ConstructedType;
		string asyncResolver = names.AsyncResolver(index);
		string asyncField = names.AsyncField(index);
		string creator = names.AsyncCreator(index);
		const string task = "global::System.Threading.Tasks.Task";
		const string ct = "global::System.Threading.CancellationToken cancellationToken";

		string summary = modifiers == "protected override"
			? $"Asynchronously resolves the singleton {XmlTypeRef(type)} (one instance per container)."
			: $"Asynchronously resolves the scoped {XmlTypeRef(type)} (one instance per scope).";
		AppendXmlSummary(builder, depth, summary);
		Indent(builder, depth).Append(modifiers).Append(' ').Append(task).Append('<').Append(type).Append("> ")
			.Append(asyncResolver).Append('(').Append(ct).AppendLine(")");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1);
		Indent(builder, depth + 1).Append(task).Append('<').Append(type).Append(">? __cached = ").Append(asyncField).AppendLine(";");
		// A faulted or canceled task is not memoized: treat it as absent so this call rebuilds rather than
		// replaying a past failure (or a previous caller's cancellation) forever. A successful or still-running
		// task is returned without taking the lock.
		Indent(builder, depth + 1).AppendLine("if (__cached is not null && !__cached.IsFaulted && !__cached.IsCanceled)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return __cached;");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("lock (__gate)");
		Indent(builder, depth + 1).AppendLine("{");
		EmitDisposedGuard(builder, depth + 2);
		Indent(builder, depth + 2).Append(task).Append('<').Append(type).Append(">? __pending = ").Append(asyncField).AppendLine(";");
		Indent(builder, depth + 2).AppendLine("if (__pending is null || __pending.IsFaulted || __pending.IsCanceled)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).Append("__pending = ").Append(creator).AppendLine("(cancellationToken);");
		Indent(builder, depth + 3).Append(asyncField).AppendLine(" = __pending;");
		Indent(builder, depth + 2).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 2).AppendLine("return __pending;");
		Indent(builder, depth + 1).AppendLine("}");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();

		Indent(builder, depth).Append("private async ").Append(task).Append('<').Append(type).Append("> ")
			.Append(creator).Append('(').Append(ct).AppendLine(")");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).Append(type).Append(" created = ").Append(construction).AppendLine(";");
		EmitAsyncDisposableRegistration(builder, depth + 1, instance, context.AsyncDisposal);
		emitDeferred?.Invoke(depth + 1);
		EmitAsyncInitialization(builder, depth + 1, instance, "created");
		Indent(builder, depth + 1).AppendLine("return created;");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits an async resolver that constructs, initializes and returns a fresh instance on every call (a
	///     transient, or a parameterized service that additionally takes the runtime arguments named in
	///     <paramref name="argSignature" />). A disposable instance is registered for teardown on the owner.
	/// </summary>
	private static void EmitAsyncFreshResolver(StringBuilder builder, int depth, int index, EmitContext context, string construction, string argSignature = "", Action<int>? emitDeferred = null)
	{
		InstanceModel instance = context.Instances[index];
		Names names = context.Names;
		string type = instance.ConstructedType;
		const string task = "global::System.Threading.Tasks.Task";
		const string ct = "global::System.Threading.CancellationToken cancellationToken";

		string summary = argSignature.Length > 0
			? $"Asynchronously resolves {XmlTypeRef(type)} from its <c>[Arg]</c> arguments (a new instance per call)."
			: $"Asynchronously resolves the transient {XmlTypeRef(type)} (a new instance per call).";
		AppendXmlSummary(builder, depth, summary);
		Indent(builder, depth).Append("internal async ").Append(task).Append('<').Append(type).Append("> ")
			.Append(names.AsyncResolver(index)).Append('(').Append(argSignature).Append(ct).AppendLine(")");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1);
		Indent(builder, depth + 1).Append(type).Append(" created = ").Append(construction).AppendLine(";");
		EmitAsyncDisposableRegistration(builder, depth + 1, instance, context.AsyncDisposal);
		emitDeferred?.Invoke(depth + 1);
		EmitAsyncInitialization(builder, depth + 1, instance, "created");
		Indent(builder, depth + 1).AppendLine("return created;");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Awaits the instance's own <c>IAsyncInitializable.InitializeAsync</c> when its implementation is
	///     async-initialized (an instance that is only async-tainted through a dependency has nothing of its
	///     own to initialize - its async dependency was already awaited during construction).
	/// </summary>
	private static void EmitAsyncInitialization(StringBuilder builder, int depth, InstanceModel instance, string variable)
	{
		if (instance.IsAsyncInitializable)
		{
			Indent(builder, depth).Append("await ((global::Awaiten.IAsyncInitializable)").Append(variable)
				.AppendLine(").InitializeAsync(cancellationToken).ConfigureAwait(false);");
		}
	}

	/// <summary>
	///     Emits the base <c>Scope</c>'s internal <c>__WarmAsync</c>: it eagerly warms this scope's
	///     async-initialized scoped services in dependency order. It is internal (not on <c>IAwaitenScope</c>)
	///     because a child scope is warmed only at creation, through <c>CreateScopeAsync</c>, which calls this.
	/// </summary>
	private static void EmitScopeInitializeAsync(StringBuilder builder, int depth, InstanceModel[] instances, Names names)
		=> EmitWarmUp(builder, depth, instances, names, "internal", "__WarmAsync", Lifetime.Scoped);

	/// <summary>
	///     Emits the <c>Root</c>'s <c>InitializeAsync</c> (the <c>IAwaitenRoot</c> member): it eagerly warms
	///     the async-initialized singletons in dependency order (idempotent and thread-safe, each warmed at
	///     most once via its memoized resolver).
	/// </summary>
	private static void EmitRootInitializeAsync(StringBuilder builder, int depth, InstanceModel[] instances, Names names)
		=> EmitWarmUp(builder, depth, instances, names, "public", "InitializeAsync", Lifetime.Singleton);

	/// <summary>
	///     Emits a warm-up method that awaits the async resolver of every async-tainted, non-parameterized
	///     instance of the given lifetime; with none it returns <c>Task.CompletedTask</c>. The disposed-guard
	///     runs synchronously (eager state validation, like <c>ResolveAsync</c>) rather than surfacing only when
	///     the returned task is awaited: with targets the public/internal entry validates and delegates to a
	///     private <c>async</c> core, so a disposed container throws immediately on the call.
	/// </summary>
	private static void EmitWarmUp(StringBuilder builder, int depth, InstanceModel[] instances, Names names, string modifiers, string method, Lifetime lifetime)
	{
		const string task = "global::System.Threading.Tasks.Task";
		const string ctParam = "global::System.Threading.CancellationToken cancellationToken = default";
		int[] targets = WarmUpTargets(instances, lifetime);

		string summary = lifetime == Lifetime.Singleton
			? "Eagerly initializes the async-initialized singletons in dependency order."
			: "Warms this scope's async-initialized scoped services in dependency order.";

		if (targets.Length == 0)
		{
			AppendXmlSummary(builder, depth, summary);
			Indent(builder, depth).Append(modifiers).Append(' ').Append(task).Append(' ').Append(method).Append('(').Append(ctParam).AppendLine(")");
			Indent(builder, depth).AppendLine("{");
			EmitDisposedGuard(builder, depth + 1);
			Indent(builder, depth + 1).Append("return ").Append(task).AppendLine(".CompletedTask;");
			Indent(builder, depth).AppendLine("}");
			return;
		}

		string core = method + "Core";
		AppendXmlSummary(builder, depth, summary);
		Indent(builder, depth).Append(modifiers).Append(' ').Append(task).Append(' ').Append(method).Append('(').Append(ctParam).AppendLine(")");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1);
		Indent(builder, depth + 1).Append("return ").Append(core).AppendLine("(cancellationToken);");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();

		Indent(builder, depth).Append("private async ").Append(task).Append(' ').Append(core)
			.AppendLine("(global::System.Threading.CancellationToken cancellationToken)");
		Indent(builder, depth).AppendLine("{");
		foreach (int i in targets)
		{
			Indent(builder, depth + 1).Append("await ").Append(names.AsyncResolver(i)).AppendLine("(cancellationToken).ConfigureAwait(false);");
		}

		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits <c>CreateScopeAsync</c> on the base <c>Scope</c> (inherited by the <c>Root</c>): it opens a
	///     child scope and warms its async-initialized scoped services before handing it back. If warm-up
	///     throws, the scope is disposed (draining whatever was built before the failure) rather than leaked.
	/// </summary>
	private static void EmitCreateScopeAsync(StringBuilder builder, int depth)
	{
		const string task = "global::System.Threading.Tasks.Task";

		// The disposed-guard runs synchronously (eager state validation, like ResolveAsync) by splitting the
		// public entry from a private async core: the entry validates and returns the core's task, so a disposed
		// container throws immediately on the call rather than only when the returned task is awaited.
		AppendXmlSummary(builder, depth,
			"Opens a child scope, warming its async-initialized scoped services.");
		Indent(builder, depth).Append("public ").Append(task)
			.AppendLine("<global::Awaiten.IAwaitenScope> CreateScopeAsync(global::System.Threading.CancellationToken cancellationToken = default)");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1);
		Indent(builder, depth + 1).AppendLine("return CreateScopeAsyncCore(cancellationToken);");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();

		Indent(builder, depth).Append("private async ").Append(task)
			.AppendLine("<global::Awaiten.IAwaitenScope> CreateScopeAsyncCore(global::System.Threading.CancellationToken cancellationToken)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("Scope __scope = CreateScope();");
		Indent(builder, depth + 1).AppendLine("try");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("await __scope.__WarmAsync(cancellationToken).ConfigureAwait(false);");
		Indent(builder, depth + 1).AppendLine("}");
		Indent(builder, depth + 1).AppendLine("catch");
		Indent(builder, depth + 1).AppendLine("{");
		// Initialization failed (or was canceled) partway through: dispose the scope so the instances it
		// already built and tracked are torn down rather than leaked, then surface the original failure.
		Indent(builder, depth + 2).AppendLine("__scope.Dispose();");
		Indent(builder, depth + 2).AppendLine("throw;");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("return __scope;");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     The indices of the async-tainted, non-parameterized instances of the given lifetime that are eagerly
	///     warmed up (singletons by the Root's <c>InitializeAsync</c>, scoped services per scope).
	/// </summary>
	private static int[] WarmUpTargets(InstanceModel[] instances, Lifetime lifetime)
	{
		List<int> targets = new();
		for (int i = 0; i < instances.Length; i++)
		{
			if (instances[i].IsAsyncTainted && instances[i].Lifetime == lifetime && !instances[i].IsParameterized)
			{
				targets.Add(i);
			}
		}

		return targets.ToArray();
	}

	/// <summary>
	///     Emits a lock-free-read, lock-on-write cached resolver: return the cached field if set, otherwise
	///     construct once under <c>lock (__gate)</c>, registering a disposable instance for teardown.
	/// </summary>
	private static void EmitCachingResolver(StringBuilder builder, int depth, in CachingResolver resolver, bool asyncDisposal, Action<int>? emitDeferred = null)
	{
		AppendXmlSummary(builder, depth, resolver.Summary);
		Indent(builder, depth).Append(resolver.Modifiers).Append(' ').Append(resolver.Type).Append(' ').Append(resolver.Method).AppendLine("()");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1);

		// The lock-free fast path returns the cached field without taking the lock. When the instance has deferred
		// ([Inject(Deferred = true)]) members it also tests the volatile wiring flag: those members are wired only
		// after the field is published (inside the lock, below), so gating on the field alone could hand a concurrent
		// caller a published-but-half-wired instance. The flag is set last, so a caller sees it true only once wiring
		// has completed (and its acquire-read makes the deferred writes visible). The re-entrant same-thread resolve
		// that breaks a mutual cycle still works: mid-wiring the flag is still false, so the re-entrant call falls
		// through to the lock (__gate is a reentrant monitor), finds the field already set, skips the miss block, and
		// returns the mid-wiring instance - which is exactly what terminates the cycle.
		string fastPathGuard = resolver.WiredFlag.Length == 0
			? $"{resolver.Field} is not null"
			: $"{resolver.Field} is not null && {resolver.WiredFlag}";
		Indent(builder, depth + 1).Append("if (").Append(fastPathGuard).AppendLine(")");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).Append("return ").Append(resolver.Field).AppendLine(";");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();

		Indent(builder, depth + 1).AppendLine("lock (__gate)");
		Indent(builder, depth + 1).AppendLine("{");
		EmitDisposedGuard(builder, depth + 2);
		Indent(builder, depth + 2).Append("if (").Append(resolver.Field).AppendLine(" is null)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).Append(resolver.Field).Append(" = ").Append(resolver.Construction).AppendLine(";");
		if (resolver.Disposal == DisposalTracking.Runtime)
		{
			// A factory's declared return type may hide a concrete disposable, so retain the realized instance
			// only when it genuinely is one. The add stays under the lock that guards the field assignment.
			string test = asyncDisposal
				? " is global::System.IDisposable or global::System.IAsyncDisposable)"
				: " is global::System.IDisposable)";
			Indent(builder, depth + 3).Append("if (").Append(resolver.Field).AppendLine(test);
			Indent(builder, depth + 3).AppendLine("{");
			Indent(builder, depth + 4).Append("(__disposables ??= new global::System.Collections.Generic.List<object>()).Add(").Append(resolver.Field).AppendLine(");");
			Indent(builder, depth + 3).AppendLine("}");
		}
		else if (resolver.Disposal == DisposalTracking.Static)
		{
			Indent(builder, depth + 3).Append("(__disposables ??= new global::System.Collections.Generic.List<object>()).Add(").Append(resolver.Field).AppendLine(");");
		}

		// Deferred members are wired after the instance is cached (still inside the cache-miss block, so it runs
		// exactly once), which is what lets a mutual cycle terminate: the re-entrant resolve returns this cached
		// instance instead of recursing. The wiring flag is set last (a volatile release-write), so the lock-free
		// fast path publishes the instance to other threads only once it is fully wired.
		if (emitDeferred is not null)
		{
			emitDeferred.Invoke(depth + 3);
			if (resolver.WiredFlag.Length != 0)
			{
				Indent(builder, depth + 3).Append(resolver.WiredFlag).AppendLine(" = true;");
			}
		}

		Indent(builder, depth + 2).AppendLine("}");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 1).Append("return ").Append(resolver.Field).AppendLine(";");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     The inputs to <see cref="EmitCachingResolver" />: the method <see cref="Modifiers" /> and return
	///     <see cref="Type" />, the resolver <see cref="Method" /> name and backing <see cref="Field" />, the
	///     <see cref="Construction" /> expression, how the instance is tracked for <see cref="Disposal" /> (not
	///     at all, by the static type, or by a runtime <c>is IDisposable</c> check on the realized factory
	///     output), and the XML doc <see cref="Summary" /> emitted over the resolver. <see cref="WiredFlag" /> is
	///     the volatile "wiring complete" flag guarding the fast path of an instance with deferred members (empty
	///     when the instance has none, so the plain fast path on <see cref="Field" /> alone is emitted).
	/// </summary>
	private readonly struct CachingResolver(string modifiers, string type, string method, string field, string construction, DisposalTracking disposal, string summary, string wiredFlag = "")
	{
		public string Modifiers { get; } = modifiers;

		public string Type { get; } = type;

		public string Method { get; } = method;

		public string Field { get; } = field;

		public string Construction { get; } = construction;

		public DisposalTracking Disposal { get; } = disposal;

		public string Summary { get; } = summary;

		public string WiredFlag { get; } = wiredFlag;
	}

	/// <summary>
	///     The inputs to <see cref="EmitFreshResolver" />: the method <see cref="Modifiers" /> and return
	///     <see cref="Type" />, the resolver <see cref="Method" /> name, its parameter <see cref="Signature" />
	///     (empty for a transient, the runtime arguments for a parameterized service), the
	///     <see cref="Construction" /> expression, how the instance is tracked for <see cref="Disposal" />
	///     (not at all, by the static type, or by a runtime <c>is IDisposable</c> check on the realized factory
	///     output), and the XML doc <see cref="Summary" /> emitted over the resolver.
	/// </summary>
	private readonly struct FreshResolver(string modifiers, string type, string method, string signature, string construction, DisposalTracking disposal, string summary)
	{
		public string Modifiers { get; } = modifiers;

		public string Type { get; } = type;

		public string Method { get; } = method;

		public string Signature { get; } = signature;

		public string Construction { get; } = construction;

		public DisposalTracking Disposal { get; } = disposal;

		public string Summary { get; } = summary;
	}
}
