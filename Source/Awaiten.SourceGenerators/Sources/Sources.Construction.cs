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
	///     <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>, <c>T[]</c>). The member resolvers are called
	///     unqualified against the current owner, so a singleton member routes through its virtual delegator to
	///     the root and a scoped/transient member resolves on the scope evaluating the collection.
	/// </summary>
	private static string CollectionLiteral(ServiceKey collection, Names names)
	{
		string items = string.Join(", ", names.CollectionResolvers(collection).Select(resolver => resolver + "()"));
		return $"new {collection.Service}[] {{ {items} }}";
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
			items[m] = asynchronous && instances[indices[m]].IsAsyncTainted
				? $"await {names.AsyncResolver(indices[m])}(cancellationToken).ConfigureAwait(false)"
				: resolvers[m] + "()";
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
			string syncItems = string.Join(", ", resolvers.Select(resolver => resolver + "()"));
			return $"global::System.Threading.Tasks.Task.FromResult<{shape}>(new {collection.Service}[] {{ {syncItems} }})";
		}

		string token = asynchronous ? "cancellationToken" : "default";
		string[] items = new string[resolvers.Length];
		for (int m = 0; m < resolvers.Length; m++)
		{
			items[m] = instances[indices[m]].IsAsyncTainted
				? $"await {names.AsyncResolver(indices[m])}({token}).ConfigureAwait(false)"
				: resolvers[m] + "()";
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
			return CollectionLiteral(new ServiceKey(dependency.ServiceType, dependency.Key), names);
		}

		if (dependency.Kind == DependencyKind.AsyncEnumerable)
		{
			return AsyncCollectionExpression(new ServiceKey(dependency.ServiceType, dependency.Key), names, instances, asynchronous);
		}

		if (dependency.Kind == DependencyKind.AwaitedEnumerable)
		{
			return AwaitedCollectionExpression(dependency, names, instances, asynchronous);
		}

		if (asynchronous && dependency.Kind == DependencyKind.Direct
		    && serviceToIndex.TryGetValue(new ServiceKey(dependency.ServiceType, dependency.Key), out int index)
		    && instances[index].IsAsyncTainted)
		{
			return $"await {names.AsyncResolver(index)}(cancellationToken).ConfigureAwait(false)";
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
		for (int m = 0; m < members.Length; m++)
		{
			if (m > 0)
			{
				assignments.Append(", ");
			}

			assignments.Append(members[m].MemberName).Append(" = ")
				.Append(DependencyValue(members[m].Dependency, instances, names, serviceToIndex, asynchronous));
		}

		return $" {{ {assignments} }}";
	}

	/// <summary>
	///     The expression that supplies a single constructor argument, resolving the target named by the
	///     parameter's service type and (optional) <c>[FromKey]</c>. A root-owned target (a singleton or
	///     pre-built Instance) is read straight off the sealed root so the call devirtualizes; a
	///     scoped/transient target resolves through its own resolver - <c>internal</c> on the base
	///     <c>Scope</c>, so the Root reaches it directly when a singleton captures it through a relationship,
	///     and a throwaway <c>Owned&lt;T&gt;</c> scope can call it too. A relationship type wraps the target in
	///     a deferred <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c>.
	/// </summary>
	private static string ResolveExpression(ParameterModel parameter, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex)
	{
		// An external dependency is not in the Awaiten graph, so it has no resolver of its own (and no entry in
		// serviceToIndex); it is routed through the container's external resolver instead, forwarding its
		// [FromKey] key (null when unkeyed) so a keyed external service can be selected.
		if (parameter.Kind == DependencyKind.External)
		{
			return $"({parameter.ServiceType})__ResolveExternal(typeof({parameter.ServiceType}), {ExternalKeyLiteral(parameter.Key)})";
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

		string value;
		if (rootOwned)
		{
			// Read singletons straight off the (sealed) root scope so the dependency call devirtualizes,
			// rather than dispatching through this scope's virtual delegator. On the root, __root is itself.
			value = $"__root.{resolver}()";
		}
		else
		{
			// A root-owned owner can only capture a non-singleton through a relationship (a direct capture
			// would be a captive dependency). The target's scoped/transient resolver is internal on the base
			// Scope, so it is reachable by simple name from the Root too - call it directly (the target index
			// has already selected the resolver for the requested [FromKey], if any). Routing through the generic
			// Resolve<T>() instead would hit the by-type withholding under strict lifetime safety and throw when
			// a Lazy<DisposableTransient> held by a singleton is forced.
			value = $"{resolver}()";
		}

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
			string ownedValue = $"__OwnedAsync<{parameter.ServiceType}>({AsyncOwnedInner(parameter, target, targetIndex, names, rootOwned, funcArgTypes)}, default)";
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
		return $"new global::System.Func<{generics}>(({lambdaArgs}) => {resolver}({lambdaArgs}))";
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
			string asyncResolver = rootOwned ? $"__root.{names.AsyncResolver(targetIndex)}" : names.AsyncResolver(targetIndex);
			return $"{asyncResolver}({callArgs}default)";
		}

		string resolver = rootOwned ? $"__root.{names.Resolver(targetIndex)}" : names.Resolver(targetIndex);
		return $"global::System.Threading.Tasks.Task.FromResult<{parameter.ServiceType}>({resolver}({string.Join(", ", argTypes.Select((_, i) => "a" + i))}))";
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
			return $"async (__s, __ct) => ({parameter.ServiceType})await __s.ResolveAsync(typeof({parameter.ServiceType}), __ct).ConfigureAwait(false)";
		}

		if (target.IsAsyncTainted)
		{
			string callArgs = string.Join("", argTypes.Select((_, i) => "a" + i + ", "));
			return $"(__s, __ct) => __s.{names.AsyncResolver(targetIndex)}({callArgs}__ct)";
		}

		string syncArgs = string.Join(", ", argTypes.Select((_, i) => "a" + i));
		return $"(__s, __ct) => global::System.Threading.Tasks.Task.FromResult<{parameter.ServiceType}>(__s.{names.Resolver(targetIndex)}({syncArgs}))";
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
			Indent(builder, depth).AppendLine("__owned.ExternalResolver = ExternalResolver;");
		}
	}

	/// <summary>
	///     A bare <c>Owned&lt;T&gt;</c>: resolve T once into a throwaway child scope and wrap it as a disposal handle.
	/// </summary>
	private static string OwnedBare(string service, string resolver, bool rootOwned)
		=> $"__Owned<{service}>({OwnedInner(service, resolver, [], rootOwned)})";

	/// <summary>
	///     A <c>new Func&lt;TArg…, Owned&lt;T&gt;&gt;((a0, …) =&gt; __Owned&lt;T&gt;(…))</c> expression: each call
	///     builds <c>T</c> in a throwaway child scope and hands back the disposal handle.
	/// </summary>
	private static string OwnedFuncFactory(string[] argTypes, string service, string resolver, bool rootOwned)
	{
		string owned = $"global::Awaiten.Owned<{service}>";
		string generics = argTypes.Length == 0 ? owned : string.Join(", ", argTypes) + ", " + owned;
		string lambdaArgs = string.Join(", ", argTypes.Select((_, i) => "a" + i));
		return $"new global::System.Func<{generics}>(({lambdaArgs}) => __Owned<{service}>({OwnedInner(service, resolver, argTypes, rootOwned)}))";
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
		if (rootOwned)
		{
			return $"__s => __s.Resolve<{service}>()";
		}

		string lambdaArgs = string.Join(", ", argTypes.Select((_, i) => "a" + i));
		return $"__s => __s.{resolver}({lambdaArgs})";
	}
}
