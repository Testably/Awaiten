using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     The built-instance indices of every collection-resolvable service's members for the
	///     transitive-disposable walk. A member absent from <paramref name="implToIndex" /> failed to build and is skipped.
	/// </summary>
	internal static CollectionMembership MembershipIndices(
		IReadOnlyList<ServiceMembers> collections,
		IReadOnlyList<KeyedServiceMembers> keyedCollections,
		Dictionary<string, int> implToIndex)
		=> new(CollectionMemberIndices(collections, implToIndex), KeyedCollectionMemberIndices(keyedCollections, implToIndex));

	/// <summary>The plain collections' member indices, keyed by the collection's (element service type, key).</summary>
	private static Dictionary<ServiceKey, List<int>> CollectionMemberIndices(
		IReadOnlyList<ServiceMembers> collections,
		Dictionary<string, int> implToIndex)
	{
		Dictionary<ServiceKey, List<int>> members = new();
		foreach (ServiceMembers collection in collections)
		{
			List<int> indices = new();
			foreach (string implementation in collection.Implementations.AsArray())
			{
				if (implToIndex.TryGetValue(implementation, out int index))
				{
					indices.Add(index);
				}
			}

			members[new ServiceKey(collection.Service, collection.Key)] = indices;
		}

		return members;
	}

	/// <summary>The keyed collections' member indices, keyed by the service (value) type.</summary>
	private static Dictionary<string, List<int>> KeyedCollectionMemberIndices(
		IReadOnlyList<KeyedServiceMembers> keyedCollections,
		Dictionary<string, int> implToIndex)
	{
		Dictionary<string, List<int>> members = new(StringComparer.Ordinal);
		foreach (KeyedServiceMembers keyed in keyedCollections)
		{
			List<int> indices = new();
			foreach (KeyedMember member in keyed.Members.AsArray())
			{
				if (implToIndex.TryGetValue(member.Implementation, out int index))
				{
					indices.Add(index);
				}
			}

			members[keyed.Service] = indices;
		}

		return members;
	}

	/// <summary>
	///     Whether building the service at <paramref name="start" /> on its owner tracks a fresh disposable
	///     there: the service itself is disposable, or its construction transitively rebuilds one. The walk
	///     follows only transient edges (a scoped/singleton dependency is cached, so bounded) and collection
	///     (Enumerable / keyed) edges, which materialize members eagerly. Used to decide whether a plain
	///     <c>Func&lt;…&gt;</c> over the service accumulates on the container root (AWT118 / strict withholding).
	/// </summary>
	internal static bool BuildsFreshDisposable(
		IReadOnlyList<InstanceModel> instances,
		Dictionary<ServiceKey, int> serviceToIndex,
		CollectionMembership membership,
		int start)
	{
		HashSet<int> visited = new();
		Stack<int> stack = new();
		stack.Push(start);

		while (stack.Count > 0)
		{
			int node = stack.Pop();
			if (!visited.Add(node))
			{
				continue;
			}

			InstanceModel instance = instances[node];
			if (instance.NeedsDisposal)
			{
				return true;
			}

			PushFreshTransientDependencies(instance, instances, serviceToIndex, membership, stack);
		}

		return false;
	}

	/// <summary>
	///     Pushes the dependencies of <paramref name="instance" /> rebuilt as part of constructing it: a direct
	///     transient dependency and each transient collection member (materialized eagerly). A relationship/Owned/Arg
	///     parameter defers and a scoped/singleton dependency is cached, so neither is followed.
	/// </summary>
	private static void PushFreshTransientDependencies(
		InstanceModel instance,
		IReadOnlyList<InstanceModel> instances,
		Dictionary<ServiceKey, int> serviceToIndex,
		CollectionMembership membership,
		Stack<int> stack)
	{
		foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
		{
			if (parameter.Kind is DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable)
			{
				PushTransientCollectionMembers(KeyOf(parameter), instances, membership.Collections, stack);
			}
			else if (parameter.Kind is DependencyKind.KeyedCollection or DependencyKind.AwaitedKeyedCollection)
			{
				// A keyed dictionary materializes its members eagerly too, so a transient disposable keyed member
				// is rebuilt like a plain collection member (the awaited Task<…> form as well).
				PushTransientKeyedMembers(parameter.ServiceType, instances, membership.Keyed, stack);
			}
			else if (parameter.Kind == DependencyKind.Direct
			         && serviceToIndex.TryGetValue(KeyOf(parameter), out int dependency)
			         && instances[dependency].Lifetime == Lifetime.Transient)
			{
				stack.Push(dependency);
			}
		}
	}

	/// <summary>
	///     Pushes each transient member of the collection reached under <paramref name="collectionKey" /> (its
	///     element service type plus resolution key; a non-transient member is cached/shared, so it is bounded).
	/// </summary>
	private static void PushTransientCollectionMembers(
		ServiceKey collectionKey,
		IReadOnlyList<InstanceModel> instances,
		IReadOnlyDictionary<ServiceKey, List<int>> collectionMembers,
		Stack<int> stack)
	{
		if (!collectionMembers.TryGetValue(collectionKey, out List<int>? members))
		{
			return;
		}

		foreach (int member in members)
		{
			if (instances[member].Lifetime == Lifetime.Transient)
			{
				stack.Push(member);
			}
		}
	}

	/// <summary>
	///     Pushes each transient member of the keyed collection for <paramref name="serviceType" /> (a non-transient
	///     member is cached, so bounded). The keyed analogue of <c>PushTransientCollectionMembers</c>.
	/// </summary>
	private static void PushTransientKeyedMembers(
		string serviceType,
		IReadOnlyList<InstanceModel> instances,
		IReadOnlyDictionary<string, List<int>> keyedCollectionMembers,
		Stack<int> stack)
	{
		if (!keyedCollectionMembers.TryGetValue(serviceType, out List<int>? members))
		{
			return;
		}

		foreach (int member in members)
		{
			if (instances[member].Lifetime == Lifetime.Transient)
			{
				stack.Push(member);
			}
		}
	}

	/// <summary>
	///     The direct-dependency graph over instance indices, for captive analysis (AWT105), async-taint propagation
	///     and the sync-async checks (AWT119/AWT120). Relationship types and bare eager <c>Owned&lt;T&gt;</c>/<c>Task&lt;T&gt;</c>
	///     defer or launder, so only direct (and eager collection) dependencies edge here. A deferred
	///     <c>[Inject(Deferred = true)]</c> member is included (it captures and awaits its target); only cycle
	///     detection excludes it, so it can break a mutual constructor cycle (AWT102).
	/// </summary>
	private static Dictionary<int, List<int>> BuildDependencyGraph(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, List<KeyedMember>> keyedMembers)
		=> BuildEdges(instances, serviceToImpl, implToIndex, serviceMembers, keyedMembers, includeEagerBare: false, includeDeferredMembers: true);

	/// <summary>
	///     The construction graph over instance indices, for cycle detection (AWT102): the direct edges plus the bare
	///     eager <c>Owned&lt;T&gt;</c> and <c>Task&lt;T&gt;</c>, which resolve their target during the owner's
	///     construction (a cycle through one re-enters an as-yet-uncached resolver and overflows at runtime, so
	///     AWT102). Deferred <c>[Inject(Deferred = true)]</c> members are excluded here so they can break a cycle,
	///     while <c>BuildDependencyGraph</c> includes them for captive/taint.
	/// </summary>
	private static Dictionary<int, List<int>> BuildConstructionGraph(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, List<KeyedMember>> keyedMembers)
		=> BuildEdges(instances, serviceToImpl, implToIndex, serviceMembers, keyedMembers, includeEagerBare: true);

	/// <summary>
	///     The combined construction-plus-deferred graph over instance indices, for the deferred-cycle analysis
	///     (AWT145/AWT146/AWT147): the construction edges plus a deferred <c>[Inject(Deferred = true)]</c> member's
	///     edges. Deferred members are excluded from the cycle (AWT102) and captive (AWT105) graphs, so cycles
	///     involving them are vetted over this union instead. Uses construction-graph semantics (<c>includeEagerBare</c>).
	/// </summary>
	private static Dictionary<int, List<int>> BuildCombinedGraph(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, List<KeyedMember>> keyedMembers)
		=> BuildEdges(instances, serviceToImpl, implToIndex, serviceMembers, keyedMembers, includeEagerBare: true, includeDeferredMembers: true);

	/// <summary>
	///     Whether any built instance has a deferred (<c>[Inject(Deferred = true)]</c>) member: the gate for
	///     building and walking the combined construction-plus-deferred graph at all.
	/// </summary>
	internal static bool AnyDeferredMember(List<InstanceModel> instances)
	{
		foreach (InstanceModel instance in instances)
		{
			foreach (MemberModel member in instance.InjectedMembers.AsArray())
			{
				if (member.Deferred)
				{
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>
	///     Builds the edge set over instance indices, keeping only parameters that contribute an edge to a built
	///     instance. A direct dependency always contributes; the bare eager <c>Owned&lt;T&gt;</c>/<c>Task&lt;T&gt;</c>
	///     only when <c>includeEagerBare</c> (the construction graph); a collection edges to each member in both
	///     graphs. A deferred <c>[Inject(Deferred = true)]</c> member contributes only when
	///     <c>includeDeferredMembers</c> (the taint graph): excluded from cycle/captive analysis, but its taint must
	///     still reach the owner or a sync owner would emit a sync resolve of an async-only service.
	/// </summary>
	private static Dictionary<int, List<int>> BuildEdges(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, List<KeyedMember>> keyedMembers,
		bool includeEagerBare,
		bool includeDeferredMembers = false)
	{
		Dictionary<int, List<int>> edges = new();
		for (int i = 0; i < instances.Count; i++)
		{
			List<int> nodeEdges = new();
			foreach (ParameterModel parameter in instances[i].ConstructorParameters.AsArray())
			{
				AddParameterEdges(parameter, serviceToImpl, implToIndex, serviceMembers, keyedMembers, includeEagerBare, nodeEdges);
			}

			// An injected [Inject] member is a full graph edge like a constructor parameter; AddParameterEdges
			// classifies it identically.
			foreach (MemberModel member in instances[i].InjectedMembers.AsArray())
			{
				if (member.Deferred && !includeDeferredMembers)
				{
					continue;
				}

				AddParameterEdges(member.Dependency, serviceToImpl, implToIndex, serviceMembers, keyedMembers, includeEagerBare, nodeEdges);
			}

			edges[i] = nodeEdges;
		}

		return edges;
	}

	/// <summary>
	///     Appends the edge(s) a single parameter contributes. A collection (Enumerable or AsyncEnumerable) edges to
	///     each member; a direct dependency (and, in the construction graph, a bare eager <c>Owned&lt;T&gt;</c>/<c>Task&lt;T&gt;</c>)
	///     edges to its single instance; everything else defers. An awaited collection (AwaitedEnumerable) launders
	///     its members' taint but its task materializes them during construction, so its member edges close cycles
	///     (construction graph only).
	/// </summary>
	private static void AddParameterEdges(
		ParameterModel parameter,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, List<KeyedMember>> keyedMembers,
		bool includeEagerBare,
		List<int> nodeEdges)
	{
		// A keyed collection materializes its members eagerly, so like a synchronous collection it captures them
		// and closes cycles in both graphs. The awaited keyed dictionary launders their taint, so its member edges
		// appear only in the construction graph (like AwaitedEnumerable below).
		if (parameter.Kind == DependencyKind.KeyedCollection
		    || (includeEagerBare && parameter.Kind == DependencyKind.AwaitedKeyedCollection))
		{
			AddKeyedCollectionMemberEdges(parameter.ServiceType, keyedMembers, implToIndex, nodeEdges);
			return;
		}

		if (parameter.Kind is DependencyKind.Enumerable or DependencyKind.AsyncEnumerable
		    || (includeEagerBare && parameter.Kind == DependencyKind.AwaitedEnumerable))
		{
			AddCollectionMemberEdges(KeyOf(parameter), serviceMembers, implToIndex, nodeEdges);
			return;
		}

		bool contributes = parameter.Kind == DependencyKind.Direct
		                   || (includeEagerBare && parameter.Kind is DependencyKind.Owned or DependencyKind.Task);
		if (contributes
		    && serviceToImpl.TryGetValue(KeyOf(parameter), out string? depImpl)
		    && implToIndex.TryGetValue(depImpl, out int depIndex))
		{
			nodeEdges.Add(depIndex);
		}
	}

	/// <summary>
	///     Appends an edge to each built member of the collection reached under <paramref name="collectionKey" />
	///     (its element service type plus resolution key; a member absent from <c>implToIndex</c> failed to build and
	///     is skipped).
	/// </summary>
	private static void AddCollectionMemberEdges(
		ServiceKey collectionKey,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, int> implToIndex,
		List<int> nodeEdges)
	{
		if (!serviceMembers.TryGetValue(collectionKey, out List<string>? members))
		{
			return;
		}

		foreach (string member in members)
		{
			if (implToIndex.TryGetValue(member, out int memberIndex))
			{
				nodeEdges.Add(memberIndex);
			}
		}
	}

	/// <summary>
	///     Appends an edge to each built keyed member of the keyed collection for <paramref name="serviceType" />.
	///     Mirrors <c>AddCollectionMemberEdges</c>, but grouped by service (value) type across every key of the service.
	/// </summary>
	private static void AddKeyedCollectionMemberEdges(
		string serviceType,
		Dictionary<string, List<KeyedMember>> keyedMembers,
		Dictionary<string, int> implToIndex,
		List<int> nodeEdges)
	{
		if (!keyedMembers.TryGetValue(serviceType, out List<KeyedMember>? members))
		{
			return;
		}

		foreach (KeyedMember member in members)
		{
			if (implToIndex.TryGetValue(member.Implementation, out int memberIndex))
			{
				nodeEdges.Add(memberIndex);
			}
		}
	}

	/// <summary>
	///     Marks every instance that is an async-taint source (async-initialized, or produced by an asynchronous
	///     factory the container reaches only by awaiting) or reaches one through non-deferred (Direct) edges, by
	///     fixpoint over the dependency graph.
	/// </summary>
	private static bool[] PropagateAsyncTaint(List<InstanceModel> instances, Dictionary<int, List<int>> dependencies)
	{
		bool[] tainted = new bool[instances.Count];
		for (int i = 0; i < instances.Count; i++)
		{
			tainted[i] = instances[i].IsAsyncInitializable || instances[i].IsAsyncFactory;
		}

		bool changed = true;
		while (changed)
		{
			changed = false;
			for (int i = 0; i < instances.Count; i++)
			{
				if (tainted[i])
				{
					continue;
				}

				foreach (int dependency in dependencies[i])
				{
					if (tainted[dependency])
					{
						tainted[i] = true;
						changed = true;
						break;
					}
				}
			}
		}

		return tainted;
	}

	/// <summary>
	///     AWT119 / AWT120 (strict mode): a synchronous <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c> /
	///     <c>Owned&lt;T&gt;</c> relationship resolves its target without awaiting initialization, so it must not
	///     target an async-tainted service. AWT119 fires when the target is itself async-initialized; AWT120 when
	///     it only reaches one transitively, reporting the path. <c>Owned</c> is included (an async-tainted service
	///     emits no synchronous resolver for its handle). An injected <c>[Inject]</c> member is checked like a
	///     constructor parameter.
	/// </summary>
	private static void DetectSynchronousAsyncResolution(
		List<InstanceModel> instances,
		Dictionary<int, List<int>> dependencies,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		for (int i = 0; i < instances.Count; i++)
		{
			foreach (ParameterModel parameter in instances[i].ConstructorParameters.AsArray())
			{
				CheckDependency(i, parameter);
			}

			foreach (MemberModel member in instances[i].InjectedMembers.AsArray())
			{
				CheckDependency(i, member.Dependency);
			}
		}

		void CheckDependency(int consumer, ParameterModel parameter)
		{
			// Guard the implToIndex lookup: serviceToImpl can name an implementation whose BuildInstance failed,
			// and an unguarded indexer would crash the generator instead of surfacing the real registration error.
			if (parameter.Kind is not (DependencyKind.Func or DependencyKind.Lazy or DependencyKind.Owned)
			    || !serviceToImpl.TryGetValue(KeyOf(parameter), out string? targetImpl)
			    || !implToIndex.TryGetValue(targetImpl, out int target))
			{
				return;
			}

			if (!instances[target].IsAsyncTainted)
			{
				return;
			}

			// Point the diagnostic at the offending parameter; fall back to the consumer's registration.
			LocationInfo? location = parameter.Location ?? instanceLocations[consumer];
			if (instances[target].IsAsyncSource)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.SynchronousAsyncResolution,
					location,
					new EquatableArray<string>([
						DisplayInstance(instances[consumer].ImplementationType),
						parameter.Kind.ToString(),
						DisplayInstance(instances[target].ImplementationType),
					])));
			}
			else
			{
				string path = AsyncTaintPath(instances, dependencies, target);
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.AsyncDependencyOnSyncPath,
					location,
					new EquatableArray<string>([
						DisplayInstance(instances[consumer].ImplementationType),
						path,
					])));
			}
		}
	}

	/// <summary>
	///     AWT186: an <c>Owned&lt;T&gt;</c>-family relationship (a bare <c>Owned&lt;T&gt;</c>, or a
	///     <c>Func</c>/<c>Task</c> form that produces one) targets a service produced by a requesting-type factory.
	///     Such a factory is called per consumer and has no owner scope to build the owned target into, so the
	///     combination is unsupported; the emit path has no owned form for it and would otherwise produce an
	///     <c>Owned&lt;T&gt;</c>-typed slot filled with a bare <c>T</c> (a raw CS1503). Reported for both constructor
	///     parameters and injected <c>[Inject]</c> members, independent of lifetime safety (it is a structural
	///     incompatibility, not an async concern). Mirrors AWT163's rejection of <c>[Arg]</c>-plus-<c>[RequestingType]</c>.
	/// </summary>
	private static void DetectOwnedOverRequestingTypeFactory(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		for (int i = 0; i < instances.Count; i++)
		{
			foreach (ParameterModel parameter in instances[i].ConstructorParameters.AsArray())
			{
				CheckDependency(i, parameter);
			}

			foreach (MemberModel member in instances[i].InjectedMembers.AsArray())
			{
				CheckDependency(i, member.Dependency);
			}
		}

		void CheckDependency(int consumer, ParameterModel parameter)
		{
			// Every owned form: the bare Owned<T> (DependencyKind.Owned) and the Func/Task forms that wrap the
			// produced value in an Owned<T> (ProducesOwned). Guard the implToIndex lookup: serviceToImpl can name an
			// implementation whose BuildInstance failed, and an unguarded indexer would crash the generator instead
			// of surfacing the real registration error.
			if ((parameter.Kind != DependencyKind.Owned && !parameter.ProducesOwned)
			    || !serviceToImpl.TryGetValue(KeyOf(parameter), out string? targetImpl)
			    || !implToIndex.TryGetValue(targetImpl, out int target)
			    || !instances[target].IsRequestingTypeFactory)
			{
				return;
			}

			// Point the diagnostic at the offending parameter; fall back to the consumer's registration.
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.OwnedOverRequestingTypeFactory,
				parameter.Location ?? instanceLocations[consumer],
				new EquatableArray<string>([
					DisplayInstance(instances[consumer].ImplementationType),
					DisplayInstance(instances[target].ImplementationType),
				])));
		}
	}

	/// <summary>
	///     AWT122: a collection dependency is materialized synchronously, so an async-tainted member would be
	///     resolved without awaiting its initialization. Reported under both strict and loose safety; only
	///     SyncResolveAfterInit suppresses it.
	/// </summary>
	private static void DetectSynchronousAsyncCollection(
		List<InstanceModel> instances,
		List<ServiceMembers> collections,
		List<KeyedServiceMembers> keyedCollections,
		Dictionary<string, int> implToIndex,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		if (collections.Count == 0 && keyedCollections.Count == 0)
		{
			return;
		}

		Dictionary<ServiceKey, ServiceMembers> byService = new();
		foreach (ServiceMembers collection in collections)
		{
			byService[new ServiceKey(collection.Service, collection.Key)] = collection;
		}

		Dictionary<string, KeyedServiceMembers> byKeyedService = new(StringComparer.Ordinal);
		foreach (KeyedServiceMembers keyed in keyedCollections)
		{
			byKeyedService[keyed.Service] = keyed;
		}

		for (int i = 0; i < instances.Count; i++)
		{
			foreach (ParameterModel parameter in instances[i].ConstructorParameters.AsArray())
			{
				CheckCollection(i, parameter);
			}

			// An injected [Inject] collection member (deferred or not) is materialized through the same
			// synchronous expression as a constructor parameter, so it is checked the same way.
			foreach (ParameterModel dependency in instances[i].InjectedMembers.AsArray().Select(member => member.Dependency))
			{
				CheckCollection(i, dependency);
			}
		}

		void CheckCollection(int consumer, ParameterModel dependency)
		{
			if (dependency.Kind == DependencyKind.Enumerable
			    && byService.TryGetValue(KeyOf(dependency), out ServiceMembers members))
			{
				ReportAsyncTaintedMembers(consumer, dependency, members.Implementations.AsArray(), instances, implToIndex, instanceLocations, diagnostics);
			}

			// A keyed collection is also materialized synchronously into a dictionary, so an async-tainted keyed
			// member cannot have its initialization awaited: the same AWT122 concern as a synchronous collection.
			if (dependency.Kind == DependencyKind.KeyedCollection
			    && byKeyedService.TryGetValue(dependency.ServiceType, out KeyedServiceMembers keyed))
			{
				ReportAsyncTaintedMembers(
					consumer, dependency, keyed.Members.AsArray().Select(member => member.Implementation).ToArray(),
					instances, implToIndex, instanceLocations, diagnostics);
			}
		}
	}

	/// <summary>
	///     Reports AWT122 for each async-tainted member of the collection <paramref name="consumer" /> injects
	///     through <paramref name="parameter" />. Shared by the synchronous collection and keyed dictionary.
	/// </summary>
	private static void ReportAsyncTaintedMembers(
		int consumer,
		ParameterModel parameter,
		string[] members,
		List<InstanceModel> instances,
		Dictionary<string, int> implToIndex,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		foreach (string member in members)
		{
			if (!implToIndex.TryGetValue(member, out int memberIndex) || !instances[memberIndex].IsAsyncTainted)
			{
				continue;
			}

			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.AsyncCollectionResolution,
				instanceLocations[consumer],
				new EquatableArray<string>([
					DisplayInstance(instances[consumer].ImplementationType),
					Display(parameter.ServiceType),
					DisplayInstance(instances[memberIndex].ImplementationType),
				])));
		}
	}

	/// <summary>
	///     The shortest chain of Direct edges from <paramref name="start" /> to an async-initialized
	///     instance, rendered for the AWT120 message.
	/// </summary>
	private static string AsyncTaintPath(List<InstanceModel> instances, Dictionary<int, List<int>> dependencies, int start)
	{
		Queue<int> queue = new();
		Dictionary<int, int> previous = new();
		HashSet<int> visited = new() { start, };
		queue.Enqueue(start);
		int end = start;
		while (queue.Count > 0)
		{
			int node = queue.Dequeue();
			if (instances[node].IsAsyncSource)
			{
				end = node;
				break;
			}

			foreach (int next in dependencies[node].Where(visited.Add))
			{
				previous[next] = node;
				queue.Enqueue(next);
			}
		}

		List<int> chain = new();
		for (int node = end; ; node = previous[node])
		{
			chain.Insert(0, node);
			if (node == start)
			{
				break;
			}
		}

		return string.Join(" -> ", chain.Select(index => DisplayInstance(instances[index].ImplementationType)));
	}

	/// <summary>
	///     Validates how parameterized services (those with <c>[Arg]</c> parameters) are registered and consumed:
	///     AWT114 (a parameterized service is built per request, so it must be Transient), AWT113 (a
	///     <c>Func&lt;TArg…, T&gt;</c> must request exactly the target's <c>[Arg]</c> types, in order), and
	///     AWT115 (a plain or <c>Lazy&lt;T&gt;</c> dependency cannot supply them, so a parameterized target is
	///     reachable only through a <c>Func</c>).
	/// </summary>
	private static void ValidateRuntimeArguments(
		List<InstanceModel> instances,
		List<LocationInfo?> instanceLocations,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		List<DiagnosticInfo> diagnostics)
	{
		for (int i = 0; i < instances.Count; i++)
		{
			InstanceModel instance = instances[i];
			LocationInfo? location = instanceLocations[i];

			if (instance.IsParameterized && instance.Lifetime != Lifetime.Transient)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ParameterizedLifetime,
					location,
					new EquatableArray<string>([Display(instance.ImplementationType), instance.Lifetime.ToString(),])));
			}

			// A parameterized async service is built fresh per call AND must await initialization, so its path is
			// Func<TArg…, Task<T>>. Misuse is caught at the consumption site (AWT119 or AWT115), not the
			// registration, so there is no registration-time diagnostic for [Arg]-plus-async.
			foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
			{
				// A collection resolves to a set of members, not a single registration whose [Arg] parameters could
				// be supplied, so runtime-argument matching does not apply (excluded explicitly). Guard the
				// implToIndex lookup: serviceToImpl can name an impl whose BuildInstance failed, and an unguarded
				// indexer would crash the generator instead of surfacing the real registration error.
				if (parameter.Kind == DependencyKind.Arg
				    || IsSynthesizedCollection(parameter.Kind)
				    || !serviceToImpl.TryGetValue(KeyOf(parameter), out string? targetImpl)
				    || !implToIndex.TryGetValue(targetImpl, out int targetIndex))
				{
					continue;
				}

				ValidateDependency(
					instance, parameter, instances[targetIndex].ArgTypes(), location, diagnostics);
			}
		}
	}

	/// <summary>
	///     Validates a single (non-<c>[Arg]</c>) dependency against its target's runtime arguments: a
	///     <c>Func&lt;TArg…, T&gt;</c> (or async <c>Func&lt;TArg…, Task&lt;T&gt;&gt;</c>) must request exactly them
	///     (AWT113); a plain, <c>Lazy&lt;T&gt;</c> or <c>Task&lt;T&gt;</c> dependency cannot, so AWT115.
	/// </summary>
	private static void ValidateDependency(
		InstanceModel consumer,
		ParameterModel parameter,
		string[] expected,
		LocationInfo? consumerLocation,
		List<DiagnosticInfo> diagnostics)
	{
		// Point the diagnostic at the offending parameter; fall back to the consumer's registration when the
		// parameter has no usable location.
		LocationInfo? location = parameter.Location ?? consumerLocation;

		if (parameter.Kind is DependencyKind.Func or DependencyKind.FuncTask)
		{
			string[] requested = parameter.FuncArgTypes.AsArray();
			if (!requested.SequenceEqual(expected))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.RuntimeArgumentMismatch,
					location,
					new EquatableArray<string>([
						Display(parameter.ServiceType),
						FormatTypeList(requested),
						FormatTypeList(expected),
					])));
			}
		}
		else if (expected.Length > 0)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ParameterizedRequiresFunc,
				location,
				new EquatableArray<string>([Display(parameter.ServiceType), DisplayInstance(consumer.ImplementationType),])));
		}
	}

	/// <summary>
	///     Renders a runtime-argument type list for a diagnostic message, reading as "none" when empty so a
	///     mismatch against a service with no <c>[Arg]</c> parameters (or a <c>Func</c> that supplies none) is not an
	///     empty "()".
	/// </summary>
	private static string FormatTypeList(string[] types)
		=> types.Length == 0 ? "none" : string.Join(", ", types.Select(Display));

	private static void DetectCaptiveDependencies(
		List<InstanceModel> instances,
		Dictionary<int, List<int>> dependencies,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		for (int i = 0; i < instances.Count; i++)
		{
			if (instances[i].Lifetime == Lifetime.Singleton)
			{
				ReportCapturedScoped(i, instances, dependencies, instanceLocations, diagnostics);
			}
		}
	}

	private static void ReportCapturedScoped(
		int singleton,
		List<InstanceModel> instances,
		Dictionary<int, List<int>> dependencies,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		// Walk the singleton's graph through its transient dependencies (baked into it). Reaching a scoped
		// service means the singleton captures it for the container's life. Each node carries its referencing
		// dependency so the diagnostic names the alias the developer wrote.
		HashSet<int> visited = new();
		Stack<(int Node, int Parent)> stack = new();
		foreach (int dependency in dependencies[singleton])
		{
			stack.Push((dependency, singleton));
		}

		while (stack.Count > 0)
		{
			(int node, int parent) = stack.Pop();
			if (!visited.Add(node))
			{
				continue;
			}

			switch (instances[node].Lifetime)
			{
				case Lifetime.Scoped:
					ServiceKey referenced = ReferencedService(instances[parent], instances[node]);
					diagnostics.Add(new DiagnosticInfo(
						Diagnostics.CaptiveDependency,
						instanceLocations[singleton],
						new EquatableArray<string>([
							DisplayInstance(instances[singleton].ImplementationType),
							DisplayKeyed(referenced.Service, referenced.Key),
						])));
					break;
				case Lifetime.Transient:
					foreach (int next in dependencies[node])
					{
						stack.Push((next, node));
					}

					break;
			}
		}

		// The service key the parent's constructor used to reach this dependency (the alias the developer wrote,
		// including any [FromKey]). Falls back to the first service key, or the implementation type when the
		// dependency exposes no service of its own, so the diagnostic still names it.
		static ServiceKey ReferencedService(InstanceModel parent, InstanceModel dependency)
		{
			ServiceKey[] dependencyServices = dependency.Services.AsArray();
			foreach (ParameterModel parameter in parent.ConstructorParameters.AsArray())
			{
				ServiceKey key = KeyOf(parameter);
				if (dependencyServices.Contains(key))
				{
					return key;
				}
			}

			return dependencyServices.Length > 0 ? dependencyServices[0] : new ServiceKey(dependency.ImplementationType, null);
		}
	}

	/// <summary>
	///     Reports <see cref="Diagnostics.EagerAsyncSingleton">AWT161</see> for an async-tainted <c>Eager</c>
	///     singleton: it has no synchronous construction path, so it cannot be built in the root's synchronous
	///     constructor. Strict default only; <c>SyncResolveAfterInit</c> emits a blocking resolver and allows it.
	/// </summary>
	private static void DetectEagerAsyncSingletons(
		List<InstanceModel> instances,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		for (int i = 0; i < instances.Count; i++)
		{
			if (instances[i].Eager && instances[i].IsAsyncTainted)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.EagerAsyncSingleton,
					instanceLocations[i],
					new EquatableArray<string>([DisplayInstance(instances[i].ImplementationType),])));
			}
		}
	}

	private static void DetectCycles(
		List<InstanceModel> instances,
		Dictionary<int, List<int>> dependencies,
		LocationInfo? containerLocation,
		List<DiagnosticInfo> diagnostics)
	{
		HashSet<int> visited = new();
		HashSet<int> onStack = new();
		List<int> path = new();
		HashSet<string> reportedCycles = new(StringComparer.Ordinal);

		for (int i = 0; i < instances.Count; i++)
		{
			Visit(i);
		}

		void Visit(int node)
		{
			visited.Add(node);
			onStack.Add(node);
			path.Add(node);

			foreach (int next in dependencies[node])
			{
				if (onStack.Contains(next))
				{
					ReportCycle(next);
				}
				else if (!visited.Contains(next))
				{
					Visit(next);
				}
			}

			onStack.Remove(node);
			path.RemoveAt(path.Count - 1);
		}

		void ReportCycle(int cycleStart)
		{
			int startIndex = path.LastIndexOf(cycleStart);
			List<int> cycle = path.GetRange(startIndex, path.Count - startIndex);
			cycle.Add(cycleStart);

			// Dedupe on the set of nodes so the same cycle is not reported once per back-edge.
			string signature = string.Join("|", cycle.Take(cycle.Count - 1).OrderBy(x => x));
			if (!reportedCycles.Add(signature))
			{
				return;
			}

			string rendered = string.Join(" -> ", cycle.Select(index => DisplayInstance(instances[index].ImplementationType)));
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.DependencyCycle,
				containerLocation,
				new EquatableArray<string>([rendered,])));
		}
	}

	/// <summary>
	///     AWT145/AWT146/AWT147: a deferred property breaks a mutual cycle only because the owning instance is
	///     cached before its deferred members are wired, so a re-entrant resolve returns the cached instance. Any
	///     cycle involving a deferred edge escapes AWT102 (which walks only the construction graph), so it is
	///     vetted here, per strongly connected component of the combined graph rather than per DFS-enumerated cycle
	///     (a component can mix supported and faulty cycles, so a complete verdict needs the whole edge set). See
	///     <see cref="ClassifyDeferredComponent" /> for the faults.
	/// </summary>
	private static void DetectNonTerminatingDeferredCycles(
		List<InstanceModel> instances,
		Dictionary<int, List<int>> constructionEdges,
		Dictionary<int, List<int>> combinedEdges,
		LocationInfo? containerLocation,
		List<DiagnosticInfo> diagnostics)
	{
		// Without a deferred member the combined graph IS the construction graph and every cycle in it is
		// AWT102's business; the (common) deferred-free container pays one member scan and no graph walk.
		if (!AnyDeferredMember(instances))
		{
			return;
		}

		foreach (List<int> component in StronglyConnectedComponents(instances.Count, combinedEdges))
		{
			// Only a non-trivial component (more than one node, or a self-loop) contains a cycle at all.
			if (component.Count > 1 || combinedEdges[component[0]].Contains(component[0]))
			{
				ClassifyDeferredComponent(component, instances, constructionEdges, combinedEdges, containerLocation, diagnostics);
			}
		}
	}

	/// <summary>
	///     Classifies one non-trivial strongly connected component of the combined graph and reports its fault, if
	///     any (one diagnostic per component, rendering a demonstrating cycle). A construction edge whose source is
	///     cached (singleton/scoped) re-enters that source before it is cached (AWT147). With no cached participant
	///     nothing terminates the re-entry: AWT147 with a construction edge in the mix, AWT145 for the all-deferred
	///     all-transient cycle. An async-tainted participant publishes its memoized task too late, so a deferred
	///     cycle through it cannot terminate (AWT146). The supported case (nothing reported) has every construction
	///     edge sourced at a transient, no async participant, and at least one synchronously-cached participant whose
	///     cache terminates the re-entry. A pure construction cycle is left to AWT102.
	/// </summary>
	private static void ClassifyDeferredComponent(
		List<int> component,
		List<InstanceModel> instances,
		Dictionary<int, List<int>> constructionEdges,
		Dictionary<int, List<int>> combinedEdges,
		LocationInfo? containerLocation,
		List<DiagnosticInfo> diagnostics)
	{
		HashSet<int> members = new(component);
		ComponentEdges edges = FindComponentEdges(component, members, instances, constructionEdges, combinedEdges);

		// No deferred edge: a pure construction cycle, which AWT102 already reports.
		if (edges.Deferred is null)
		{
			return;
		}

		// The construction edges alone already close a cycle (AWT102 reports it). The verdicts below assume every
		// cycle through a construction edge also traverses a deferred edge, so defer to AWT102 here.
		if (edges.Construction is not null && HasConstructionCycle(component, members, constructionEdges))
		{
			return;
		}

		if (edges.CachedSourceConstruction is { } duplicating)
		{
			Report(Diagnostics.DeferredMixedCycle, duplicating);
			return;
		}

		(bool anyCached, int asyncParticipant) = ScanParticipants(component, instances);

		if (!anyCached)
		{
			if (edges.Construction is { } mixed)
			{
				Report(Diagnostics.DeferredMixedCycle, mixed);
			}
			else
			{
				Report(Diagnostics.DeferredTransientCycle, edges.Deferred.Value);
			}

			return;
		}

		if (asyncParticipant >= 0)
		{
			// Demonstrate a cycle through the async participant: its first intra-component edge closes one (a
			// participant of a non-trivial strongly connected component always has one).
			int closing = combinedEdges[asyncParticipant].First(members.Contains);
			Report(Diagnostics.DeferredAsyncCycle, (asyncParticipant, closing));
			return;
		}

		// Supported: every construction edge starts at a transient and a synchronously-cached participant
		// terminates the re-entry, so the cycle terminates from every entry point.
		void Report(DiagnosticDescriptor descriptor, (int Source, int Target) edge)
		{
			List<int> cycle = CycleThroughEdge(edge.Source, edge.Target, members, combinedEdges);
			string rendered = string.Join(" -> ", cycle.Select(index => DisplayInstance(instances[index].ImplementationType)));
			diagnostics.Add(new DiagnosticInfo(descriptor, containerLocation, new EquatableArray<string>([rendered,])));
		}
	}

	/// <summary>
	///     The intra-component edges that drive the classification: the first deferred edge (its presence marks the
	///     component as a deferred cycle), the first construction edge, and the first construction edge whose source
	///     is cached (a singleton/scoped re-entered before it is cached). Any may be absent.
	/// </summary>
	private static ComponentEdges FindComponentEdges(
		List<int> component,
		HashSet<int> members,
		List<InstanceModel> instances,
		Dictionary<int, List<int>> constructionEdges,
		Dictionary<int, List<int>> combinedEdges)
	{
		(int Source, int Target)? deferredEdge = null;
		(int Source, int Target)? constructionEdge = null;
		(int Source, int Target)? cachedSourceConstructionEdge = null;
		foreach (int node in component)
		{
			foreach (int next in combinedEdges[node])
			{
				if (!members.Contains(next))
				{
					continue;
				}

				if (constructionEdges[node].Contains(next))
				{
					constructionEdge ??= (node, next);
					if (instances[node].Lifetime != Lifetime.Transient)
					{
						cachedSourceConstructionEdge ??= (node, next);
					}
				}
				else
				{
					deferredEdge ??= (node, next);
				}
			}
		}

		return new ComponentEdges(deferredEdge, constructionEdge, cachedSourceConstructionEdge);
	}

	/// <summary>
	///     Scans the component once for the two participant facts the classification needs: whether any participant
	///     is synchronously cached (can terminate the re-entry) and the first async-tainted participant (-1 when none).
	/// </summary>
	private static (bool AnyCached, int AsyncParticipant) ScanParticipants(List<int> component, List<InstanceModel> instances)
	{
		bool anyCached = false;
		int asyncParticipant = -1;
		foreach (int node in component)
		{
			anyCached |= instances[node].Lifetime != Lifetime.Transient;
			if (asyncParticipant < 0 && instances[node].IsAsyncTainted)
			{
				asyncParticipant = node;
			}
		}

		return (anyCached, asyncParticipant);
	}

	private readonly struct ComponentEdges((int Source, int Target)? deferred, (int Source, int Target)? construction, (int Source, int Target)? cachedSourceConstruction)
	{
		public (int Source, int Target)? Deferred { get; } = deferred;

		public (int Source, int Target)? Construction { get; } = construction;

		public (int Source, int Target)? CachedSourceConstruction { get; } = cachedSourceConstruction;
	}

	/// <summary>
	///     Whether the construction edges alone close a cycle within the component (a visited/on-stack DFS restricted
	///     to the component's nodes).
	/// </summary>
	private static bool HasConstructionCycle(List<int> component, HashSet<int> members, Dictionary<int, List<int>> constructionEdges)
	{
		HashSet<int> visited = new();
		HashSet<int> onStack = new();
		return component.Where(node => !visited.Contains(node)).Any(Visit);

		bool Visit(int node)
		{
			visited.Add(node);
			onStack.Add(node);
			foreach (int next in constructionEdges[node])
			{
				if (members.Contains(next) && (onStack.Contains(next) || (!visited.Contains(next) && Visit(next))))
				{
					return true;
				}
			}

			onStack.Remove(node);
			return false;
		}
	}

	/// <summary>
	///     A concrete cycle through the intra-component edge source -&gt; target, for a diagnostic: the edge followed
	///     by the shortest path from target back to source, rendered "A -&gt; B -&gt; A".
	/// </summary>
	private static List<int> CycleThroughEdge(int source, int target, HashSet<int> members, Dictionary<int, List<int>> edges)
	{
		List<int> cycle = new() { source, };
		cycle.AddRange(PathBetween(target, source, members, edges));
		return cycle;
	}

	/// <summary>
	///     The shortest path from one component node to another (inclusive on both ends) over the component's edges,
	///     by breadth-first search; a from == to path is the single node.
	/// </summary>
	private static List<int> PathBetween(int from, int to, HashSet<int> members, Dictionary<int, List<int>> edges)
	{
		if (from == to)
		{
			return new List<int> { from, };
		}

		Dictionary<int, int> previous = new();
		HashSet<int> visited = new() { from, };
		Queue<int> queue = new();
		queue.Enqueue(from);
		while (queue.Count > 0)
		{
			int node = queue.Dequeue();
			foreach (int next in edges[node])
			{
				if (!members.Contains(next) || !visited.Add(next))
				{
					continue;
				}

				previous[next] = node;
				if (next == to)
				{
					queue.Clear();
					break;
				}

				queue.Enqueue(next);
			}
		}

		List<int> path = new();
		for (int node = to; ; node = previous[node])
		{
			path.Insert(0, node);
			if (node == from)
			{
				break;
			}
		}

		return path;
	}

	/// <summary>
	///     Tarjan's strongly connected components over the instance indices <c>[0, count)</c>, in a
	///     deterministic order (the graph and iteration order are deterministic, so the emitted diagnostics
	///     are cacheable by the incremental pipeline).
	/// </summary>
	private static List<List<int>> StronglyConnectedComponents(int count, Dictionary<int, List<int>> edges)
	{
		// index[v] holds the 1-based discovery index (0 = unvisited); low[v] the smallest index reachable from
		// v's DFS subtree through at most one back-edge. A node whose low-link equals its own index roots a
		// strongly connected component, which is popped off the stack as a unit.
		int[] index = new int[count];
		int[] low = new int[count];
		bool[] onStack = new bool[count];
		Stack<int> stack = new();
		List<List<int>> components = new();
		int nextIndex = 0;

		for (int i = 0; i < count; i++)
		{
			if (index[i] == 0)
			{
				Connect(i);
			}
		}

		return components;

		void Connect(int node)
		{
			index[node] = low[node] = ++nextIndex;
			stack.Push(node);
			onStack[node] = true;

			foreach (int next in edges[node])
			{
				if (index[next] == 0)
				{
					Connect(next);
					low[node] = Math.Min(low[node], low[next]);
				}
				else if (onStack[next])
				{
					low[node] = Math.Min(low[node], index[next]);
				}
			}

			if (low[node] == index[node])
			{
				List<int> component = new();
				int popped;
				do
				{
					popped = stack.Pop();
					onStack[popped] = false;
					component.Add(popped);
				}
				while (popped != node);

				components.Add(component);
			}
		}
	}

	private static string KeywordOf(INamedTypeSymbol symbol)
	{
		if (symbol.IsRecord)
		{
			return symbol.TypeKind == TypeKind.Struct ? "record struct" : "record";
		}

		return symbol.TypeKind == TypeKind.Struct ? "struct" : "class";
	}

	/// <summary>
	///     Strips every <c>global::</c> alias (the leading one and any nested in generic type arguments) so
	///     diagnostics read <c>System.Func&lt;MyCode.Leaf&gt;</c> rather than <c>System.Func&lt;global::MyCode.Leaf&gt;</c>.
	/// </summary>
	internal static string Display(string fullyQualified)
		=> fullyQualified.Replace("global::", string.Empty);

	/// <summary>
	///     Renders an instance identity for diagnostics. A decorator chain link carries a synthetic
	///     <c>&lt;type&gt;@__dec:…</c> identity; trim the synthetic suffix so an error names the real decorator type.
	/// </summary>
	internal static string DisplayInstance(string implementationType)
	{
		int marker = implementationType.IndexOf("@" + DecoratorKeyPrefix, StringComparison.Ordinal);
		return Display(marker >= 0 ? implementationType.Substring(0, marker) : implementationType);
	}
}
