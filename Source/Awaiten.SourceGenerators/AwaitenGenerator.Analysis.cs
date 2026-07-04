using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     The built-instance indices of every collection-resolvable service's members - the plain
	///     <paramref name="collections" /> keyed by (element service type, key) and the
	///     <paramref name="keyedCollections" /> keyed by service (value) type - for the transitive-disposable
	///     walk (a member absent from <paramref name="implToIndex" /> failed to build and is skipped).
	/// </summary>
	internal static CollectionMembership MembershipIndices(
		IReadOnlyList<ServiceMembers> collections,
		IReadOnlyList<KeyedServiceMembers> keyedCollections,
		Dictionary<string, int> implToIndex)
		=> new(CollectionMemberIndices(collections, implToIndex), KeyedCollectionMemberIndices(keyedCollections, implToIndex));

	// The plain collections' member indices, keyed by the collection's (element service type, key).
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

	// The keyed collections' member indices, keyed by the service (value) type.
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
	///     follows only <em>transient</em> dependency edges, because a scoped or singleton dependency is
	///     cached/shared (built at most once on the owner) and so is bounded, whereas a transient dependency is
	///     rebuilt - and, if disposable, re-tracked - on every construction. A collection (Enumerable) edge - and a
	///     keyed collection (IReadOnlyDictionary&lt;string, T&gt;) edge - is followed too: each materializes its
	///     members eagerly into the owner, so a transient disposable member is rebuilt on every construction just
	///     like a direct transient dependency. Used to decide whether a plain
	///     <c>Func&lt;…&gt;</c> over the service accumulates on the container root (AWT118 / strict withholding):
	///     a non-disposable transient that injects a disposable transient leaks just the same when built
	///     repeatedly through a root-bound factory.
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

	// Pushes the dependencies of <paramref name="instance" /> that are rebuilt as part of constructing it: a
	// direct (non-deferred) transient dependency, and each transient member of a collection (a collection -
	// including the awaited Task<C> form, whose task starts materializing at construction - builds its members
	// eagerly during construction). A relationship/Owned/Arg parameter defers, and a scoped/singleton dependency
	// is cached/shared, so neither is followed.
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
			else if (parameter.Kind == DependencyKind.KeyedCollection)
			{
				// A keyed dictionary materializes its members eagerly during construction too, so a transient
				// disposable keyed member is rebuilt on every construction just like a plain collection member.
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

	// Pushes each transient member of the collection reached under <paramref name="collectionKey" /> (its
	// element service type plus resolution key; a non-transient member is cached/shared, so it is bounded).
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

	// Pushes each transient member of the keyed collection for <paramref name="serviceType" /> (a non-transient
	// member is cached/shared, so it is bounded). The keyed analogue of PushTransientCollectionMembers, grouped
	// by service (value) type rather than (element type, key).
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

	// The direct-dependency graph over instance indices (resolvable edges to built instances): the edge set
	// for captive-dependency analysis (AWT105), async-taint propagation, and the synchronous-async checks
	// (AWT119/AWT120). The relationship types (Func<T>/Lazy<T>/…) and the bare eager relationships
	// (Owned<T>/Task<T>) all defer or launder, so only direct (and eager collection) dependencies contribute
	// edges here. A deferred [Inject(Deferred = true)] member is included: its post-construction assignment still
	// captures its target for the owner's lifetime (captive) and awaits an async target (taint), so for every use
	// but cycle detection it behaves exactly like a constructor edge. Only cycle detection excludes it (that
	// exclusion is what lets it break a mutual constructor cycle - AWT102), which uses BuildConstructionGraph.
	private static Dictionary<int, List<int>> BuildDependencyGraph(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, List<KeyedMember>> keyedMembers)
		=> BuildEdges(instances, serviceToImpl, implToIndex, serviceMembers, keyedMembers, includeEagerBare: false, includeDeferredMembers: true);

	// The construction graph over instance indices, for cycle detection (AWT102): the direct edges plus the bare
	// eager relationships Owned<T> and Task<T> (the latter also covering Task<Owned<T>>). Unlike their deferred
	// Func/Lazy wrappers - which are stored as a closure and invoked later - these resolve their target during the
	// owner's construction: synchronously for a synchronous target, and in the synchronous prefix (before the
	// first await, and before the memoized task is published) of an async resolver. A cycle closed through one of
	// them therefore re-enters an as-yet-uncached resolver and overflows the stack at runtime rather than being
	// broken, so it is reported as a dependency cycle (AWT102). Deferred [Inject(Deferred = true)] members are
	// excluded here (that exclusion is what lets them break a cycle); unlike BuildDependencyGraph, which includes
	// them for captive/taint, so neither graph is a strict superset of the other.
	private static Dictionary<int, List<int>> BuildConstructionGraph(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, List<KeyedMember>> keyedMembers)
		=> BuildEdges(instances, serviceToImpl, implToIndex, serviceMembers, keyedMembers, includeEagerBare: true);

	// The combined construction-plus-deferred graph over instance indices, for the deferred-cycle analysis
	// (AWT145/AWT146/AWT147): the construction edges plus the edges a deferred [Inject(Deferred = true)] member
	// contributes when its post-construction assignment runs. Deferred members are excluded from the cycle
	// (AWT102) and captive (AWT105) graphs - that exclusion is what lets a deferred property break a mutual
	// constructor cycle - so cycles that involve them are vetted over this union instead. Uses construction-graph
	// semantics (includeEagerBare) because a deferred assignment resolves its target during the owner's
	// construction episode, exactly like a construction edge.
	private static Dictionary<int, List<int>> BuildCombinedGraph(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, List<KeyedMember>> keyedMembers)
		=> BuildEdges(instances, serviceToImpl, implToIndex, serviceMembers, keyedMembers, includeEagerBare: true, includeDeferredMembers: true);

	/// <summary>
	///     Whether any built instance has a deferred (<c>[Inject(Deferred = true)]</c>) member - the gate for
	///     building and walking the combined construction-plus-deferred graph at all (without one, that graph is
	///     the construction graph and every cycle in it is AWT102's business).
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

	// Builds the edge set over instance indices, keeping only the parameters that contribute an edge and that
	// resolve to a built instance. A direct dependency always contributes; the bare eager relationships
	// Owned<T> and Task<T> contribute only when <paramref name="includeEagerBare" /> is set (the construction
	// graph), since they resolve eagerly and so close cycles even though they launder async taint. A collection
	// (Enumerable) contributes an edge to each of its members in both graphs: it materializes them eagerly into
	// an array, so it captures them (taint/captive) and closes cycles through them just like a direct dependency.
	// A deferred [Inject(Deferred = true)] member contributes only when <paramref name="includeDeferredMembers" />
	// is set (the taint graph): its assignment is excluded from cycle (AWT102) and captive (AWT105) analysis - that
	// exclusion is what lets it break a mutual constructor cycle - but a deferred Direct/collection member to an
	// async-tainted target is awaited at assignment time, so its taint must still reach the owner (otherwise a
	// synchronous owner would emit a synchronous resolve of an async-only service and fail to compile).
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

			// An injected [Inject] member is a full graph edge just like a constructor parameter: a Direct
			// member captures its target (and, resolved at construction, closes a cycle through it), a collection
			// member edges to each of its members, and a relationship member defers - so AddParameterEdges
			// classifies it identically to a constructor edge.
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

	// Appends the edge(s) a single parameter contributes to its node's edge list. A collection - synchronous
	// (Enumerable) or asynchronous (AsyncEnumerable) - edges to each of its members; a direct dependency (and, in
	// the construction graph, a bare eager Owned<T>/Task<T>) edges to its single resolved instance; everything else
	// defers and contributes nothing. Both collection kinds materialize their members eagerly, so both capture them
	// (taint/captive) and close cycles through them; they differ only in that AsyncEnumerable awaits its members.
	// An awaited collection (AwaitedEnumerable) is the collection form of the bare Task<T>: its members are awaited
	// behind the produced task, not at the consumer's construction, so it launders their taint - but the task starts
	// materializing them during construction, so its member edges still close cycles (the construction graph only).
	private static void AddParameterEdges(
		ParameterModel parameter,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, List<KeyedMember>> keyedMembers,
		bool includeEagerBare,
		List<int> nodeEdges)
	{
		// A keyed collection materializes every keyed member eagerly into a dictionary, so - like a synchronous
		// collection - it captures them (taint/captive) and closes cycles through them, in both graphs.
		if (parameter.Kind == DependencyKind.KeyedCollection)
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

	// Appends an edge to each built member of the collection reached under <paramref name="collectionKey" />
	// (its element service type plus resolution key; a member absent from implToIndex failed to build and is skipped).
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

	// Appends an edge to each built keyed member of the keyed collection for <paramref name="serviceType" />
	// (a member absent from implToIndex failed to build and is skipped). Mirrors AddCollectionMemberEdges, but the
	// membership is grouped by service (value) type - a keyed collection spans every key of the service, not a
	// single (type, key) collection.
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
	///     Marks every instance that is an async-taint source - its implementation is async-initialized, or it
	///     is produced by an asynchronous factory (Task&lt;T&gt; / ValueTask&lt;T&gt;), which the container can
	///     only reach by awaiting - or that reaches one through non-deferred (Direct) edges, by fixpoint over
	///     the dependency graph. The edges already exclude relationship/Owned/Arg parameters, so the taint is
	///     laundered by exactly the deferrals that break cycles.
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
	///     <c>Owned&lt;T&gt;</c> relationship resolves its target on demand without awaiting initialization,
	///     so it must not target an async-tainted service. AWT119 fires when the target is itself
	///     async-initialized; AWT120 fires when it only reaches one transitively, and reports the dependency
	///     path. (The prototype checked only <c>Func</c>/<c>Lazy</c>; <c>Owned</c> is included here because
	///     it is the same synchronous deferral and an async-tainted service emits no synchronous resolver
	///     for the <c>Owned</c> handle to build into.) An injected <c>[Inject]</c> member resolves through
	///     the same synchronous expression as a constructor parameter - and a deferred one still resolves
	///     synchronously at wiring time, while its <c>Func</c>/<c>Lazy</c>/<c>Owned</c> wrapper launders the
	///     async taint off the owner - so member relationships are checked exactly like parameters here.
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
			// Guard the implToIndex lookup: serviceToImpl can name an implementation whose BuildInstance
			// failed (so it is absent from implToIndex), and an unguarded indexer would crash the generator
			// (KeyNotFoundException) instead of surfacing the real registration error. Mirrors the guard in
			// BuildDependencyGraph / ValidateRuntimeArguments.
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
	///     AWT122: a collection dependency is materialized synchronously, so a member whose implementation is
	///     async-tainted would be resolved without awaiting its initialization. Collections are synchronous-only,
	///     so this is reported rather than silently emitting a synchronous resolver for an async-tainted member
	///     (which the synchronous path does not even generate for such a member). Like AWT119/AWT120 this is a
	///     synchronous-resolution-of-async concern independent of lifetime safety, so it is reported under both
	///     strict and loose safety; only the pragmatic SyncResolveAfterInit mode suppresses it (the caller gates
	///     on that, as it does for AWT119/AWT120).
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
			// member cannot have its initialization awaited - the same AWT122 concern as a synchronous collection.
			if (dependency.Kind == DependencyKind.KeyedCollection
			    && byKeyedService.TryGetValue(dependency.ServiceType, out KeyedServiceMembers keyed))
			{
				ReportAsyncTaintedMembers(
					consumer, dependency, keyed.Members.AsArray().Select(member => member.Implementation).ToArray(),
					instances, implToIndex, instanceLocations, diagnostics);
			}
		}
	}

	// Reports AWT122 for each async-tainted member of the collection <paramref name="consumer" /> injects
	// through <paramref name="parameter" /> (a member absent from implToIndex failed to build and is skipped).
	// <paramref name="members" /> is the member implementations, shared by the synchronous collection and the
	// synchronous keyed dictionary - both materialize their members eagerly with no place to await one.
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
	///     Validates how parameterized services (those with <c>[Arg]</c>-marked parameters) are registered
	///     and consumed:
	///     <list type="bullet">
	///         <item>
	///             AWT114: a parameterized service is built fresh from its runtime arguments on every
	///             request, so a non-<c>Transient</c> lifetime cannot be honored.
	///         </item>
	///         <item>
	///             AWT113: a <c>Func&lt;TArg…, T&gt;</c> relationship must request exactly the runtime
	///             arguments that <c>T</c>'s <c>[Arg]</c> parameters expect, in order (a plain
	///             <c>Func&lt;T&gt;</c> over a parameterized service requests none, so it mismatches).
	///         </item>
	///         <item>
	///             AWT115: a parameterized service requested as a plain dependency or a <c>Lazy&lt;T&gt;</c>
	///             cannot be supplied its runtime arguments, so it is reachable only through a
	///             <c>Func&lt;TArg…, T&gt;</c>.
	///         </item>
	///     </list>
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

			// A parameterized async service (an [Arg] service that is IAsyncInitializable, is produced by an
			// asynchronous Task<T> / ValueTask<T> factory, or transitively reaches one) is built fresh per call
			// from its runtime arguments AND must await initialization, so its correct resolution path is the
			// async parameterized factory relationship Func<TArg…, Task<T>>, which forwards the arguments to the
			// async resolver. Misuse is caught at the consumption site rather than the registration: a synchronous
			// Func<TArg…, T> over it is AWT119 (cannot await), and a plain / Lazy<T> / Task<T> dependency that
			// supplies no arguments is AWT115 (parameterized requires a Func). There is therefore no
			// registration-time diagnostic for the [Arg]-plus-async combination itself.
			foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
			{
				// A collection (Enumerable, AsyncEnumerable or AwaitedEnumerable) resolves to a set of members, not
				// a single registration whose [Arg] parameters could be supplied, so runtime-argument matching does
				// not apply to it (its element type may coincidentally be singly registered, so it is excluded
				// explicitly rather than by the serviceToImpl lookup below). Guard the implToIndex lookup the same
				// way BuildDependencyGraph does: serviceToImpl can name an implementation whose BuildInstance failed
				// (so it is absent from implToIndex), and an unguarded indexer would crash the generator
				// (KeyNotFoundException) instead of surfacing the real registration error (e.g. AWT103).
				if (parameter.Kind is DependencyKind.Arg or DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable or DependencyKind.KeyedCollection
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
	///     Validates a single (non-<c>[Arg]</c>) dependency against its target's runtime arguments
	///     (<paramref name="expected" />): a <c>Func&lt;TArg…, T&gt;</c> or its async form
	///     <c>Func&lt;TArg…, Task&lt;T&gt;&gt;</c> must request exactly them (AWT113); a plain, <c>Lazy&lt;T&gt;</c>
	///     or <c>Task&lt;T&gt;</c> dependency cannot supply them at all, so a parameterized target must instead be
	///     reached through a <c>Func</c> (AWT115).
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

	// Renders a runtime-argument type list for a diagnostic message, reading as "none" when empty so a
	// mismatch against a service with no [Arg] parameters (or a Func that supplies none) is not an empty "()".
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
		// Walk the singleton's graph through its transient dependencies (which are baked into it).
		// Reaching a scoped service means the singleton would capture it for the container's life. Each
		// node carries the index of the dependency that referenced it, so the diagnostic can name the
		// service alias the developer actually wrote rather than an arbitrary one of its service types.
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

		// The service key the parent's constructor used to reach this dependency - the alias the developer
		// wrote, including any [FromKey] - which is the one of the dependency's service keys that a parent
		// parameter selects. Falls back to the first service key if no parameter matches, or - when the
		// dependency is a collection member reached only through the collection and so exposes no service of
		// its own - to its implementation type, so the diagnostic still names it.
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
	///     Reports <see cref="Diagnostics.EagerAsyncSingleton">AWT161</see> for an <c>Eager</c> singleton that is
	///     async-tainted: it is async-initialized (or reaches one through its non-deferred dependencies), so it
	///     has no synchronous construction path and cannot be built in the generated root's synchronous
	///     constructor without handing back an uninitialized instance. Called only in the strict default -
	///     <c>InitializeAsync</c> warms the async singletons instead; the pragmatic <c>SyncResolveAfterInit</c>
	///     mode emits a blocking synchronous resolver, so eager construction is allowed and this is not reported.
	///     Missing-dependency, cycle and captive faults through an eager singleton are already covered by
	///     AWT101/102/105.
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
	///     cycle that involves a deferred edge escapes AWT102 (which walks only the construction graph, from which
	///     deferred edges are absent), so it must be vetted here. The verdict is computed per strongly connected
	///     component of the combined construction-plus-deferred graph rather than per DFS-enumerated cycle: a DFS
	///     enumerates only some of a component's cycles (a cycle closing through an already-finished node is never
	///     seen), and unlike AWT102 - where any one found cycle suffices to reject - a component here can mix
	///     supported and faulty cycles, so a complete verdict must come from the component's whole edge set (every
	///     intra-component edge lies on some cycle). See <see cref="ClassifyDeferredComponent" /> for the faults.
	/// </summary>
	private static void DetectNonTerminatingDeferredCycles(
		List<InstanceModel> instances,
		Dictionary<int, List<int>> constructionEdges,
		Dictionary<int, List<int>> combinedEdges,
		LocationInfo? containerLocation,
		List<DiagnosticInfo> diagnostics)
	{
		// Without a deferred member the combined graph IS the construction graph and every cycle in it is
		// AWT102's business - the (common) deferred-free container pays one member scan and no graph walk.
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

	// Classifies one non-trivial strongly connected component of the combined construction-plus-deferred graph
	// and reports its fault, if any (one diagnostic per component, rendering a concrete demonstrating cycle):
	// - A construction edge whose source is cached (singleton/scoped) re-enters that source before it is cached
	//   when resolution flows around the cycle, constructing a duplicate of it (AWT147).
	// - With no cached participant at all nothing terminates the re-entry, so the cycle recurses forever: AWT147
	//   when a construction edge remains in the mix, AWT145 for the all-deferred all-transient cycle.
	// - An async-tainted participant publishes its memoized task only after the re-entrant resolve has already
	//   returned, so a deferred cycle through it cannot terminate either (AWT146).
	// What remains - every construction edge sourced at a transient, no async participant, and at least one
	// synchronously-cached participant whose cache terminates the re-entry - is the supported case: the cycle
	// terminates from every entry point (transients are merely rebuilt a bounded number of times), so nothing is
	// reported. A component with no deferred edge is a pure construction cycle and is left to AWT102; a component
	// whose construction edges already close a cycle on their own is also left to AWT102 (the verdicts above
	// assume every cycle through a construction edge also traverses a deferred edge), and is re-vetted here once
	// the developer has broken that pure construction cycle.
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

		// The construction edges alone already close a cycle, which AWT102 reports. The verdicts below assume
		// every cycle through a construction edge also traverses a deferred edge, so defer to AWT102 here rather
		// than stacking a second, possibly-spurious deferred verdict onto the same component.
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

	// The intra-component edges that drive the classification: the first <see cref="ComponentEdges.Deferred" />
	// edge (its presence alone marks the component as a deferred cycle), the first <see cref="ComponentEdges.Construction" />
	// edge, and the first construction edge whose source is cached (a singleton/scoped re-entered before it is
	// cached - the duplicating case, <see cref="ComponentEdges.CachedSourceConstruction" />). Any may be absent.
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

	// Scans the component once for the two participant facts the classification needs: whether any participant is
	// synchronously cached (a non-transient lifetime, which can terminate the re-entry) and the first async-tainted
	// participant (-1 when none), whose memoized task publishes too late for a deferred cycle to terminate through.
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

	// Whether the construction edges alone close a cycle within the component (a visited/on-stack DFS restricted
	// to the component's nodes).
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

	// A concrete cycle through the intra-component edge source -> target, for a diagnostic message: the edge
	// followed by the shortest path (over the component's edges) from the target back to the source, rendered
	// head-first with the head repeated at the tail ("A -> B -> A"). The path exists because source and target
	// share a strongly connected component.
	private static List<int> CycleThroughEdge(int source, int target, HashSet<int> members, Dictionary<int, List<int>> edges)
	{
		List<int> cycle = new() { source, };
		cycle.AddRange(PathBetween(target, source, members, edges));
		return cycle;
	}

	// The shortest path from one component node to another (inclusive on both ends) over the component's edges,
	// by breadth-first search; a from == to path is the single node.
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

	// Strip every 'global::' alias (the leading one and any nested in generic type arguments) so
	// diagnostics read 'System.Func<MyCode.Leaf>' rather than 'System.Func<global::MyCode.Leaf>'.
	internal static string Display(string fullyQualified)
		=> fullyQualified.Replace("global::", string.Empty);

	// Renders an instance identity for diagnostics. A decorator chain link carries a synthetic
	// '<type>@__dec:…' identity (see DecoratorIdentity); trim the synthetic suffix so an error names the real
	// decorator type ('MyCode.Deco') rather than the internal key ('MyCode.Deco@__dec:MyCode.IService:0:1').
	internal static string DisplayInstance(string implementationType)
	{
		int marker = implementationType.IndexOf("@" + DecoratorKeyPrefix, StringComparison.Ordinal);
		return Display(marker >= 0 ? implementationType.Substring(0, marker) : implementationType);
	}
}
