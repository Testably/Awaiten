using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     The instance indices of every collection-resolvable service's members, keyed by the collection's
	///     (element service type, key), for the transitive-disposable walk. Composed from the graph's
	///     <paramref name="collections" /> and <paramref name="implToIndex" /> (a member absent from the latter
	///     failed to build and is skipped).
	/// </summary>
	internal static Dictionary<ServiceKey, List<int>> CollectionMemberIndices(
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

	/// <summary>
	///     Whether building the service at <paramref name="start" /> on its owner tracks a fresh disposable
	///     there: the service itself is disposable, or its construction transitively rebuilds one. The walk
	///     follows only <em>transient</em> dependency edges, because a scoped or singleton dependency is
	///     cached/shared (built at most once on the owner) and so is bounded, whereas a transient dependency is
	///     rebuilt - and, if disposable, re-tracked - on every construction. A collection (Enumerable) edge is
	///     followed too: it materializes its members eagerly into the owner, so a transient disposable member is
	///     rebuilt on every construction just like a direct transient dependency. Used to decide whether a plain
	///     <c>Func&lt;…&gt;</c> over the service accumulates on the container root (AWT118 / strict withholding):
	///     a non-disposable transient that injects a disposable transient leaks just the same when built
	///     repeatedly through a root-bound factory.
	/// </summary>
	internal static bool BuildsFreshDisposable(
		IReadOnlyList<InstanceModel> instances,
		Dictionary<ServiceKey, int> serviceToIndex,
		IReadOnlyDictionary<ServiceKey, List<int>> collectionMembers,
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

			PushFreshTransientDependencies(instance, instances, serviceToIndex, collectionMembers, stack);
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
		IReadOnlyDictionary<ServiceKey, List<int>> collectionMembers,
		Stack<int> stack)
	{
		foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
		{
			if (parameter.Kind is DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable)
			{
				PushTransientCollectionMembers(KeyOf(parameter), instances, collectionMembers, stack);
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
		Dictionary<ServiceKey, List<string>> serviceMembers)
		=> BuildEdges(instances, serviceToImpl, implToIndex, serviceMembers, includeEagerBare: false, includeDeferredMembers: true);

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
		Dictionary<ServiceKey, List<string>> serviceMembers)
		=> BuildEdges(instances, serviceToImpl, implToIndex, serviceMembers, includeEagerBare: true);

	// The deferred-edge graph over instance indices: the edges a deferred [Inject(Deferred = true)] member
	// contributes when its post-construction assignment runs. Deferred members are excluded from the cycle
	// (AWT102) and captive (AWT105) graphs - that exclusion is what lets a deferred property break a mutual
	// constructor cycle - so this is a dedicated graph walked only by AWT139/AWT140 (the taint graph also follows
	// them, since the assignment awaits an async target). A deferred Direct member edges to its target and a
	// deferred collection (Enumerable/AsyncEnumerable/AwaitedEnumerable) edges to each of its members, because
	// the assignment materializes them eagerly; a deferred Func<T>/Lazy<T> defers and contributes nothing. Uses
	// construction-graph semantics (includeEagerBare) because the assignment resolves its target during the
	// owner's construction (a scoped/singleton is already cached, but a transient is not, and an async task is
	// not yet published), so a cycle closed through such an edge cannot terminate.
	private static Dictionary<int, List<int>> BuildDeferredGraph(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers)
	{
		Dictionary<int, List<int>> edges = new();
		for (int i = 0; i < instances.Count; i++)
		{
			List<int> nodeEdges = new();
			foreach (MemberModel member in instances[i].InjectedMembers.AsArray())
			{
				if (member.Deferred)
				{
					AddParameterEdges(member.Dependency, serviceToImpl, implToIndex, serviceMembers, includeEagerBare: true, nodeEdges);
				}
			}

			edges[i] = nodeEdges;
		}

		return edges;
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
		bool includeEagerBare,
		bool includeDeferredMembers = false)
	{
		Dictionary<int, List<int>> edges = new();
		for (int i = 0; i < instances.Count; i++)
		{
			List<int> nodeEdges = new();
			foreach (ParameterModel parameter in instances[i].ConstructorParameters.AsArray())
			{
				AddParameterEdges(parameter, serviceToImpl, implToIndex, serviceMembers, includeEagerBare, nodeEdges);
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

				AddParameterEdges(member.Dependency, serviceToImpl, implToIndex, serviceMembers, includeEagerBare, nodeEdges);
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
		bool includeEagerBare,
		List<int> nodeEdges)
	{
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
	///     for the <c>Owned</c> handle to build into.)
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
				// Guard the implToIndex lookup: serviceToImpl can name an implementation whose BuildInstance
				// failed (so it is absent from implToIndex), and an unguarded indexer would crash the generator
				// (KeyNotFoundException) instead of surfacing the real registration error. Mirrors the guard in
				// BuildDependencyGraph / ValidateRuntimeArguments.
				if (parameter.Kind is not (DependencyKind.Func or DependencyKind.Lazy or DependencyKind.Owned)
				    || !serviceToImpl.TryGetValue(KeyOf(parameter), out string? targetImpl)
				    || !implToIndex.TryGetValue(targetImpl, out int target))
				{
					continue;
				}

				if (!instances[target].IsAsyncTainted)
				{
					continue;
				}

				// Point the diagnostic at the offending parameter; fall back to the consumer's registration.
				LocationInfo? location = parameter.Location ?? instanceLocations[i];
				if (instances[target].IsAsyncSource)
				{
					diagnostics.Add(new DiagnosticInfo(
						Diagnostics.SynchronousAsyncResolution,
						location,
						new EquatableArray<string>([
							DisplayInstance(instances[i].ImplementationType),
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
							DisplayInstance(instances[i].ImplementationType),
							path,
						])));
				}
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
		Dictionary<string, int> implToIndex,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		if (collections.Count == 0)
		{
			return;
		}

		Dictionary<ServiceKey, ServiceMembers> byService = new();
		foreach (ServiceMembers collection in collections)
		{
			byService[new ServiceKey(collection.Service, collection.Key)] = collection;
		}

		for (int i = 0; i < instances.Count; i++)
		{
			foreach (ParameterModel parameter in instances[i].ConstructorParameters.AsArray())
			{
				if (parameter.Kind == DependencyKind.Enumerable
				    && byService.TryGetValue(KeyOf(parameter), out ServiceMembers members))
				{
					ReportAsyncTaintedMembers(i, parameter, members, instances, implToIndex, instanceLocations, diagnostics);
				}
			}
		}
	}

	// Reports AWT122 for each async-tainted member of the collection <paramref name="consumer" /> injects
	// through <paramref name="parameter" /> (a member absent from implToIndex failed to build and is skipped).
	private static void ReportAsyncTaintedMembers(
		int consumer,
		ParameterModel parameter,
		ServiceMembers members,
		List<InstanceModel> instances,
		Dictionary<string, int> implToIndex,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		foreach (string member in members.Implementations.AsArray())
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
				if (parameter.Kind is DependencyKind.Arg or DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable
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

	// The union of the construction and deferred edge sets over instance indices: a cycle can close through a
	// construction edge on one hop and a deferred edge on another (a mixed cycle), which neither the
	// construction-only graph (AWT102) nor the deferred-only graph sees on its own. Walking the union surfaces
	// those. Construction edges come first, then the deferred edges not already present (a duplicate edge would
	// only add redundant DFS work, not change the result).
	private static Dictionary<int, List<int>> BuildCombinedGraph(
		int count,
		Dictionary<int, List<int>> constructionEdges,
		Dictionary<int, List<int>> deferredEdges)
	{
		Dictionary<int, List<int>> combined = new();
		for (int i = 0; i < count; i++)
		{
			List<int> union = new(constructionEdges[i]);
			union.AddRange(deferredEdges[i].Where(next => !union.Contains(next)));
			combined[i] = union;
		}

		return combined;
	}

	/// <summary>
	///     AWT139/AWT140/AWT141: a deferred property breaks a mutual cycle only because the owning instance is
	///     cached before its deferred members are wired, so a re-entrant resolve returns the cached instance. Any
	///     cycle that involves a deferred edge escapes AWT102 (which walks only the construction graph, from which
	///     deferred edges are absent), so it must be vetted here. This walks the combined construction-plus-deferred
	///     graph and, for each cycle that involves at least one deferred edge, reports the fault that prevents it
	///     from terminating: a construction-time edge still in the cycle leaves it only partly broken (AWT141); a
	///     cycle whose every participant is a transient is never cached anywhere, so each lap reconstructs the
	///     participants and it recurses forever (AWT139); an async-tainted participant publishes its memoized task
	///     only after the re-entrant resolve has already returned (AWT140). A pure construction cycle is left to
	///     AWT102, and an all-deferred synchronous cycle with at least one singleton/scoped participant is supported
	///     and not reported.
	/// </summary>
	private static void DetectNonTerminatingDeferredCycles(
		List<InstanceModel> instances,
		Dictionary<int, List<int>> constructionEdges,
		Dictionary<int, List<int>> deferredEdges,
		LocationInfo? containerLocation,
		List<DiagnosticInfo> diagnostics)
	{
		Dictionary<int, List<int>> combined = BuildCombinedGraph(instances.Count, constructionEdges, deferredEdges);

		HashSet<int> visited = new();
		HashSet<int> onStack = new();
		List<int> path = new();
		HashSet<string> reported = new(StringComparer.Ordinal);

		for (int i = 0; i < instances.Count; i++)
		{
			Visit(i);
		}

		void Visit(int node)
		{
			visited.Add(node);
			onStack.Add(node);
			path.Add(node);

			foreach (int next in combined[node])
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

			if (DeferredCycleFault(cycle, instances, constructionEdges) is not { } descriptor)
			{
				return;
			}

			// Dedupe on the set of nodes so the same cycle is not reported once per back-edge.
			string signature = string.Join("|", cycle.OrderBy(x => x));
			if (!reported.Add(signature))
			{
				return;
			}

			cycle.Add(cycleStart);
			string rendered = string.Join(" -> ", cycle.Select(index => DisplayInstance(instances[index].ImplementationType)));
			diagnostics.Add(new DiagnosticInfo(descriptor, containerLocation, new EquatableArray<string>([rendered,])));
		}
	}

	// The non-termination fault a cycle in the combined construction-plus-deferred graph exhibits, or null when it
	// is not this analysis's concern (a pure construction cycle - left to AWT102) or is the supported case (every
	// edge is deferred, no participant is async-tainted, and at least one participant is a synchronously-resolved
	// singleton or scoped that is cached before its deferred members are wired, so a re-entrant resolve returns the
	// cached instance and terminates the cycle). A cycle hop is a construction edge when the target is in the
	// construction graph, otherwise it is a deferred edge (the combined walk follows only construction or deferred
	// edges). A cycle that still traverses a construction edge is only partly broken and cannot terminate from every
	// entry point regardless of lifetime (AWT141); an all-deferred cycle whose every participant is a transient is
	// never cached anywhere (AWT139); an all-deferred cycle through an async-tainted participant publishes its
	// memoized task only after the re-entrant resolve has returned (AWT140).
	private static DiagnosticDescriptor? DeferredCycleFault(
		List<int> cycle,
		List<InstanceModel> instances,
		Dictionary<int, List<int>> constructionEdges)
	{
		bool hasDeferredHop = false;
		bool hasConstructionHop = false;
		for (int i = 0; i < cycle.Count; i++)
		{
			int owner = cycle[i];
			int target = cycle[(i + 1) % cycle.Count];
			if (constructionEdges[owner].Contains(target))
			{
				hasConstructionHop = true;
			}
			else
			{
				hasDeferredHop = true;
			}
		}

		// A cycle with no deferred edge is a pure construction cycle, which AWT102 already reports; ignore it here
		// so it is not reported twice.
		if (!hasDeferredHop)
		{
			return null;
		}

		// A cycle that still traverses a construction edge is only partly broken: the deferred property terminates
		// its own hop, but the remaining construction edge re-enters an as-yet-uncached participant when resolution
		// begins at that participant.
		if (hasConstructionHop)
		{
			return Diagnostics.DeferredMixedCycle;
		}

		// Only when *every* participant is a transient is nothing cached anywhere in the cycle, so it recurses
		// forever. A single cached (singleton/scoped) participant breaks the recursion: on the second lap its
		// re-entrant resolve returns the already-cached instance instead of continuing round, so the cycle
		// terminates from every entry point (the transients are merely rebuilt a bounded number of times).
		if (cycle.All(index => instances[index].Lifetime == Lifetime.Transient))
		{
			return Diagnostics.DeferredTransientCycle;
		}

		if (cycle.Any(index => instances[index].IsAsyncTainted))
		{
			return Diagnostics.DeferredAsyncCycle;
		}

		return null;
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
