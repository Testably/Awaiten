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
