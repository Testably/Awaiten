using System.Collections.Generic;

namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     The resolved object graph of a container: the built <see cref="Instances" />, the direct-dependency
///     <see cref="Dependencies" /> edges between them (by instance index), the wider
///     <see cref="ConstructionDependencies" /> edges that additionally include the bare eager relationships
///     (<c>Owned&lt;T&gt;</c> / <c>Task&lt;T&gt;</c>) used for cycle detection, the
///     <see cref="ServiceToImpl" /> / <see cref="ImplToIndex" /> lookups that map a service key (service type
///     plus optional resolution key) to its implementation and an implementation to its instance index, and
///     each instance's source <see cref="InstanceLocations" />. Produced by <c>AwaitenGenerator.BuildGraph</c>
///     and shared by the generator (which emits the container from it) and <c>AwaitenAnalyzer</c> (which walks
///     it for AWT118).
/// </summary>
internal sealed record GraphModel(
	List<InstanceModel> Instances,
	Dictionary<int, List<int>> Dependencies,
	Dictionary<int, List<int>> ConstructionDependencies,
	Dictionary<ServiceKey, string> ServiceToImpl,
	Dictionary<string, int> ImplToIndex,
	List<LocationInfo?> InstanceLocations);
