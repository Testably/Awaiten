namespace Awaiten.Tests.Support;

/// <summary>
///     A marker interface that lives in a referenced support assembly, so a container in the test assembly can
///     scan for its implementations across the assembly boundary via <c>[Scan(InAssembliesOf = ...)]</c>.
/// </summary>
public interface ICrossAssemblyPlugin;

/// <summary>A concrete plugin in the support assembly, discoverable by a cross-assembly scan.</summary>
public sealed class GammaPlugin : ICrossAssemblyPlugin;

/// <summary>A second concrete plugin in the support assembly, discoverable by a cross-assembly scan.</summary>
public sealed class DeltaPlugin : ICrossAssemblyPlugin;

/// <summary>An abstract type assignable to the marker; a scan must skip it.</summary>
public abstract class PluginBase : ICrossAssemblyPlugin;

/// <summary>An unbound generic marker in a referenced assembly, for cross-assembly closed-types-of scanning.</summary>
public interface ICrossAssemblyView<TViewModel>;

/// <summary>A view-model the cross-assembly view closes the marker at.</summary>
public sealed class CrossAssemblyViewModelOne;

/// <summary>A second view-model the cross-assembly view closes the marker at.</summary>
public sealed class CrossAssemblyViewModelTwo;

/// <summary>A concrete view closing the cross-assembly marker, discoverable by a closed-types-of scan.</summary>
public sealed class CrossAssemblyViewOne : ICrossAssemblyView<CrossAssemblyViewModelOne>;

/// <summary>A second concrete view closing the cross-assembly marker at a different type argument.</summary>
public sealed class CrossAssemblyViewTwo : ICrossAssemblyView<CrossAssemblyViewModelTwo>;
