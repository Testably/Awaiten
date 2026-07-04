using System.Text;
using Awaiten.SourceGenerators.Entities;
using Microsoft.CodeAnalysis.CSharp;

namespace Awaiten.SourceGenerators;

internal static partial class Sources
{
	// Whether the __AsyncArray<T> helper is used: some instance injects an IAsyncEnumerable<T>, or some unkeyed,
	// non-suppressed collection is offered by type as IAsyncEnumerable<T> (a synchronous dispatch entry when
	// sync-materializable, an async resolver otherwise) - both materialize through the helper. A collection whose
	// async shape is explicitly registered offers no synthesized view, so it does not use the helper.
	private static bool NeedsAsyncArrayHelper(InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex)
		=> instances.Any(instance => instance.ConstructorParameters.AsArray().Any(p => p.Kind == DependencyKind.AsyncEnumerable))
		   || names.Collections.Any(collection => collection.Key is null
		                                          && !SynthesisSuppressed(serviceToIndex, collection.Service)
		                                          && !AsyncShapeRegistered(serviceToIndex, collection.Service));

	/// <summary>
	///     Emits the <c>__AsyncArray&lt;T&gt;</c> helper: a minimal <c>IAsyncEnumerable&lt;T&gt;</c> /
	///     <c>IAsyncEnumerator&lt;T&gt;</c> over an eagerly-materialized array. The async collection resolves (and
	///     awaits) its members up front, then hands them back through this replay enumerator - so each iteration
	///     completes synchronously over already-initialized instances. Written by hand rather than as an <c>async</c>
	///     iterator so it needs only the async-stream interfaces and <c>ValueTask</c>, never the async-iterator state
	///     machine builder (which is absent on net48 / netstandard2.0 even with Microsoft.Bcl.AsyncInterfaces).
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
	///     (element type, key), materialized eagerly in registration order (each member keeping its own lifetime).
	///     An empty membership yields <c>new T[] { }</c>. An array satisfies every supported collection parameter
	///     shape (<c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>,
	///     <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>, <c>T[]</c>). Each member calls its static resolver
	///     over the current owner <c>__s</c>: a singleton member on the <c>Root</c> (<c>Root.ResolveX(__s.__root)</c>),
	///     a scoped/transient member on the <c>Scope</c> (<c>ResolveX(__s)</c>).
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
	///     registration of the service type, materialized eagerly and keyed by its <c>[Key]</c>, in registration
	///     order (each member keeping its own lifetime). A <c>Dictionary&lt;,&gt;</c> satisfies the requested
	///     <c>IReadOnlyDictionary&lt;string, TService&gt;</c>; an empty membership yields an empty dictionary.
	///     Each member calls its static resolver over the current owner <c>__s</c>, exactly like
	///     <see cref="CollectionLiteral" />: a singleton member on the <c>Root</c>
	///     (<c>Root.ResolveX(__s.__root)</c>), a scoped/transient member on the <c>Scope</c> (<c>ResolveX(__s)</c>).
	/// </summary>
	private static string KeyedCollectionLiteral(string service, Names names)
	{
		string items = string.Join(", ", names.KeyedCollectionResolvers(service)
			.Select(member => $"[{SymbolDisplay.FormatLiteral(member.Key, quote: true)}] = {ResolveCall(member.Resolver, member.RootOwned)}"));
		return $"new global::System.Collections.Generic.Dictionary<string, {service}> {{ {items} }}";
	}

	/// <summary>
	///     A <c>new __AsyncArray&lt;T&gt;(new T[] { … })</c> expression producing an
	///     <c>IAsyncEnumerable&lt;T&gt;</c> over every registration of the collection's (element type, key). The
	///     members are materialized eagerly in registration order into the backing array - each async-tainted member
	///     awaited through its async resolver when <paramref name="asynchronous" /> is set (the consumer is built on
	///     the async path), a synchronous member resolved directly - and the array is wrapped in the
	///     <c>__AsyncArray&lt;T&gt;</c> helper, whose enumerator replays the already-initialized members. An empty
	///     membership yields an empty stream.
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
	///     A <c>Task&lt;C&gt;</c> expression producing the awaited collection of every registration of the
	///     parameter's (element type, key), materialized eagerly in registration order. When every member is
	///     synchronous it is a completed <c>Task.FromResult&lt;C&gt;</c> over the synchronous array - no async
	///     machinery at all; otherwise it is an immediately-invoked async lambda that awaits each async-tainted
	///     member through its async resolver and resolves the rest synchronously, with the array cast to the
	///     requested collection shape <c>C</c> so the task's result type matches the parameter exactly. The
	///     resolve-time token is forwarded to the awaited members only on the async construction path; a
	///     synchronously built consumer has no ambient token, so its awaited members receive <c>default</c>.
	///     An empty membership yields a completed empty array.
	/// </summary>
	private static string AwaitedCollectionExpression(ParameterModel parameter, Names names, InstanceModel[] instances, bool asynchronous)
		=> AwaitedCollectionExpression(new ServiceKey(parameter.ServiceType, parameter.Key), parameter.AwaitedCollectionType!, names, instances, asynchronous);

	/// <summary>
	///     The <c>Task&lt;C&gt;</c> expression for the collection under <paramref name="collection" /> cast to the
	///     inner collection shape <paramref name="shape" /> - shared by the injection path (which supplies the
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

	private static string EmitConstruction(InstanceModel instance, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool asynchronous = false)
	{
		ParameterModel[] parameters = instance.ConstructorParameters.AsArray();
		StringBuilder arguments = new();
		int argIndex = 0;
		for (int p = 0; p < parameters.Length; p++)
		{
			if (p > 0)
			{
				arguments.Append(", ");
			}

			// Runtime arguments are passed in as a0, a1, … by the parameterized resolver, and an async factory's
			// CancellationToken is forwarded from the resolve-time token; everything else resolves from the graph
			// (a collection, a relationship, or - on the async path - an awaited async-tainted direct dependency).
			if (parameters[p].Kind == DependencyKind.Arg)
			{
				arguments.Append("a" + argIndex++);
			}
			else if (parameters[p].Kind == DependencyKind.CancellationToken)
			{
				// Forward the resolve-time token: the async creator's cancellationToken is in scope here. Only an
				// async factory yields a CancellationToken dependency, and an async factory is built solely on the
				// async path, so this is never reached with asynchronous == false.
				arguments.Append("cancellationToken");
			}
			else
			{
				arguments.Append(DependencyValue(parameters[p], instances, names, serviceToIndex, asynchronous));
			}
		}

		if (instance.Production == ProductionKind.Factory)
		{
			// The container is a static class nested-enclosing both Scope and Root, so its static factory
			// method is in scope by simple name - no receiver. An asynchronous factory returns Task<T> /
			// ValueTask<T>; it is awaited here so the construction expression yields the produced T. This is
			// only ever reached on the async path (asynchronous: true): an async factory is async-tainted, so
			// its synchronous resolver delegates to the async one rather than constructing directly. The await
			// expression is uniform for Task and ValueTask, so no TFM gating of the emitted code is needed.
			if (instance.IsAsyncFactory)
			{
				return $"await {instance.ProductionMember}({arguments}).ConfigureAwait(false)";
			}

			return $"{instance.ProductionMember}({arguments})";
		}

		return $"new {instance.ConstructedType}({arguments}){MemberInitializer(instance, instances, names, serviceToIndex, asynchronous)}";
	}

	/// <summary>
	///     The value expression for a graph-resolved dependency - a constructor argument or an injected member.
	///     A collection materializes eagerly from its members' resolvers (an <c>IAsyncEnumerable&lt;T&gt;</c>
	///     awaiting each on the async path, or a <c>Task&lt;C&gt;</c> that launders the members' taint behind the
	///     produced task); on the async construction path an async-tainted direct dependency is awaited so the
	///     instance it injects is already initialized (keeping initialization in dependency order); everything else
	///     resolves through <see cref="ResolveExpression" />. Shared by constructor arguments and the
	///     injected-member initializer so a property resolves exactly like a constructor parameter.
	/// </summary>
	private static string DependencyValue(ParameterModel dependency, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool asynchronous)
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

		if (asynchronous && dependency.Kind == DependencyKind.Direct
		    && serviceToIndex.TryGetValue(new ServiceKey(dependency.ServiceType, dependency.Key), out int index)
		    && instances[index].IsAsyncTainted)
		{
			return $"await {AsyncResolveCall(names.AsyncResolver(index), IsRootOwned(instances[index]), "cancellationToken")}.ConfigureAwait(false)";
		}

		return ResolveExpression(dependency, instances, names, serviceToIndex);
	}

	/// <summary>
	///     The object initializer that fills the opt-in <c>[Inject]</c> members after construction:
	///     <c>{ Bus = ResolveBus(), … }</c>, appended to the <c>new …(…)</c> so the properties are assigned at
	///     construction (the instance is never observed half-set). Object-initializer syntax assigns both
	///     <c>init</c> and <c>set</c> properties; on the async path an async-tainted member is awaited inside the
	///     initializer exactly like an async-tainted constructor argument. Empty (no initializer) when the
	///     instance has no injected members.
	/// </summary>
	private static string MemberInitializer(InstanceModel instance, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool asynchronous)
	{
		MemberModel[] members = instance.InjectedMembers.AsArray();
		if (members.Length == 0)
		{
			return string.Empty;
		}

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
				.Append(DependencyValue(members[m].Dependency, instances, names, serviceToIndex, asynchronous));
		}

		return first ? string.Empty : $" {{ {assignments} }}";
	}

	/// <summary>
	///     Emits the post-construction assignment of an instance's deferred members
	///     (<c>[Inject(Deferred = true)]</c>): <c>variable.Invoice = ResolveInvoiceService();</c>. Emitted
	///     <em>after</em> the instance is stored in its cache, so a re-entrant resolve of the same service (the
	///     other side of a mutual cycle) returns the already-cached instance instead of recursing - this is what
	///     lets a deferred property break a constructor cycle. It runs only on the construction path (inside the
	///     cache-miss block), so a cache hit never reassigns. On the async path a deferred async-tainted member is
	///     awaited exactly like an async-tainted constructor argument.
	/// </summary>
	private static void EmitDeferredAssignments(StringBuilder builder, int depth, InstanceModel instance, string variable, EmitContext context, bool asynchronous)
	{
		foreach (MemberModel member in instance.InjectedMembers.AsArray())
		{
			if (!member.Deferred)
			{
				continue;
			}

			string value = DependencyValue(member.Dependency, context.Instances, context.Names, context.ServiceToIndex, asynchronous);
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
	///     A call to a target's synchronous static resolver over the owner variable <c>__s</c> in scope at the
	///     emission site. A root-owned target (singleton or pre-built Instance) is resolved on the <c>Root</c>
	///     (<c>Root.ResolveX(__s.__root, …)</c>); a scoped/transient one on the <c>Scope</c>
	///     (<c>ResolveX(__s, …)</c>). <paramref name="trailingArgs" /> are any runtime <c>[Arg]</c> values
	///     following the owner (comma-prefixed, e.g. <c>", a0, a1"</c>); a root-owned target never takes them.
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
	///     parameter's service type and (optional) <c>[FromKey]</c>. A root-owned target (a singleton or
	///     pre-built Instance) resolves through its <c>Root</c>-hosted static resolver over the root; a
	///     scoped/transient target through its <c>Scope</c>-hosted static resolver over the current owner
	///     <c>__s</c>. A relationship type wraps the target in a deferred <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c>.
	/// </summary>
	private static string ResolveExpression(ParameterModel parameter, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex)
	{
		// An external dependency is not in the Awaiten graph, so it has no resolver of its own (and no entry in
		// serviceToIndex); it is routed through the container's external resolver instead, forwarding its
		// [FromKey] key (null when unkeyed) so a keyed external service can be selected.
		if (parameter.Kind == DependencyKind.External)
		{
			return $"({parameter.ServiceType})__s.__ResolveExternal(typeof({parameter.ServiceType}), {ExternalKeyLiteral(parameter.Key)})";
		}

		int targetIndex = serviceToIndex[new ServiceKey(parameter.ServiceType, parameter.Key)];
		string resolver = names.Resolver(targetIndex);
		InstanceModel target = instances[targetIndex];

		string[] funcArgTypes = parameter.FuncArgTypes.AsArray();
		bool rootOwned = IsRootOwned(target);

		// The async relationship types resolve their target through its async resolver (awaiting
		// initialization), or wrap a synchronous target in a completed Task. They defer like the synchronous
		// relationships, so they are created here at construction time and carry no ambient cancellation token.
		// Their leak-free Owned<T> forms (Task<Owned<T>> / Func<…, Task<Owned<T>>>) async-resolve into a
		// throwaway child scope through the async __OwnedAsync<T> helper - the counterpart of the synchronous
		// ProducesOwned path below - so they are handled first, before that synchronous path.
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

		// A singleton/Instance is resolved through its Root-hosted static resolver over the root (__s.__root; on
		// the root, __root is itself); a scoped/transient through its Scope-hosted static resolver over the current
		// owner __s. Owner selection is thus static (no virtual hop), and a transient built for a singleton tracks
		// on the root because the singleton resolver runs with __s == root.
		string value = ResolveCall(resolver, rootOwned);

		return parameter.Kind switch
		{
			DependencyKind.Func => $"new global::System.Func<{parameter.ServiceType}>(() => {value})",
			DependencyKind.Lazy => $"new global::System.Lazy<{parameter.ServiceType}>(() => {value})",
			_ => value,
		};
	}

	/// <summary>
	///     The construction expression for an async relationship (<c>Task&lt;T&gt;</c>,
	///     <c>Func&lt;…, Task&lt;T&gt;&gt;</c> or <c>Lazy&lt;Task&lt;T&gt;&gt;</c>), extracted from
	///     <see cref="ResolveExpression" />. A leak-free <c>Owned&lt;T&gt;</c> form (<c>ProducesOwned</c>)
	///     async-resolves into a throwaway child scope through <c>__OwnedAsync&lt;T&gt;</c>, wrapping it in a factory for the
	///     <c>Func</c> form; otherwise the awaited value is wrapped in the factory / memoizing Lazy as the kind
	///     requires, or returned bare for a plain <c>Task&lt;T&gt;</c>.
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
	///     A <c>Task&lt;T&gt;</c>-valued expression for an async relationship. An async-tainted target is
	///     produced by its async resolver - which awaits initialization, and for a parameterized target forwards
	///     the runtime arguments (<c>a0…</c>) - so the relationship hands back an initialized instance. A
	///     synchronously-resolvable target is wrapped with <c>Task.FromResult</c> over its synchronous resolver.
	///     The relationship is created at construction time and carries no ambient cancellation token
	///     (<c>default</c>). A root-owned target is read straight off <c>__root</c> so the call devirtualizes.
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
	///     <c>__OwnedAsync&lt;T&gt;</c> that async-resolves the single value into the throwaway scope <c>__s</c> -
	///     the async counterpart of <see cref="OwnedInner" />. A root-owned target (a singleton or pre-built
	///     instance) goes through the public <c>ResolveAsync(Type)</c> surface (never withheld off a child scope,
	///     and shared off the root) with a cast; any other target calls its async resolver directly - internal on
	///     the base <c>Scope</c>, so reachable even when the type is withheld - or, for a synchronously-resolvable
	///     target, wraps its synchronous resolver in a completed task. A parameterized target forwards the runtime
	///     arguments (<c>a0…</c>) captured from the enclosing factory lambda.
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
	///     expression: the async leak-free factory, each call async-resolving <c>T</c> into a throwaway child
	///     scope and handing back the <c>Owned&lt;T&gt;</c> disposal handle (the async counterpart of
	///     <see cref="OwnedFuncFactory" />). With no argument types this is the plain <c>Func&lt;Task&lt;Owned&lt;T&gt;&gt;&gt;</c>.
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
	///     Emits the <c>__Owned&lt;T&gt;</c> helper on the base <c>Scope</c>: it opens a throwaway child scope
	///     (sharing the root's singletons), resolves a single <c>T</c> into it through the supplied delegate and
	///     returns an <c>Owned&lt;T&gt;</c> over that scope. Disposing the handle disposes only that scope,
	///     draining what was built for this one resolution while shared singletons live on.
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

		// The async counterpart of __Owned<T>: open a throwaway child scope, await the resolution (and any
		// initialization) of a single T into it, and hand back the Owned<T> over that scope. Disposing the handle
		// disposes only that scope. Like __Owned<T> it does not roll back the scope if resolution throws (the
		// synchronous helper does not either); CreateScopeAsync is the warming entry that does.
		AppendXmlSummary(builder, depth,
			"The asynchronous counterpart of <c>__Owned&lt;T&gt;</c>.");
		Indent(builder, depth).AppendLine("protected async global::System.Threading.Tasks.Task<global::Awaiten.Owned<T>> __OwnedAsync<T>(global::System.Func<Scope, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<T>> __resolve, global::System.Threading.CancellationToken cancellationToken)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("Scope __owned = CreateScope();");
		EmitOwnedExternalPropagation(builder, depth + 1, hasExternal);
		Indent(builder, depth + 1).AppendLine("return new global::Awaiten.Owned<T>(__owned, await __resolve(__owned, cancellationToken).ConfigureAwait(false));");
		Indent(builder, depth).AppendLine("}");
	}

	// The throwaway Owned scope belongs to the resolution that opened it, so it inherits the creating scope's
	// external resolver: an external dependency built inside it resolves from the same provider the creating
	// scope is aligned to (a null resolver propagates as null, keeping the fallback to the root's). Emitted only
	// when the container has external dependencies, so other containers' Owned helpers stay unchanged.
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
	///     The delegate passed to <c>__Owned&lt;T&gt;</c> that resolves the single value into the throwaway
	///     scope <c>__s</c>. A root-owned target (a singleton or pre-built instance) goes through the public
	///     typed surface (it is never withheld, and shares off the root); any other target calls its resolver
	///     directly - which is internal so it stays reachable even when the type is withheld from by-type
	///     resolution under strict lifetime safety.
	/// </summary>
	private static string OwnedInner(string service, string resolver, string[] argTypes, bool rootOwned)
	{
		// __o is the throwaway Owned scope (named distinctly from the enclosing resolver's __s). A root-owned
		// target goes through the throwaway's public typed surface; any other calls its Scope-hosted static
		// resolver over __o.
		if (rootOwned)
		{
			return $"__o => __o.Resolve<{service}>()";
		}

		string syncArgs = argTypes.Length == 0 ? "" : ", " + string.Join(", ", argTypes.Select((_, i) => "a" + i));
		return $"__o => {resolver}(__o{syncArgs})";
	}
}
