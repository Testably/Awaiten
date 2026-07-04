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
		Indent(builder, depth).Append("internal static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IAsyncEnumerable<")
			.Append(collection.Service).Append(">> ").Append(method).AppendLine("(Scope __s, global::System.Threading.CancellationToken cancellationToken)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).Append("return ").Append(AsyncCollectionExpression(collectionKey, names, instances, asynchronous: true)).AppendLine(";");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits a non-singleton resolver as an <c>internal static</c> method on the base <c>Scope</c>, taking the
	///     resolving scope as <c>__s</c>. A parameterized service (with <c>[Arg]</c> parameters) is built fresh per
	///     call from its runtime arguments; a scoped service caches on <c>__s</c>; a transient constructs fresh on
	///     <c>__s</c>. Singleton-owned services (singletons and pre-built Instances) are emitted on the <c>Root</c>
	///     instead (see <see cref="EmitRootResolver" />), so this never emits a delegator - a dependency selects the
	///     right owner by calling the target's static resolver directly.
	/// </summary>
	private static void EmitScopeResolver(StringBuilder builder, int depth, int index, EmitContext context)
	{
		InstanceModel instance = context.Instances[index];
		Names names = context.Names;
		bool asyncDisposal = context.AsyncDisposal;
		string type = instance.ConstructedType;
		string resolver = names.Resolver(index);

		// A requesting-type factory embeds the consumer's typeof(…) per call, so it cannot be lowered to a shared
		// cached resolver: its resolver takes the requesting type as a parameter and calls the factory on each
		// invocation (the factory itself may cache, as the canonical logger factory does). It is built fresh per
		// call - the declared lifetime is ignored for caching, exactly so the per-consumer requesting type
		// survives - and a disposable output is still tracked for teardown on the resolving scope.
		if (instance.IsRequestingTypeFactory)
		{
			string requestingConstruction = EmitConstruction(instance, context.Instances, names, context.ServiceToIndex);
			string requestingSignature = $"global::System.Type? {RequestingTypeParameterName}";
			string requestingSummary = $"Resolves {XmlTypeRef(type)} through its requesting-type factory, passing the requesting consumer's typeof(…) (a new instance per call).";
			EmitFreshResolver(builder, depth, new FreshResolver("Scope", type, resolver, requestingSignature, requestingConstruction, DisposalOf(instance), requestingSummary), asyncDisposal);
			return;
		}

		if (instance.IsParameterized)
		{
			string[] argTypes = instance.ArgTypes();
			string signature = string.Join(", ", argTypes.Select((t, i) => $"{t} a{i}"));
			string parameterizedConstruction = EmitConstruction(instance, context.Instances, names, context.ServiceToIndex);
			string parameterizedSummary = $"Resolves {XmlTypeRef(type)} from its <c>[Arg]</c> arguments (a new instance per call).";
			EmitFreshResolver(builder, depth, new FreshResolver("Scope", type, resolver, signature, parameterizedConstruction, DisposalOf(instance), parameterizedSummary), asyncDisposal, DeferredEmitter(builder, instance, "created", context, asynchronous: false));
			return;
		}

		string construction = EmitConstruction(instance, context.Instances, names, context.ServiceToIndex);

		if (instance.Lifetime == Lifetime.Transient)
		{
			string transientSummary = $"Resolves the transient {XmlTypeRef(type)} (a new instance per call).";
			EmitFreshResolver(builder, depth, new FreshResolver("Scope", type, resolver, string.Empty, construction, DisposalOf(instance), transientSummary), asyncDisposal, DeferredEmitter(builder, instance, "created", context, asynchronous: false));
			return;
		}

		string scopedSummary = $"Resolves the scoped {XmlTypeRef(type)} (one instance per scope).";
		string scopedWiredFlag = HasDeferredMembers(instance) ? names.WiredField(index) : string.Empty;
		// The deferred members are wired onto the cached field on the owner (__s.<field>), inside the static resolver.
		EmitCachingResolver(builder, depth, new CachingResolver("Scope", type, resolver, (names.Field(index), scopedWiredFlag), construction, DisposalOf(instance), scopedSummary), asyncDisposal, DeferredEmitter(builder, instance, "__s." + names.Field(index), context, asynchronous: false));
	}

	/// <summary>
	///     The deferred-wiring emitter for an instance - the callback a resolver shape invokes after
	///     construction to assign the instance's <c>[Inject(Deferred = true)]</c> members - or
	///     <see langword="null" /> when it has none, so a plain resolver's emitted code is unchanged. Shared by
	///     every resolver shape (sync/async, fresh/caching) so the hookup cannot drift per emission site.
	/// </summary>
	private static Action<int>? DeferredEmitter(StringBuilder builder, InstanceModel instance, string variable, EmitContext context, bool asynchronous)
		=> HasDeferredMembers(instance)
			? d => EmitDeferredAssignments(builder, d, instance, variable, context, asynchronous)
			: null;

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
		// A static resolver over its owner `__s` (the resolving scope, or the root for a singleton), so it is
		// reachable across Scope/Root without a virtual hop and stays off the instance surface. Runtime [Arg]
		// arguments, when present, follow the owner.
		Indent(builder, depth).Append("internal static ").Append(type).Append(' ').Append(resolver.Method)
			.Append('(').Append(resolver.Owner).Append(" __s");
		if (resolver.Signature.Length > 0)
		{
			builder.Append(", ").Append(resolver.Signature);
		}

		builder.AppendLine(")");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1, "__s.");

		// A transient is not cached, so its deferred members never participate in a terminating cycle (AWT145
		// rejects a transient deferred cycle); they are still wired after construction here - before the owner's
		// own disposal registration, so a dependency first built during wiring registers earlier and is disposed
		// later than this owner. Deferred members also force the `created` variable form so there is an instance
		// to assign through.
		if (resolver.Disposal != DisposalTracking.None || emitDeferred is not null)
		{
			Indent(builder, depth + 1).Append(type).Append(" created = ").Append(construction).AppendLine(";");
			emitDeferred?.Invoke(depth + 1);
			if (resolver.Disposal != DisposalTracking.None)
			{
				EmitFreshDisposalTracking(builder, depth + 1, resolver.Disposal == DisposalTracking.Runtime, asyncDisposal, asyncContext: false);
				builder.AppendLine();
			}

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
			Indent(builder, depth).Append("internal static ").Append(type).Append(' ').Append(resolver).AppendLine("(Root __s)");
			Indent(builder, depth).AppendLine("{");
			EmitDisposedGuard(builder, depth + 1, "__s.");
			Indent(builder, depth + 1).Append("return ").Append(instance.ProductionMember).AppendLine(";");
			Indent(builder, depth).AppendLine("}");
			return;
		}

		string construction = EmitConstruction(instance, context.Instances, names, context.ServiceToIndex);
		string singletonSummary = $"Resolves the singleton {XmlTypeRef(type)} (one instance per container).";
		string singletonWiredFlag = HasDeferredMembers(instance) ? names.WiredField(index) : string.Empty;
		// The deferred members are wired onto the cached field on the owner (__s.<field>), inside the static resolver.
		EmitCachingResolver(builder, depth, new CachingResolver("Root", type, resolver, (names.Field(index), singletonWiredFlag), construction, DisposalOf(instance), singletonSummary), context.AsyncDisposal, DeferredEmitter(builder, instance, "__s." + names.Field(index), context, asynchronous: false));
	}

	/// <summary>
	///     The by-type async dispatch arms: one per unkeyed service key of each async-tainted, non-parameterized
	///     service (routed to its memoizing async resolver), plus one <c>IAsyncEnumerable&lt;T&gt;</c> arm per async
	///     collection (routed to its generated async collection resolver). Each carries the guidance a root-withheld
	///     arm throws off the Root (null when not withheld). They are collected up front so a large set can be split
	///     across chunk methods, staying under RyuJIT's optimization guards - the same cliff the synchronous dispatch
	///     hit before it was chunked.
	/// </summary>
	private static List<(string Service, string AsyncResolver, bool RootOwned, bool RequestingType, string? RootWithheldMessage)> BuildAsyncArms(
		InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool strict, bool syncResolveAfterInit)
	{
		List<(string Service, string AsyncResolver, bool RootOwned, bool RequestingType, string? RootWithheldMessage)> arms = new();
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
			bool rootOwned = IsRootOwned(instances[i]);
			// A requesting-type factory's async resolver takes the requesting type; a top-level ResolveAsync has no
			// requesting consumer, so its arm passes null (mirroring the synchronous by-type dispatch).
			bool requestingType = instances[i].IsRequestingTypeFactory;
			// Keyed registrations are reached only by [FromKey] injection, never by-type resolution.
			foreach (string service in instances[i].Services.AsArray().Where(serviceKey => serviceKey.Key is null).Select(serviceKey => serviceKey.Service))
			{
				arms.Add((service, asyncResolver, rootOwned, requestingType, rootWithheld ? AsyncRootWithheldMessage(service) : null));
			}
		}

		// The IAsyncEnumerable<T> arm of each async collection, materialized through its generated async collection
		// resolver. A collection with a build-on-demand disposable member is root-withheld, mirroring the sync side.
		Dictionary<string, int> implToIndex = new(StringComparer.Ordinal);
		for (int i = 0; i < instances.Length; i++)
		{
			implToIndex[instances[i].ImplementationType] = i;
		}

		CollectionMembership membership = AwaitenGenerator.MembershipIndices(names.Collections, names.KeyedCollections, implToIndex);
		foreach ((ServiceMembers collection, string method) in AsyncByTypeCollections(names, serviceToIndex, syncResolveAfterInit))
		{
			ServiceKey collectionKey = new(collection.Service, collection.Key);
			string shape = AwaitenGenerator.AsyncEnumerableShapeType(collection.Service);
			bool rootWithheld = membership.Collections.TryGetValue(collectionKey, out List<int>? members)
			                    && members.Any(member => IsFuncWithheld(instances, member, serviceToIndex, membership, strict));
			// An async collection resolver is emitted on the base Scope (never root-owned), so it is called with __s.
			arms.Add((shape, method, false, false, rootWithheld ? CollectionAsyncRootWithheldMessage(shape) : null));
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
	private static void EmitAsyncResolutionApi(ApiRegions regions, int depth, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool strict, bool syncResolveAfterInit)
	{
		const string task = "global::System.Threading.Tasks.Task";
		(StringBuilder members, StringBuilder fields, StringBuilder helpers) = regions;
		StringBuilder builder = members;
		Separate(members);

		List<(string Service, string AsyncResolver, bool RootOwned, bool RequestingType, string? RootWithheldMessage)> arms = BuildAsyncArms(instances, names, serviceToIndex, strict, syncResolveAfterInit);

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

		EmitAsyncBucketDispatch(fields, helpers, depth, arms);
		EmitAsObjectHelper(helpers, depth);
	}

	/// <summary>
	///     Emits the async dispatch table: an <c>__AsyncBucket</c> slot type and the <c>__asyncBuckets</c> /
	///     <c>__asyncBucketSize</c> table, built once through field initializers (which coexist with the
	///     synchronous static constructor). Each slot's delegate awaits the async resolver and converts the result
	///     to <c>Task&lt;object&gt;</c>; a root-withheld arm bakes its guidance throw into the delegate (the Root
	///     throws, a child scope resolves). No forwarder methods are needed - the delegates are inline lambdas. The
	///     table fields are routed into <paramref name="fields" /> (the fields region); the <c>__AsyncBucket</c>
	///     slot type and <c>__BuildAsyncBuckets</c> into <paramref name="helpers" />.
	/// </summary>
	private static void EmitAsyncBucketDispatch(StringBuilder fields, StringBuilder helpers, int depth, List<(string Service, string AsyncResolver, bool RootOwned, bool RequestingType, string? RootWithheldMessage)> arms)
	{
		const string func = "global::System.Func<Scope, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<object>>";
		int bucketCount = BucketCount(arms.Count);

		Separate(fields);
		Indent(fields, depth).Append("private const int __asyncBucketCount = ").Append(bucketCount).AppendLine(";");
		Indent(fields, depth).AppendLine("private static readonly __AsyncBucket[] __asyncBuckets = __BuildAsyncBuckets();");
		Indent(fields, depth).AppendLine("private static readonly int __asyncBucketSize = __asyncBuckets.Length / __asyncBucketCount;");

		Separate(helpers);
		AppendXmlSummary(helpers, depth, "One slot of the async by-type dispatch table.");
		Indent(helpers, depth).AppendLine("private readonly struct __AsyncBucket");
		Indent(helpers, depth).AppendLine("{");
		Indent(helpers, depth + 1).AppendLine("public readonly global::System.Type? Key;");
		Indent(helpers, depth + 1).Append("public readonly ").Append(func).AppendLine(" Resolve;");
		Indent(helpers, depth + 1).Append("public __AsyncBucket(global::System.Type? key, ").Append(func).AppendLine(" resolve)");
		Indent(helpers, depth + 1).AppendLine("{");
		Indent(helpers, depth + 2).AppendLine("Key = key;");
		Indent(helpers, depth + 2).AppendLine("Resolve = resolve;");
		Indent(helpers, depth + 1).AppendLine("}");
		Indent(helpers, depth).AppendLine("}");
		helpers.AppendLine();

		AppendXmlSummary(helpers, depth, "Builds the async by-type dispatch table.");
		Indent(helpers, depth).AppendLine("private static __AsyncBucket[] __BuildAsyncBuckets()");
		Indent(helpers, depth).AppendLine("{");
		Indent(helpers, depth + 1).AppendLine("__AsyncBucket[] __entries =");
		Indent(helpers, depth + 1).AppendLine("{");
		foreach ((string service, string asyncResolver, bool rootOwned, bool requestingType, string? rootWithheldMessage) in arms)
		{
			// A root-owned (singleton) async resolver lives on the Root and caches on the root, so it is called
			// Root.ResolveXAsync(__s.__root, __ct); a scoped/transient/collection one lives on the Scope, so it is
			// called ResolveXAsync(__s, __ct) over the resolving scope. A requesting-type factory (never root-owned)
			// takes the requesting type; a top-level ResolveAsync has no consumer, so it passes null.
			string call;
			if (rootOwned)
			{
				call = $"Root.{asyncResolver}(__s.__root, __ct)";
			}
			else if (requestingType)
			{
				call = $"{asyncResolver}(__s, null, __ct)";
			}
			else
			{
				call = $"{asyncResolver}(__s, __ct)";
			}

			string resolve = rootWithheldMessage is not null
				? $"static (__s, __ct) => __s is Root ? throw new global::System.InvalidOperationException({rootWithheldMessage}) : __AsObject({call})"
				: $"static (__s, __ct) => __AsObject({call})";
			Indent(helpers, depth + 2).Append("new __AsyncBucket(typeof(").Append(service).Append("), ").Append(resolve).AppendLine("),");
		}

		Indent(helpers, depth + 1).AppendLine("};");
		helpers.AppendLine();
		EmitBucketDistribution(helpers, depth + 1, "__AsyncBucket", "__asyncBucketCount");
		Indent(helpers, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits the <c>__AsObject&lt;T&gt;</c> helper that converts a <c>Task&lt;T&gt;</c> async resolver result to
	///     the <c>Task&lt;object&gt;</c> the by-type <c>ResolveAsync</c> returns. Called only when at least one
	///     async-tainted service exists (otherwise nothing references it).
	/// </summary>
	private static void EmitAsObjectHelper(StringBuilder helpers, int depth)
	{
		const string task = "global::System.Threading.Tasks.Task";
		Separate(helpers);
		Indent(helpers, depth).Append("private static async ").Append(task)
			.AppendLine("<object> __AsObject<T>(global::System.Threading.Tasks.Task<T> __task) => (object)(await __task.ConfigureAwait(false))!;");
	}

	/// <summary>
	///     Emits the async resolver for a non-singleton async-tainted instance as an <c>internal static</c> method
	///     on the base <c>Scope</c>, over the resolving scope <c>__s</c>: a scoped service memoizes its
	///     construction-and-initialization <c>Task</c> on <c>__s</c>; a transient (and a parameterized service)
	///     constructs, initializes and returns each call. Singleton async resolvers are emitted on the <c>Root</c>
	///     (see <see cref="EmitAsyncRootResolver" />).
	/// </summary>
	private static void EmitAsyncScopeResolver(StringBuilder builder, int depth, int index, EmitContext context)
	{
		InstanceModel instance = context.Instances[index];
		Names names = context.Names;

		// A deferred [Inject(Deferred = true)] member is wired after the instance is constructed, awaiting an
		// async-tainted member exactly like an async-tainted constructor argument. Null when there is none.
		Action<int>? emitDeferred = DeferredEmitter(builder, instance, "created", context, asynchronous: true);

		// A requesting-type factory that is async-tainted (an async factory, or one that awaits an async
		// dependency) builds fresh per call with the consumer's typeof(…) embedded, so - like a parameterized
		// service - it never caches: a fresh async resolver takes the requesting type alongside the token. AWT162
		// forbids it from also being parameterized, so it has no runtime arguments to carry.
		if (instance.IsRequestingTypeFactory)
		{
			string requestingSummary = $"Asynchronously resolves {XmlTypeRef(instance.ConstructedType)} through its requesting-type factory, passing the requesting consumer's typeof(…) (a new instance per call).";
			EmitAsyncFreshResolver(builder, depth, index, context, $"global::System.Type? {RequestingTypeParameterName}, ", emitDeferred, requestingSummary);
			return;
		}

		// A parameterized async service is built fresh per call from its runtime arguments AND awaits
		// initialization, so it is reached only through Func<TArg…, Task<T>>. Its async resolver takes the
		// arguments alongside the token, never caching (a parameterized service is always transient).
		if (instance.IsParameterized)
		{
			string[] argTypes = instance.ArgTypes();
			string argSignature = string.Join("", argTypes.Select((t, i) => $"{t} a{i}, "));
			EmitAsyncFreshResolver(builder, depth, index, context, argSignature, emitDeferred);
			return;
		}

		if (instance.Lifetime == Lifetime.Transient)
		{
			EmitAsyncFreshResolver(builder, depth, index, context, emitDeferred: emitDeferred);
			return;
		}

		// Scoped: a memoized Task on the scope guards construction-and-initialization.
		string construction = EmitConstruction(instance, context.Instances, names, context.ServiceToIndex, asynchronous: true);
		EmitAsyncCachingResolver(builder, depth, index, context, construction, "Scope", emitDeferred);
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
	private static void EmitDelegatingSyncResolver(StringBuilder builder, int depth, int index, InstanceModel instance, Names names, string owner)
	{
		// A parameterized service forwards its runtime arguments to the async resolver: the synchronous resolver
		// takes the same arguments, blocks on the (per-call) async resolver, and so still drives initialization.
		// The async resolver is emitted on the same host (Scope or Root), so it is called with `__s` directly.
		string[] argTypes = instance.ArgTypes();
		string signature = string.Join("", argTypes.Select((t, i) => $", {t} a{i}"));
		string forward = string.Join("", argTypes.Select((_, i) => "a" + i + ", "));

		// A requesting-type factory's async resolver takes the requesting type in place of runtime arguments
		// (AWT162 forbids both); the blocking sync resolver takes and forwards it identically.
		if (instance.IsRequestingTypeFactory)
		{
			signature = $", global::System.Type? {RequestingTypeParameterName}";
			forward = RequestingTypeParameterName + ", ";
		}

		AppendXmlSummary(builder, depth,
			$"Resolves {XmlTypeRef(instance.ConstructedType)} by blocking on its async resolver (<c>SyncResolveAfterInit</c>).");
		Indent(builder, depth).Append("internal static ").Append(instance.ConstructedType).Append(' ')
			.Append(names.Resolver(index)).Append('(').Append(owner).Append(" __s").Append(signature).AppendLine(")");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1, "__s.");
		Indent(builder, depth + 1).Append("return ").Append(names.AsyncResolver(index))
			.Append("(__s, ").Append(forward).AppendLine("default).GetAwaiter().GetResult();");
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
		Action<int>? emitDeferred = DeferredEmitter(builder, instance, "created", context, asynchronous: true);
		EmitAsyncCachingResolver(builder, depth, index, context, construction, "Root", emitDeferred);
	}

	/// <summary>
	///     Emits a memoizing async resolver whose construction-and-initialization runs in a local <c>async</c>
	///     function. The resolver returns the cached <c>Task</c> if it exists (lock-free), otherwise assigns it
	///     under <c>__gate</c> so the local function runs once; that function constructs, registers for disposal
	///     and awaits <c>InitializeAsync</c>. A task that faults or is canceled is evicted from the cache so a
	///     later call retries rather than replaying the same failure (and so one caller's cancellation does not
	///     permanently poison a shared singleton).
	/// </summary>
	private static void EmitAsyncCachingResolver(StringBuilder builder, int depth, int index, EmitContext context, string construction, string owner, Action<int>? emitDeferred = null)
	{
		InstanceModel instance = context.Instances[index];
		Names names = context.Names;
		string type = instance.ConstructedType;
		string asyncResolver = names.AsyncResolver(index);
		string asyncField = names.AsyncField(index);
		string creator = names.AsyncCreator(index);
		const string task = "global::System.Threading.Tasks.Task";
		const string ct = "global::System.Threading.CancellationToken cancellationToken";

		string summary = owner == "Root"
			? $"Asynchronously resolves the singleton {XmlTypeRef(type)} (one instance per container)."
			: $"Asynchronously resolves the scoped {XmlTypeRef(type)} (one instance per scope).";
		AppendXmlSummary(builder, depth, summary);
		// A static memoizing async resolver over its owner `__s` (the scope for a scoped service, the root for a
		// singleton), caching the construction-and-initialization Task on that owner's field.
		Indent(builder, depth).Append("internal static ").Append(task).Append('<').Append(type).Append("> ")
			.Append(asyncResolver).Append('(').Append(owner).Append(" __s, ").Append(ct).AppendLine(")");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1, "__s.");
		Indent(builder, depth + 1).Append(task).Append('<').Append(type).Append(">? __cached = __s.").Append(asyncField).AppendLine(";");
		// A faulted or canceled task is not memoized: treat it as absent so this call rebuilds rather than
		// replaying a past failure (or a previous caller's cancellation) forever. A successful or still-running
		// task is returned without taking the lock.
		Indent(builder, depth + 1).AppendLine("if (__cached is not null && !__cached.IsFaulted && !__cached.IsCanceled)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return __cached;");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("lock (__s.__gate)");
		Indent(builder, depth + 1).AppendLine("{");
		EmitDisposedGuard(builder, depth + 2, "__s.");
		Indent(builder, depth + 2).Append(task).Append('<').Append(type).Append(">? __pending = __s.").Append(asyncField).AppendLine(";");
		Indent(builder, depth + 2).AppendLine("if (__pending is null || __pending.IsFaulted || __pending.IsCanceled)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).Append("__pending = ").Append(creator).AppendLine("();");
		Indent(builder, depth + 3).Append("__s.").Append(asyncField).AppendLine(" = __pending;");
		Indent(builder, depth + 2).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 2).AppendLine("return __pending;");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();

		// The construction-and-initialization is a local async function so the synchronous disposed-guard above
		// runs eagerly; it captures `__s` and `cancellationToken`. Deferred members are wired before the owner's
		// own disposal registration, so a dependency first built during wiring registers earlier and is disposed
		// later than this owner (reverse teardown order). A failed wiring faults the memoized task, which the
		// resolver evicts, so a later call retries.
		Indent(builder, depth + 1).Append("async ").Append(task).Append('<').Append(type).Append("> ").Append(creator).AppendLine("()");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).Append(type).Append(" created = ").Append(construction).AppendLine(";");
		emitDeferred?.Invoke(depth + 2);
		EmitAsyncDisposableRegistration(builder, depth + 2, instance, context.AsyncDisposal);
		EmitAsyncInitialization(builder, depth + 2, instance, "created");
		Indent(builder, depth + 2).AppendLine("return created;");
		Indent(builder, depth + 1).AppendLine("}");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits an async resolver that constructs, initializes and returns a fresh instance on every call (a
	///     transient, or a parameterized service that additionally takes the runtime arguments named in
	///     <paramref name="argSignature" />). A disposable instance is registered for teardown on the owner.
	/// </summary>
	private static void EmitAsyncFreshResolver(StringBuilder builder, int depth, int index, EmitContext context, string argSignature = "", Action<int>? emitDeferred = null, string? summaryOverride = null)
	{
		InstanceModel instance = context.Instances[index];
		Names names = context.Names;
		string type = instance.ConstructedType;
		string construction = EmitConstruction(instance, context.Instances, names, context.ServiceToIndex, asynchronous: true);
		const string task = "global::System.Threading.Tasks.Task";
		const string ct = "global::System.Threading.CancellationToken cancellationToken";

		string summary = summaryOverride ?? (argSignature.Length > 0
			? $"Asynchronously resolves {XmlTypeRef(type)} from its <c>[Arg]</c> arguments (a new instance per call)."
			: $"Asynchronously resolves the transient {XmlTypeRef(type)} (a new instance per call).");
		AppendXmlSummary(builder, depth, summary);
		// A static async resolver over the resolving scope `__s`; fresh per call (transient or parameterized), so
		// it never caches. Runtime [Arg] arguments, when present, precede the cancellation token.
		Indent(builder, depth).Append("internal static async ").Append(task).Append('<').Append(type).Append("> ")
			.Append(names.AsyncResolver(index)).Append("(Scope __s, ").Append(argSignature).Append(ct).AppendLine(")");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1, "__s.");
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
	///     Emits the base <c>Scope</c>'s private <c>__WarmAsync</c>: it eagerly warms this scope's
	///     async-initialized scoped services in dependency order. It is private (not on <c>IAwaitenScope</c>)
	///     because a child scope is warmed only at creation, through <c>CreateScopeAsync</c> - its sole caller,
	///     declared on the same <c>Scope</c> - so nothing outside the type ever reaches it.
	/// </summary>
	private static void EmitScopeInitializeAsync(StringBuilder builder, int depth, InstanceModel[] instances, Names names)
		=> EmitWarmUp(builder, depth, instances, names, "private", "__WarmAsync", Lifetime.Scoped);

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
	///     the returned task is awaited: with targets the entry validates and delegates to a local <c>async</c>
	///     function, so a disposed container throws immediately on the call.
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

		AppendXmlSummary(builder, depth, summary);
		Indent(builder, depth).Append(modifiers).Append(' ').Append(task).Append(' ').Append(method).Append('(').Append(ctParam).AppendLine(")");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1);
		Indent(builder, depth + 1).AppendLine("return Core();");
		builder.AppendLine();

		// The warming loop is a local async function so the synchronous disposed-guard above runs eagerly on the
		// call; it captures `cancellationToken` and `this`. The warm-up runs on the same class that hosts these
		// resolvers (the base Scope warms its scoped async services; the Root warms its singletons), so each static
		// resolver is called with `this`.
		Indent(builder, depth + 1).Append("async ").Append(task).AppendLine(" Core()");
		Indent(builder, depth + 1).AppendLine("{");
		foreach (int i in targets)
		{
			Indent(builder, depth + 2).Append("await ").Append(names.AsyncResolver(i)).AppendLine("(this, cancellationToken).ConfigureAwait(false);");
		}

		Indent(builder, depth + 1).AppendLine("}");
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

		// The disposed-guard runs synchronously (eager state validation, like ResolveAsync): the entry validates
		// and returns a local async function's task, so a disposed container throws immediately on the call rather
		// than only when the returned task is awaited.
		AppendXmlSummary(builder, depth,
			"Opens a child scope, warming its async-initialized scoped services.");
		Indent(builder, depth).Append("public ").Append(task)
			.AppendLine("<global::Awaiten.IAwaitenScope> CreateScopeAsync(global::System.Threading.CancellationToken cancellationToken = default)");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1);
		Indent(builder, depth + 1).AppendLine("return Core();");
		builder.AppendLine();

		Indent(builder, depth + 1).Append("async ").Append(task)
			.AppendLine("<global::Awaiten.IAwaitenScope> Core()");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("Scope __scope = CreateScope();");
		Indent(builder, depth + 2).AppendLine("try");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("await __scope.__WarmAsync(cancellationToken).ConfigureAwait(false);");
		Indent(builder, depth + 2).AppendLine("}");
		Indent(builder, depth + 2).AppendLine("catch");
		Indent(builder, depth + 2).AppendLine("{");
		// Initialization failed (or was canceled) partway through: dispose the scope so the instances it
		// already built and tracked are torn down rather than leaked, then surface the original failure.
		Indent(builder, depth + 3).AppendLine("__scope.Dispose();");
		Indent(builder, depth + 3).AppendLine("throw;");
		Indent(builder, depth + 2).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 2).AppendLine("return __scope;");
		Indent(builder, depth + 1).AppendLine("}");
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
			// A requesting-type factory is built fresh per consumer (never cached), so there is nothing to warm -
			// and its async resolver takes the requesting type, which the argument-free warm-up call cannot supply.
			if (instances[i].IsAsyncTainted && instances[i].Lifetime == lifetime && !instances[i].IsParameterized && !instances[i].IsRequestingTypeFactory)
			{
				targets.Add(i);
			}
		}

		return targets.ToArray();
	}

	/// <summary>
	///     Emits a lock-free-read, lock-on-write cached resolver: return the cached field if set, otherwise
	///     construct once under <c>lock (__gate)</c>, registering a disposable instance for teardown. An instance
	///     with deferred (<c>[Inject(Deferred = true)]</c>) members additionally wraps its cache-miss block in a
	///     wiring episode (see the emitted comments): the field is published before wiring so a re-entrant resolve
	///     can terminate a cycle, the wiring flags of everything the episode published are committed only by the
	///     outermost frame (so another thread's fast path never observes a half-wired instance, not even
	///     transitively through a peer), and a failed episode unpublishes what it built so a later resolve retries
	///     instead of silently returning a half-wired instance forever.
	/// </summary>
	private static void EmitCachingResolver(StringBuilder builder, int depth, in CachingResolver resolver, bool asyncDisposal, Action<int>? emitDeferred = null)
	{
		string field = resolver.Field;
		string construction = resolver.Construction;
		DisposalTracking disposal = resolver.Disposal;
		bool deferred = resolver.WiredFlag.Length != 0;

		AppendXmlSummary(builder, depth, resolver.Summary);
		// A static caching resolver over its owner `__s` (the scope for a scoped service, the root for a
		// singleton), caching on that owner's field so a child scope caches per-scope and a singleton caches once.
		Indent(builder, depth).Append("internal static ").Append(resolver.Type).Append(' ').Append(resolver.Method)
			.Append('(').Append(resolver.Owner).AppendLine(" __s)");
		Indent(builder, depth).AppendLine("{");
		EmitDisposedGuard(builder, depth + 1, "__s.");

		// The lock-free fast path returns the cached field without taking the lock. When the instance has deferred
		// ([Inject(Deferred = true)]) members it also tests the volatile wiring flag: those members are wired only
		// after the field is published (inside the lock, below), so gating on the field alone could hand a concurrent
		// caller a published-but-half-wired instance. The flag is committed by the outermost frame of the wiring
		// episode, so a caller sees it true only once every instance the episode published is fully wired (and its
		// acquire-read makes the deferred writes visible). The re-entrant same-thread resolve that breaks a mutual
		// cycle still works: mid-wiring the flag is still false, so the re-entrant call falls through to the lock
		// (__gate is a reentrant monitor), finds the field already set, skips the miss block, and returns the
		// mid-wiring instance - which is exactly what terminates the cycle.
		string fastPathGuard = deferred
			? $"__s.{field} is not null && __s.{resolver.WiredFlag}"
			: $"__s.{field} is not null";
		Indent(builder, depth + 1).Append("if (").Append(fastPathGuard).AppendLine(")");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).Append("return __s.").Append(field).AppendLine(";");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();

		Indent(builder, depth + 1).AppendLine("lock (__s.__gate)");
		Indent(builder, depth + 1).AppendLine("{");
		EmitDisposedGuard(builder, depth + 2, "__s.");
		Indent(builder, depth + 2).Append("if (__s.").Append(field).AppendLine(" is null)");
		Indent(builder, depth + 2).AppendLine("{");
		if (!deferred)
		{
			Indent(builder, depth + 3).Append("__s.").Append(field).Append(" = ").Append(construction).AppendLine(";");
			EmitCachedDisposalRegistration(builder, depth + 3, field, disposal, asyncDisposal);
		}
		else
		{
			// The wiring episode: everything published while __wiring is non-zero is still being wired (possibly
			// re-entrantly, across a cycle). The instance is published before its deferred members are wired - that
			// is what lets the re-entrant resolve of a mutual cycle return it - and registered for disposal after
			// them, so a dependency first built during wiring registers earlier and is disposed later than this
			// owner (reverse teardown order preserves dependency-outlives-dependent).
			Indent(builder, depth + 3).AppendLine("__s.__wiring++;");
			Indent(builder, depth + 3).AppendLine("try");
			Indent(builder, depth + 3).AppendLine("{");
			Indent(builder, depth + 4).Append("__s.").Append(field).Append(" = ").Append(construction).AppendLine(";");
			emitDeferred?.Invoke(depth + 4);
			EmitCachedDisposalRegistration(builder, depth + 4, field, disposal, asyncDisposal);
			Indent(builder, depth + 3).AppendLine("}");
			Indent(builder, depth + 3).AppendLine("catch");
			Indent(builder, depth + 3).AppendLine("{");
			// A failed wiring episode must not leave a half-wired instance published: a later resolve would skip
			// the miss block (field non-null) and silently return it forever. Unpublish this field and - from the
			// outermost frame - every other instance the failed episode published (their flags are still false),
			// so the next resolve rebuilds instead. Peers cached in the failed episode may keep a reference to an
			// unpublished instance; they are unpublished with it, so nothing published survives half-consistent.
			Indent(builder, depth + 4).Append("__s.").Append(field).AppendLine(" = null;");
			Indent(builder, depth + 4).AppendLine("if (__s.__wiring == 1)");
			Indent(builder, depth + 4).AppendLine("{");
			Indent(builder, depth + 5).AppendLine("__s.__RollbackWiring();");
			Indent(builder, depth + 4).AppendLine("}");
			builder.AppendLine();
			Indent(builder, depth + 4).AppendLine("throw;");
			Indent(builder, depth + 3).AppendLine("}");
			Indent(builder, depth + 3).AppendLine("finally");
			Indent(builder, depth + 3).AppendLine("{");
			// Only the outermost frame commits the wiring flags: a nested (re-entrant) frame's instance may still
			// be referenced by a half-wired outer participant, so flagging it early would let another thread's
			// fast path observe that half-wired participant transitively. After a rollback the commit is a no-op
			// (everything unwired was unpublished).
			Indent(builder, depth + 4).AppendLine("__s.__wiring--;");
			Indent(builder, depth + 4).AppendLine("if (__s.__wiring == 0)");
			Indent(builder, depth + 4).AppendLine("{");
			Indent(builder, depth + 5).AppendLine("__s.__CommitWiring();");
			Indent(builder, depth + 4).AppendLine("}");
			Indent(builder, depth + 3).AppendLine("}");
		}

		Indent(builder, depth + 2).AppendLine("}");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 1).Append("return __s.").Append(field).AppendLine(";");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits the disposal registration of a freshly cached instance, inside the cache-miss block (under the
	///     lock that guards the field assignment). With <see cref="DisposalTracking.Runtime" /> (a factory output,
	///     whose declared return type may hide a concrete disposable) the add is gated on a runtime test so only
	///     genuinely-disposable outputs are retained.
	/// </summary>
	private static void EmitCachedDisposalRegistration(StringBuilder builder, int depth, string field, DisposalTracking disposal, bool asyncDisposal)
	{
		if (disposal == DisposalTracking.Runtime)
		{
			string test = asyncDisposal
				? " is global::System.IDisposable or global::System.IAsyncDisposable)"
				: " is global::System.IDisposable)";
			Indent(builder, depth).Append("if (__s.").Append(field).AppendLine(test);
			Indent(builder, depth).AppendLine("{");
			Indent(builder, depth + 1).Append("(__s.__disposables ??= new global::System.Collections.Generic.List<object>()).Add(__s.").Append(field).AppendLine(");");
			Indent(builder, depth).AppendLine("}");
		}
		else if (disposal == DisposalTracking.Static)
		{
			Indent(builder, depth).Append("(__s.__disposables ??= new global::System.Collections.Generic.List<object>()).Add(__s.").Append(field).AppendLine(");");
		}
	}

	/// <summary>
	///     The inputs to <see cref="EmitCachingResolver" />: the <see cref="Owner" /> type of the static resolver's
	///     <c>__s</c> parameter and return <see cref="Type" />, the resolver <see cref="Method" /> name and backing <see cref="Field" />, the
	///     <see cref="Construction" /> expression, how the instance is tracked for <see cref="Disposal" /> (not
	///     at all, by the static type, or by a runtime <c>is IDisposable</c> check on the realized factory
	///     output), and the XML doc <see cref="Summary" /> emitted over the resolver. <see cref="WiredFlag" /> is
	///     the volatile "wiring complete" flag guarding the fast path of an instance with deferred members (empty
	///     when the instance has none, so the plain fast path on <see cref="Field" /> alone is emitted).
	/// </summary>
	private readonly struct CachingResolver(string owner, string type, string method, (string Field, string WiredFlag) cache, string construction, DisposalTracking disposal, string summary)
	{
		// The declaring/owner type of the static resolver's `__s` parameter: <c>Scope</c> for a scoped resolver,
		// <c>Root</c> for a singleton one - it is always emitted on that type and caches on that owner.
		public string Owner { get; } = owner;

		public string Type { get; } = type;

		public string Method { get; } = method;

		public string Field { get; } = cache.Field;

		public string Construction { get; } = construction;

		public DisposalTracking Disposal { get; } = disposal;

		public string Summary { get; } = summary;

		public string WiredFlag { get; } = cache.WiredFlag;
	}

	/// <summary>
	///     The inputs to <see cref="EmitFreshResolver" />: the <see cref="Owner" /> type of the static resolver's
	///     <c>__s</c> parameter and return <see cref="Type" />, the resolver <see cref="Method" /> name, its parameter <see cref="Signature" />
	///     (empty for a transient, the runtime arguments for a parameterized service), the
	///     <see cref="Construction" /> expression, how the instance is tracked for <see cref="Disposal" />
	///     (not at all, by the static type, or by a runtime <c>is IDisposable</c> check on the realized factory
	///     output), and the XML doc <see cref="Summary" /> emitted over the resolver.
	/// </summary>
	private readonly struct FreshResolver(string owner, string type, string method, string signature, string construction, DisposalTracking disposal, string summary)
	{
		// The declaring/owner type of the static resolver's `__s` parameter: <c>Scope</c> for a scoped or
		// transient resolver, <c>Root</c> for a (parameterized-on-root) one - it is always emitted on that type.
		public string Owner { get; } = owner;

		public string Type { get; } = type;

		public string Method { get; } = method;

		public string Signature { get; } = signature;

		public string Construction { get; } = construction;

		public DisposalTracking Disposal { get; } = disposal;

		public string Summary { get; } = summary;
	}
}
