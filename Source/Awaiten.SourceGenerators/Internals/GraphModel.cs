using System.Collections.Generic;

namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     The resolved object graph of a container: the built <see cref="Instances" />, the direct-dependency
///     <see cref="Dependencies" /> edges between them (by instance index), the
///     <see cref="ServiceToImpl" /> / <see cref="ImplToIndex" /> lookups that map a service type to its
///     implementation and an implementation to its instance index, and each instance's source
///     <see cref="InstanceLocations" />. Produced by <c>AwaitenGenerator.BuildGraph</c> and shared by the
///     generator (which emits the container from it) and <c>AwaitenAnalyzer</c> (which walks it for AWT117).
/// </summary>
internal sealed record GraphModel(
	List<InstanceModel> Instances,
	Dictionary<int, List<int>> Dependencies,
	Dictionary<string, string> ServiceToImpl,
	Dictionary<string, int> ImplToIndex,
	List<LocationInfo?> InstanceLocations);
