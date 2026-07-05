namespace Awaiten.Tests.Support;

/// <summary>Marker interface in a referenced assembly, for cross-assembly <c>[Scan(InAssembliesOf = ...)]</c>.</summary>
public interface ICrossAssemblyPlugin;

/// <summary>A concrete plugin in the support assembly, discoverable by a cross-assembly scan.</summary>
public sealed class GammaPlugin : ICrossAssemblyPlugin;

/// <summary>A second concrete plugin in the support assembly, discoverable by a cross-assembly scan.</summary>
public sealed class DeltaPlugin : ICrossAssemblyPlugin;

/// <summary>An abstract type assignable to the marker; a scan must skip it.</summary>
public abstract class PluginBase : ICrossAssemblyPlugin;

/// <summary>Internal type assignable to the marker; a cross-assembly scan must skip it (not visible to the scanner).</summary>
internal sealed class InternalPlugin : ICrossAssemblyPlugin;

/// <summary>A generic type definition assignable to the marker; a scan must skip it (no closed form to construct).</summary>
public sealed class GenericPlugin<T> : ICrossAssemblyPlugin
{
	public T? Value { get; set; }
}

// S2326: TViewModel is the scan's input. A container scans for closed implementers of this open generic
// via [Scan(typeof(ICrossAssemblyView<>))], so the interface body never references it.
#pragma warning disable S2326

/// <summary>An unbound generic marker in a referenced assembly, for cross-assembly closed-types-of scanning.</summary>
public interface ICrossAssemblyView<TViewModel>;

#pragma warning restore S2326

/// <summary>A view-model the cross-assembly view closes the marker at.</summary>
public sealed class CrossAssemblyViewModelOne;

/// <summary>A second view-model the cross-assembly view closes the marker at.</summary>
public sealed class CrossAssemblyViewModelTwo;

/// <summary>A concrete view closing the cross-assembly marker, discoverable by a closed-types-of scan.</summary>
public sealed class CrossAssemblyViewOne : ICrossAssemblyView<CrossAssemblyViewModelOne>;

/// <summary>A second concrete view closing the cross-assembly marker at a different type argument.</summary>
public sealed class CrossAssemblyViewTwo : ICrossAssemblyView<CrossAssemblyViewModelTwo>;
