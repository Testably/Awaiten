using System.Text;
using Awaiten.SourceGenerators.Entities;
using Microsoft.CodeAnalysis.CSharp;

namespace Awaiten.SourceGenerators;

internal static partial class Sources
{
	/// <summary>
	///     The name of the synthetic <c>System.Type?</c> parameter a requesting-type factory resolver takes and
	///     forwards into the factory method's <c>[RequestingType]</c> parameter.
	/// </summary>
	private const string RequestingTypeParameterName = "__requestingType";

	/// <summary>
	///     The <c>typeof(…)</c> literal an instance presents as the requesting type to any requesting-type
	///     factory it consumes: its own constructed type (the declaring type of the member being satisfied).
	/// </summary>
	private static string RequestingTypeOf(InstanceModel instance) => $"typeof({instance.ConstructedType})";

	// Whether the __AsyncArray<T> helper is used: some instance injects an IAsyncEnumerable<T>, or some unkeyed,
	// non-suppressed collection is offered by type as IAsyncEnumerable<T> (skipping a registered async shape).
	private static bool NeedsAsyncArrayHelper(InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex)
		=> instances.Any(instance => instance.ConstructorParameters.AsArray().Any(p => p.Kind == DependencyKind.AsyncEnumerable))
		   || names.Collections.Any(collection => collection.Key is null
		                                          && !SynthesisSuppressed(serviceToIndex, collection.Service)
		                                          && !AsyncShapeRegistered(serviceToIndex, collection.Service));

	/// <summary>
	///     Emits the <c>__AsyncArray&lt;T&gt;</c> helper: a minimal <c>IAsyncEnumerable&lt;T&gt;</c> /
	///     <c>IAsyncEnumerator&lt;T&gt;</c> replaying an eagerly-materialized array. Written by hand, not as an
	///     <c>async</c> iterator, so it needs no async-iterator state machine builder (absent on net48 /
	///     netstandard2.0 even with Microsoft.Bcl.AsyncInterfaces).
	/// </summary>
	private static void EmitAsyncArrayHelper(StringBuilder builder, int depth)
	{
		AppendXmlSummary(builder, depth,
			"An eagerly materialized <c>IAsyncEnumerable&lt;T&gt;</c> over already-initialized members.");
		Indent(builder, depth).AppendLine("private sealed class __AsyncArray<T> : global::System.Collections.Generic.IAsyncEnumerable<T>, global::System.Collections.Generic.IAsyncEnumerator<T>");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("private readonly T[] __items;");
		Indent(builder, depth + 1).AppendLine("private int __index = -1;");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("public __AsyncArray(T[] items) => __items = items;");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("public global::System.Collections.Generic.IAsyncEnumerator<T> GetAsyncEnumerator(global::System.Threading.CancellationToken cancellationToken = default)");
		Indent(builder, depth + 2).AppendLine("=> new __AsyncArray<T>(__items);");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("public T Current => __items[__index];");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("public global::System.Threading.Tasks.ValueTask<bool> MoveNextAsync()");
		Indent(builder, depth + 2).AppendLine("=> new global::System.Threading.Tasks.ValueTask<bool>(++__index < __items.Length);");
		builder.AppendLine();
		Indent(builder, depth + 1).AppendLine("public global::System.Threading.Tasks.ValueTask DisposeAsync() => default;");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     A <c>new T[] { resolverA(), resolverB(), … }</c> expression: every registration of the collection's
	///     (element type, key), materialized eagerly in registration order. The array satisfies every supported
	///     collection shape. Each member calls its static resolver over the current owner <c>__s</c> (a singleton
	///     on the <c>Root</c>, a scoped/transient on the <c>Scope</c>).
	/// </summary>
	private static string CollectionLiteral(ServiceKey collection, Names names, InstanceModel[] instances)
	{
		string[] resolvers = names.CollectionResolvers(collection);
		int[] indices = names.CollectionMemberIndices(collection);
		string items = string.Join(", ", resolvers.Select((resolver, m) => ResolveCall(resolver, IsRootOwned(instances[indices[m]]))));
		return $"new {collection.Service}[] {{ {items} }}";
	}

	/// <summary>
	///     A <c>new Dictionary&lt;string, TService&gt; { ["a"] = ResolveA(__s), … }</c> expression: every keyed
	///     registration of the service, keyed by its <c>[Key]</c> in registration order, satisfying the requested
	///     <c>IReadOnlyDictionary&lt;string, TService&gt;</c>. Each member calls its static resolver over the
	///     current owner <c>__s</c>, exactly like <see cref="CollectionLiteral" />.
	/// </summary>
	private static string KeyedCollectionLiteral(string service, Names names)
	{
		string items = string.Join(", ", names.KeyedCollectionResolvers(service)
			.Select(member => $"[{SymbolDisplay.FormatLiteral(member.Key, quote: true)}] = {ResolveCall(member.Resolver, member.RootOwned)}"));
		return $"new global::System.Collections.Generic.Dictionary<string, {service}> {{ {items} }}";
	}

	/// <summary>
	///     A <c>new __AsyncArray&lt;T&gt;(new T[] { … })</c> expression producing an <c>IAsyncEnumerable&lt;T&gt;</c>
	///     over every registration of the collection's (element type, key). When <paramref name="asynchronous" />
	///     is set each async-tainted member is awaited through its async resolver; the array is then wrapped in the
	///     <c>__AsyncArray&lt;T&gt;</c> replay helper.
	/// </summary>
	private static string AsyncCollectionExpression(ServiceKey collection, Names names, InstanceModel[] instances, bool asynchronous)
	{
		string[] resolvers = names.CollectionResolvers(collection);
		int[] indices = names.CollectionMemberIndices(collection);
		string[] items = new string[resolvers.Length];
		for (int m = 0; m < resolvers.Length; m++)
		{
			bool rootOwned = IsRootOwned(instances[indices[m]]);
			items[m] = asynchronous && instances[indices[m]].IsAsyncTainted
				? $"await {AsyncResolveCall(names.AsyncResolver(indices[m]), rootOwned, "cancellationToken")}.ConfigureAwait(false)"
				: ResolveCall(resolvers[m], rootOwned);
		}

		return $"new __AsyncArray<{collection.Service}>(new {collection.Service}[] {{ {string.Join(", ", items)} }})";
	}

	/// <summary>
	///     A <c>Task&lt;C&gt;</c> expression producing the awaited collection of the parameter's (element type, key).
	///     When every member is synchronous it is a completed <c>Task.FromResult&lt;C&gt;</c>; otherwise an
	///     immediately-invoked async lambda awaits each async-tainted member and casts the array to <c>C</c>. Awaited
	///     members receive the resolve-time token only on the async construction path (<c>default</c> otherwise).
	/// </summary>
	private static string AwaitedCollectionExpression(ParameterModel parameter, Names names, InstanceModel[] instances, bool asynchronous)
		=> AwaitedCollectionExpression(new ServiceKey(parameter.ServiceType, parameter.Key), parameter.AwaitedCollectionType!, names, instances, asynchronous);

	/// <summary>
	///     The <c>Task&lt;C&gt;</c> expression for the collection under <paramref name="collection" /> cast to the
	///     inner collection shape <paramref name="shape" />. Shared by the injection path (which supplies the
	///     parameter's exact shape) and the by-type dispatch (which offers every shape). See the parameter overload.
	/// </summary>
	private static string AwaitedCollectionExpression(ServiceKey collection, string shape, Names names, InstanceModel[] instances, bool asynchronous)
	{
		string[] resolvers = names.CollectionResolvers(collection);
		int[] indices = names.CollectionMemberIndices(collection);

		bool anyAsync = false;
		for (int m = 0; m < indices.Length; m++)
		{
			anyAsync |= instances[indices[m]].IsAsyncTainted;
		}

		if (!anyAsync)
		{
			string syncItems = string.Join(", ", resolvers.Select((resolver, m) => ResolveCall(resolver, IsRootOwned(instances[indices[m]]))));
			return $"global::System.Threading.Tasks.Task.FromResult<{shape}>(new {collection.Service}[] {{ {syncItems} }})";
		}

		string token = asynchronous ? "cancellationToken" : "default";
		string[] items = new string[resolvers.Length];
		for (int m = 0; m < resolvers.Length; m++)
		{
			bool rootOwned = IsRootOwned(instances[indices[m]]);
			items[m] = instances[indices[m]].IsAsyncTainted
				? $"await {AsyncResolveCall(names.AsyncResolver(indices[m]), rootOwned, token)}.ConfigureAwait(false)"
				: ResolveCall(resolvers[m], rootOwned);
		}

		string array = $"({shape})new {collection.Service}[] {{ {string.Join(", ", items)} }}";
		return $"((global::System.Func<global::System.Threading.Tasks.Task<{shape}>>)(async () => {array}))()";
	}

	/// <summary>
	///     A <c>Task&lt;IReadOnlyDictionary&lt;string, TService&gt;&gt;</c> expression producing the awaited keyed
	///     dictionary of every keyed registration of the service. The keyed analogue of the awaited collection
	///     expression: a completed <c>Task.FromResult</c> when every member is synchronous, otherwise an async
	///     lambda awaiting each async-tainted member. Awaited members receive the token only on the async path.
	/// </summary>
	private static string AwaitedKeyedCollectionExpression(string service, Names names, InstanceModel[] instances, bool asynchronous)
	{
		(string Key, string Resolver, bool RootOwned)[] members = names.KeyedCollectionResolvers(service);
		int[] indices = names.KeyedCollectionMemberIndices(service);
		string shape = $"global::System.Collections.Generic.IReadOnlyDictionary<string, {service}>";
		string dictionary = $"global::System.Collections.Generic.Dictionary<string, {service}>";

		bool anyAsync = false;
		for (int m = 0; m < indices.Length; m++)
		{
			anyAsync |= instances[indices[m]].IsAsyncTainted;
		}

		if (!anyAsync)
		{
			string syncItems = string.Join(", ", members.Select(member =>
				$"[{SymbolDisplay.FormatLiteral(member.Key, quote: true)}] = {ResolveCall(member.Resolver, member.RootOwned)}"));
			return $"global::System.Threading.Tasks.Task.FromResult<{shape}>(new {dictionary} {{ {syncItems} }})";
		}

		string token = asynchronous ? "cancellationToken" : "default";
		string[] items = new string[members.Length];
		for (int m = 0; m < members.Length; m++)
		{
			string value = instances[indices[m]].IsAsyncTainted
				? $"await {AsyncResolveCall(names.AsyncResolver(indices[m]), members[m].RootOwned, token)}.ConfigureAwait(false)"
				: ResolveCall(members[m].Resolver, members[m].RootOwned);
			items[m] = $"[{SymbolDisplay.FormatLiteral(members[m].Key, quote: true)}] = {value}";
		}

		string literal = $"({shape})new {dictionary} {{ {string.Join(", ", items)} }}";
		return $"((global::System.Func<global::System.Threading.Tasks.Task<{shape}>>)(async () => {literal}))()";
	}

	private static string EmitConstruction(InstanceModel instance, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool asynchronous = false)
	{
		ParameterModel[] parameters = instance.ConstructorParameters.AsArray();
		// The requesting type this instance presents to any requesting-type factory it consumes: its own
		// constructed type (the declaring type of the member being satisfied). The factory is never the consumer.
		string requestingType = RequestingTypeOf(instance);
		StringBuilder arguments = new();
		int argIndex = 0;
		for (int p = 0; p < parameters.Length; p++)
		{
			if (p > 0)
			{
				arguments.Append(", ");
			}

			// Runtime arguments are passed in as a0, a1, … and an async factory's CancellationToken from the
			// resolve-time token; everything else resolves from the graph.
			if (parameters[p].Kind == DependencyKind.Arg)
			{
				arguments.Append("a" + argIndex++);
			}
			else if (parameters[p].Kind == DependencyKind.CancellationToken)
			{
				// Forward the resolve-time token. Only an async factory yields a CancellationToken dependency, and it
				// is built solely on the async path, so this is never reached with asynchronous == false.
				arguments.Append("cancellationToken");
			}
			else if (parameters[p].Kind == DependencyKind.RequestingType)
			{
				// Forward the resolver's own __requestingType parameter (typeof(consumer) or null at top level). The
				// ! keeps the call warning-free whether the factory declares the parameter nullable or not.
				arguments.Append(RequestingTypeParameterName).Append('!');
			}
			else
			{
				arguments.Append(DependencyValue(parameters[p], instances, names, serviceToIndex, asynchronous, requestingType));
			}
		}

		if (instance.Production == ProductionKind.Factory)
		{
			// The container is a static class enclosing both Scope and Root, so its static factory method is in
			// scope by simple name. An async factory returns Task<T> / ValueTask<T>, awaited here (uniformly, no
			// TFM gating); it is only ever reached on the async path.
			if (instance.IsAsyncFactory)
			{
				return $"await {instance.ProductionMember}({arguments}).ConfigureAwait(false)";
			}

			return $"{instance.ProductionMember}({arguments})";
		}

		return $"new {instance.ConstructedType}({arguments}){MemberInitializer(instance, instances, names, serviceToIndex, asynchronous)}";
	}

	/// <summary>
	///     The value expression for a graph-resolved dependency (a constructor argument or an injected member). A
	///     collection materializes eagerly; on the async construction path an async-tainted direct dependency is
	///     awaited so the instance it injects is already initialized; everything else resolves through
	///     <see cref="ResolveExpression" />. Shared so a property resolves exactly like a constructor parameter.
	/// </summary>
	private static string DependencyValue(ParameterModel dependency, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool asynchronous, string requestingType = "null")
	{
		if (dependency.Kind == DependencyKind.Enumerable)
		{
			return CollectionLiteral(new ServiceKey(dependency.ServiceType, dependency.Key), names, instances);
		}

		if (dependency.Kind == DependencyKind.AsyncEnumerable)
		{
			return AsyncCollectionExpression(new ServiceKey(dependency.ServiceType, dependency.Key), names, instances, asynchronous);
		}

		if (dependency.Kind == DependencyKind.AwaitedEnumerable)
		{
			return AwaitedCollectionExpression(dependency, names, instances, asynchronous);
		}

		if (dependency.Kind == DependencyKind.KeyedCollection)
		{
			return KeyedCollectionLiteral(dependency.ServiceType, names);
		}

		if (dependency.Kind == DependencyKind.AwaitedKeyedCollection)
		{
			return AwaitedKeyedCollectionExpression(dependency.ServiceType, names, instances, asynchronous);
		}

		if (asynchronous && dependency.Kind == DependencyKind.Direct
		    && serviceToIndex.TryGetValue(new ServiceKey(dependency.ServiceType, dependency.Key), out int index)
		    && instances[index].IsAsyncTainted)
		{
			// A requesting-type factory's async resolver takes the consumer's typeof(…) (never root-owned); every
			// other async target resolves through AsyncResolveCall.
			if (instances[index].IsRequestingTypeFactory)
			{
				return $"await {names.AsyncResolver(index)}(__s, {requestingType}, cancellationToken).ConfigureAwait(false)";
			}

			return $"await {AsyncResolveCall(names.AsyncResolver(index), IsRootOwned(instances[index]), "cancellationToken")}.ConfigureAwait(false)";
		}

		return ResolveExpression(dependency, instances, names, serviceToIndex, requestingType);
	}

	/// <summary>
	///     The object initializer that fills the opt-in <c>[Inject]</c> members after construction:
	///     <c>{ Bus = ResolveBus(), … }</c>, appended to the <c>new …(…)</c> so the instance is never observed
	///     half-set. Assigns both <c>init</c> and <c>set</c> properties; on the async path an async-tainted member
	///     is awaited inside the initializer. Empty when the instance has no injected members.
	/// </summary>
	private static string MemberInitializer(InstanceModel instance, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool asynchronous)
	{
		MemberModel[] members = instance.InjectedMembers.AsArray();
		if (members.Length == 0)
		{
			return string.Empty;
		}

		// The requesting type this instance presents to a requesting-type factory an injected member consumes.
		string requestingType = RequestingTypeOf(instance);
		StringBuilder assignments = new();
		bool first = true;
		for (int m = 0; m < members.Length; m++)
		{
			// A deferred member ([Inject(Deferred = true)]) is assigned after construction and caching, not in the
			// object initializer, so it can break a mutual constructor cycle. It is emitted by EmitDeferredAssignments.
			if (members[m].Deferred)
			{
				continue;
			}

			if (!first)
			{
				assignments.Append(", ");
			}

			first = false;
			assignments.Append(members[m].MemberName).Append(" = ")
				.Append(DependencyValue(members[m].Dependency, instances, names, serviceToIndex, asynchronous, requestingType));
		}

		return first ? string.Empty : $" {{ {assignments} }}";
	}

	/// <summary>
	///     Emits the post-construction assignment of an instance's deferred members
	///     (<c>[Inject(Deferred = true)]</c>). Emitted after the instance is stored in its cache, so a re-entrant
	///     resolve of the other side of a mutual cycle returns the cached instance, letting a deferred property
	///     break a constructor cycle. Runs only on the construction path; async-tainted members are awaited.
	/// </summary>
	private static void EmitDeferredAssignments(StringBuilder builder, int depth, InstanceModel instance, string variable, EmitContext context, bool asynchronous)
	{
		string requestingType = RequestingTypeOf(instance);
		foreach (MemberModel member in instance.InjectedMembers.AsArray())
		{
			if (!member.Deferred)
			{
				continue;
			}

			string value = DependencyValue(member.Dependency, context.Instances, context.Names, context.ServiceToIndex, asynchronous, requestingType);
			Indent(builder, depth).Append(variable).Append('.').Append(member.MemberName).Append(" = ").Append(value).AppendLine(";");
		}
	}

	/// <summary>Whether an instance has any deferred ([Inject(Deferred = true)]) member to wire after construction.</summary>
	private static bool HasDeferredMembers(InstanceModel instance)
	{
		foreach (MemberModel member in instance.InjectedMembers.AsArray())
		{
			if (member.Deferred)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	///     A call to a target's synchronous static resolver over the owner <c>__s</c>. A root-owned target
	///     (singleton or pre-built Instance) resolves on the <c>Root</c> (<c>Root.ResolveX(__s.__root, …)</c>); a
	///     scoped/transient one on the <c>Scope</c> (<c>ResolveX(__s, …)</c>). <paramref name="trailingArgs" /> are
	///     any runtime <c>[Arg]</c> values following the owner; a root-owned target never takes them.
	/// </summary>
	private static string ResolveCall(string resolver, bool rootOwned, string trailingArgs = "")
		=> rootOwned
			? $"Root.{resolver}(__s.__root{trailingArgs})"
			: $"{resolver}(__s{trailingArgs})";

	/// <summary>
	///     A call to a target's asynchronous static resolver over <c>__s</c>. <paramref name="args" /> are the
	///     arguments after the owner (any runtime <c>[Arg]</c> values plus the cancellation token, e.g.
	///     <c>"cancellationToken"</c> or <c>"a0, __ct"</c>).
	/// </summary>
	private static string AsyncResolveCall(string asyncResolver, bool rootOwned, string args)
		=> rootOwned
			? $"Root.{asyncResolver}(__s.__root, {args})"
			: $"{asyncResolver}(__s, {args})";

	/// <summary>
	///     The expression that supplies a single constructor argument, resolving the target named by the
	///     parameter's service type and optional <c>[FromKey]</c>. A root-owned target resolves through its
	///     <c>Root</c>-hosted resolver; a scoped/transient one through its <c>Scope</c>-hosted resolver over
	///     <c>__s</c>. A relationship type wraps the target in a deferred <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c>.
	/// </summary>
	private static string ResolveExpression(ParameterModel parameter, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, string requestingType = "null")
	{
		// An external dependency is not in the Awaiten graph, so it is routed through the container's external
		// resolver, forwarding its [FromKey] key (null when unkeyed).
		if (parameter.Kind == DependencyKind.External)
		{
			return $"({parameter.ServiceType})__s.__ResolveExternal(typeof({parameter.ServiceType}), {ExternalKeyLiteral(parameter.Key)})";
		}

		int targetIndex = serviceToIndex[new ServiceKey(parameter.ServiceType, parameter.Key)];
		string resolver = names.Resolver(targetIndex);
		InstanceModel target = instances[targetIndex];

		string[] funcArgTypes = parameter.FuncArgTypes.AsArray();
		bool rootOwned = IsRootOwned(target);

		// A requesting-type factory's resolver takes the requesting type, so the consumer passes its own typeof(…)
		// directly. Placed first because it produces the supported shapes itself (see RequestingTypeExpression).
		if (target.IsRequestingTypeFactory)
		{
			return RequestingTypeExpression(parameter, target, resolver, names.AsyncResolver(targetIndex), requestingType);
		}

		// The async relationship types resolve through the target's async resolver (or wrap a synchronous target in
		// a completed Task), deferred at construction time with no ambient token. Their leak-free Owned<T> forms go
		// through __OwnedAsync<T>, so they are handled first, before the synchronous ProducesOwned path below.
		if (parameter.Kind is DependencyKind.Task or DependencyKind.FuncTask or DependencyKind.LazyTask)
		{
			return AsyncRelationshipExpression(parameter, target, targetIndex, names, rootOwned, funcArgTypes);
		}

		// Owned relationships build into a throwaway child scope, independent of this owner, so they need none
		// of the root-routing below: a Func<…, Owned<T>> factory, or a bare Owned<T> resolved once.
		if (parameter.ProducesOwned)
		{
			return OwnedFuncFactory(funcArgTypes, parameter.ServiceType, resolver, rootOwned);
		}

		if (parameter.Kind == DependencyKind.Owned)
		{
			return OwnedBare(parameter.ServiceType, resolver, rootOwned);
		}

		if (parameter.Kind == DependencyKind.Func && funcArgTypes.Length > 0)
		{
			// A Func<TArg…, T> over a parameterized service binds the owner's parameterized resolver, which
			// takes the runtime arguments and is never cached.
			return FuncFactory(funcArgTypes, parameter.ServiceType, resolver);
		}

		// A singleton/Instance resolves through its Root-hosted resolver over the root; a scoped/transient through
		// its Scope-hosted resolver over __s. Owner selection is static (no virtual hop), and a transient built for
		// a singleton tracks on the root because the singleton resolver runs with __s == root.
		string value = ResolveCall(resolver, rootOwned);

		return parameter.Kind switch
		{
			DependencyKind.Func => $"new global::System.Func<{parameter.ServiceType}>(() => {value})",
			DependencyKind.Lazy => $"new global::System.Lazy<{parameter.ServiceType}>(() => {value})",
			_ => value,
		};
	}

	/// <summary>
	///     The construction expression for a dependency produced by a requesting-type factory, extracted from
	///     <see cref="ResolveExpression" />. The consumer passes its own <c>typeof(…)</c> into the call. The
	///     factory is Scope-hosted and called per consumer, never cached. It produces the supported shapes itself:
	///     the plain dependency, its <c>Func</c>/<c>Lazy</c> wrappers, and, when async-tainted, the awaiting
	///     <c>Task</c> forms (a synchronous <c>Func</c>/<c>Lazy</c> over an async-tainted factory is AWT119). No
	///     <c>Owned&lt;T&gt;</c> form.
	/// </summary>
	private static string RequestingTypeExpression(ParameterModel parameter, InstanceModel target, string resolver, string asyncResolver, string requestingType)
	{
		if (parameter.Kind is DependencyKind.Task or DependencyKind.FuncTask or DependencyKind.LazyTask)
		{
			string task = $"global::System.Threading.Tasks.Task<{parameter.ServiceType}>";
			// An async-tainted factory produces its Task through the awaiting async resolver; a synchronous one
			// wraps its resolver's result in a completed task. By-type deferral carries no ambient token (default).
			string taskValue = target.IsAsyncTainted
				? $"{asyncResolver}(__s, {requestingType}, default)"
				: $"global::System.Threading.Tasks.Task.FromResult<{parameter.ServiceType}>({resolver}(__s, {requestingType}))";
			return parameter.Kind switch
			{
				DependencyKind.FuncTask => $"new global::System.Func<{task}>(() => {taskValue})",
				DependencyKind.LazyTask => $"new global::System.Lazy<{task}>(() => {taskValue})",
				_ => taskValue,
			};
		}

		string requestingCall = $"{resolver}(__s, {requestingType})";
		return parameter.Kind switch
		{
			DependencyKind.Func => $"new global::System.Func<{parameter.ServiceType}>(() => {requestingCall})",
			DependencyKind.Lazy => $"new global::System.Lazy<{parameter.ServiceType}>(() => {requestingCall})",
			_ => requestingCall,
		};
	}

	/// <summary>
	///     The construction expression for an async relationship (<c>Task&lt;T&gt;</c>,
	///     <c>Func&lt;…, Task&lt;T&gt;&gt;</c> or <c>Lazy&lt;Task&lt;T&gt;&gt;</c>). A leak-free <c>Owned&lt;T&gt;</c>
	///     form async-resolves into a throwaway child scope through <c>__OwnedAsync&lt;T&gt;</c>; otherwise the
	///     awaited value is wrapped in the factory / memoizing Lazy, or returned bare for a plain <c>Task&lt;T&gt;</c>.
	/// </summary>
	private static string AsyncRelationshipExpression(ParameterModel parameter, InstanceModel target, int targetIndex, Names names, bool rootOwned, string[] funcArgTypes)
	{
		if (parameter.ProducesOwned)
		{
			string ownedValue = $"__s.__OwnedAsync<{parameter.ServiceType}>({AsyncOwnedInner(parameter, target, targetIndex, names, rootOwned, funcArgTypes)}, default)";
			return parameter.Kind == DependencyKind.FuncTask
				? AsyncOwnedFuncFactory(funcArgTypes, parameter.ServiceType, ownedValue)
				: ownedValue;
		}

		string asyncValue = AsyncRelationshipValue(parameter, target, targetIndex, names, rootOwned, funcArgTypes);
		return parameter.Kind switch
		{
			DependencyKind.FuncTask => AsyncFuncFactory(funcArgTypes, parameter.ServiceType, asyncValue),
			DependencyKind.LazyTask => $"new global::System.Lazy<global::System.Threading.Tasks.Task<{parameter.ServiceType}>>(() => {asyncValue})",
			_ => asyncValue,
		};
	}

	/// <summary>
	///     A <c>new Func&lt;TArg…, T&gt;((a0, …) =&gt; resolver(a0, …))</c> expression that forwards the
	///     runtime arguments to a parameterized service's resolver. With no argument types this is the plain
	///     deferred <c>Func&lt;T&gt;</c>.
	/// </summary>
	private static string FuncFactory(string[] argTypes, string service, string resolver)
	{
		string generics = argTypes.Length == 0 ? service : string.Join(", ", argTypes) + ", " + service;
		string lambdaArgs = string.Join(", ", argTypes.Select((_, i) => "a" + i));
		// A Func over a parameterized service binds its Scope-hosted static resolver over the current owner __s,
		// forwarding the runtime arguments after it.
		string call = ResolveCall(resolver, rootOwned: false, argTypes.Length == 0 ? "" : ", " + lambdaArgs);
		return $"new global::System.Func<{generics}>(({lambdaArgs}) => {call})";
	}

	/// <summary>
	///     A <c>Task&lt;T&gt;</c>-valued expression for an async relationship. An async-tainted target is produced
	///     by its async resolver (forwarding runtime arguments for a parameterized target); a synchronous one is
	///     wrapped with <c>Task.FromResult</c>. Created at construction time with no ambient token. A root-owned
	///     target is read straight off <c>__root</c> so the call devirtualizes.
	/// </summary>
	private static string AsyncRelationshipValue(ParameterModel parameter, InstanceModel target, int targetIndex, Names names, bool rootOwned, string[] argTypes)
	{
		string callArgs = string.Join("", argTypes.Select((_, i) => "a" + i + ", "));
		if (target.IsAsyncTainted)
		{
			return AsyncResolveCall(names.AsyncResolver(targetIndex), rootOwned, callArgs + "default");
		}

		string syncArgs = argTypes.Length == 0 ? "" : ", " + string.Join(", ", argTypes.Select((_, i) => "a" + i));
		return $"global::System.Threading.Tasks.Task.FromResult<{parameter.ServiceType}>({ResolveCall(names.Resolver(targetIndex), rootOwned, syncArgs)})";
	}

	/// <summary>
	///     A <c>new Func&lt;TArg…, Task&lt;T&gt;&gt;((a0, …) =&gt; …)</c> expression: the async counterpart of
	///     <see cref="FuncFactory" />, wrapping the async relationship value in a factory that forwards any
	///     runtime arguments. With no argument types this is the plain async factory <c>Func&lt;Task&lt;T&gt;&gt;</c>.
	/// </summary>
	private static string AsyncFuncFactory(string[] argTypes, string service, string asyncValue)
	{
		string task = $"global::System.Threading.Tasks.Task<{service}>";
		string generics = argTypes.Length == 0 ? task : string.Join(", ", argTypes) + ", " + task;
		string lambdaArgs = string.Join(", ", argTypes.Select((_, i) => "a" + i));
		return $"new global::System.Func<{generics}>(({lambdaArgs}) => {asyncValue})";
	}

	/// <summary>
	///     The <c>Func&lt;Scope, CancellationToken, Task&lt;T&gt;&gt;</c> delegate passed to
	///     <c>__OwnedAsync&lt;T&gt;</c> that async-resolves the single value into the throwaway scope. The async
	///     counterpart of <see cref="OwnedInner" />: a root-owned target goes through the public
	///     <c>ResolveAsync(Type)</c> surface; any other calls its async resolver directly (or wraps a synchronous
	///     one in a completed task).
	/// </summary>
	private static string AsyncOwnedInner(ParameterModel parameter, InstanceModel target, int targetIndex, Names names, bool rootOwned, string[] argTypes)
	{
		if (rootOwned)
		{
			return $"async (__o, __ct) => ({parameter.ServiceType})await __o.ResolveAsync(typeof({parameter.ServiceType}), __ct).ConfigureAwait(false)";
		}

		if (target.IsAsyncTainted)
		{
			string callArgs = string.Join("", argTypes.Select((_, i) => "a" + i + ", "));
			return $"(__o, __ct) => {names.AsyncResolver(targetIndex)}(__o, {callArgs}__ct)";
		}

		string syncArgs = argTypes.Length == 0 ? "" : ", " + string.Join(", ", argTypes.Select((_, i) => "a" + i));
		return $"(__o, __ct) => global::System.Threading.Tasks.Task.FromResult<{parameter.ServiceType}>({names.Resolver(targetIndex)}(__o{syncArgs}))";
	}

	/// <summary>
	///     A <c>new Func&lt;TArg…, Task&lt;Owned&lt;T&gt;&gt;&gt;((a0, …) =&gt; __OwnedAsync&lt;T&gt;(…))</c>
	///     expression: the async leak-free factory, each call async-resolving <c>T</c> into a throwaway child scope
	///     and handing back the <c>Owned&lt;T&gt;</c> handle (async counterpart of <see cref="OwnedFuncFactory" />).
	/// </summary>
	private static string AsyncOwnedFuncFactory(string[] argTypes, string service, string ownedValue)
	{
		string task = $"global::System.Threading.Tasks.Task<global::Awaiten.Owned<{service}>>";
		string generics = argTypes.Length == 0 ? task : string.Join(", ", argTypes) + ", " + task;
		string lambdaArgs = string.Join(", ", argTypes.Select((_, i) => "a" + i));
		return $"new global::System.Func<{generics}>(({lambdaArgs}) => {ownedValue})";
	}

	// The [FromKey] key of an external dependency as a C# literal to forward to the resolver: the escaped
	// string literal, or "null" for an unkeyed dependency.
	private static string ExternalKeyLiteral(string? key)
		=> key is null ? "null" : SymbolDisplay.FormatLiteral(key, quote: true);

	/// <summary>
	///     Emits the <c>__Owned&lt;T&gt;</c> helper on the base <c>Scope</c>: it opens a throwaway child scope,
	///     resolves a single <c>T</c> into it through the supplied delegate and returns an <c>Owned&lt;T&gt;</c>
	///     over that scope. Disposing the handle disposes only that scope, while shared singletons live on.
	/// </summary>
	private static void EmitOwnedHelper(StringBuilder builder, int depth, bool hasExternal)
	{
		AppendXmlSummary(builder, depth,
			"Resolves <typeparamref name=\"T\" /> in its own child scope, owned by the returned <c>Owned&lt;T&gt;</c>.");
		Indent(builder, depth).AppendLine("protected global::Awaiten.Owned<T> __Owned<T>(global::System.Func<Scope, T> __resolve)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("Scope __owned = CreateScope();");
		EmitOwnedExternalPropagation(builder, depth + 1, hasExternal);
		Indent(builder, depth + 1).AppendLine("return new global::Awaiten.Owned<T>(__owned, __resolve(__owned));");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();

		// The async counterpart of __Owned<T>: await the single T into a throwaway child scope and hand back the
		// Owned<T>. Like __Owned<T> it does not roll back the scope if resolution throws.
		AppendXmlSummary(builder, depth,
			"The asynchronous counterpart of <c>__Owned&lt;T&gt;</c>.");
		Indent(builder, depth).AppendLine("protected async global::System.Threading.Tasks.Task<global::Awaiten.Owned<T>> __OwnedAsync<T>(global::System.Func<Scope, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<T>> __resolve, global::System.Threading.CancellationToken cancellationToken)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("Scope __owned = CreateScope();");
		EmitOwnedExternalPropagation(builder, depth + 1, hasExternal);
		Indent(builder, depth + 1).AppendLine("return new global::Awaiten.Owned<T>(__owned, await __resolve(__owned, cancellationToken).ConfigureAwait(false));");
		Indent(builder, depth).AppendLine("}");
	}

	// The throwaway Owned scope inherits the creating scope's external resolver, so an external dependency built
	// inside it resolves from the same provider. Emitted only when the container has external dependencies.
	private static void EmitOwnedExternalPropagation(StringBuilder builder, int depth, bool hasExternal)
	{
		if (hasExternal)
		{
			Indent(builder, depth).AppendLine("__owned.__externalResolver = __externalResolver;");
		}
	}

	/// <summary>
	///     A bare <c>Owned&lt;T&gt;</c>: resolve T once into a throwaway child scope and wrap it as a disposal handle.
	/// </summary>
	private static string OwnedBare(string service, string resolver, bool rootOwned)
		=> $"__s.__Owned<{service}>({OwnedInner(service, resolver, [], rootOwned)})";

	/// <summary>
	///     A <c>new Func&lt;TArg…, Owned&lt;T&gt;&gt;((a0, …) =&gt; __Owned&lt;T&gt;(…))</c> expression: each call
	///     builds <c>T</c> in a throwaway child scope and hands back the disposal handle.
	/// </summary>
	private static string OwnedFuncFactory(string[] argTypes, string service, string resolver, bool rootOwned)
	{
		string owned = $"global::Awaiten.Owned<{service}>";
		string generics = argTypes.Length == 0 ? owned : string.Join(", ", argTypes) + ", " + owned;
		string lambdaArgs = string.Join(", ", argTypes.Select((_, i) => "a" + i));
		return $"new global::System.Func<{generics}>(({lambdaArgs}) => __s.__Owned<{service}>({OwnedInner(service, resolver, argTypes, rootOwned)}))";
	}

	/// <summary>
	///     The delegate passed to <c>__Owned&lt;T&gt;</c> that resolves the single value into the throwaway scope.
	///     A root-owned target goes through the public typed surface; any other calls its resolver directly
	///     (internal, so reachable even when the type is withheld from by-type resolution under strict lifetime
	///     safety).
	/// </summary>
	private static string OwnedInner(string service, string resolver, string[] argTypes, bool rootOwned)
	{
		// __o is the throwaway Owned scope (named distinctly from the enclosing resolver's __s). A root-owned target
		// goes through the throwaway's public typed surface; any other calls its resolver over __o.
		if (rootOwned)
		{
			return $"__o => __o.Resolve<{service}>()";
		}

		string syncArgs = argTypes.Length == 0 ? "" : ", " + string.Join(", ", argTypes.Select((_, i) => "a" + i));
		return $"__o => {resolver}(__o{syncArgs})";
	}
}
