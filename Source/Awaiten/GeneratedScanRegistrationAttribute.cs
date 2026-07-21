using System;
using System.ComponentModel;

namespace Awaiten;

// S2326: the type parameter is the source generator's input. It reads the service type from the attribute's
// type argument via Roslyn, so the body never references it.
#pragma warning disable S2326

/// <summary>
///     Emitted by the source generator onto a <c>[Module]</c> that self-compiles a <c>[Scan]</c> - one per
///     match - and read back by a consuming container. It carries the accessible exposure interface
///     (<typeparamref name="TService" />), the name of the generated <c>public static</c> factory that
///     constructs the (possibly <c>internal</c>) match and returns that interface, and the lifetime the scan
///     declared. A container reads these like a container <c>[Scan]</c>'s matches - collection-eligible and
///     overridable by an explicit registration - so a self-compiled scan behaves as close to a container scan
///     as the assembly boundary allows.
/// </summary>
/// <typeparam name="TService">The accessible interface the match is exposed and resolved under.</typeparam>
/// <remarks>Generated code, not intended to be written by hand.</remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedScanRegistrationAttribute<TService> : Attribute
	where TService : class
{
	/// <summary>Registers one scan match produced by <paramref name="factory" />.</summary>
	/// <param name="factory">
	///     The name of the generated <c>static</c> factory method on the module that constructs the match and
	///     returns <typeparamref name="TService" />; its parameters are resolved from the consumer's graph.
	/// </param>
	public GeneratedScanRegistrationAttribute(string factory)
	{
		Factory = factory;
	}

	/// <summary>The generated factory method that constructs the match.</summary>
	public string Factory { get; }

	/// <summary>The lifetime the scan declared. Defaults to <see cref="AwaitenLifetime.Transient" />, matching <c>[Scan]</c>.</summary>
	public AwaitenLifetime Lifetime { get; set; } = AwaitenLifetime.Transient;

	/// <summary>
	///     Whether the scan declared <c>SkipUnconstructable</c>: the consuming container drops the match with a
	///     warning (AWT141) instead of an error when a factory parameter is not satisfiable from its graph.
	/// </summary>
	public bool SkipUnconstructable { get; set; }

	/// <summary>
	///     The name of the generated <c>public static void</c> activation wrapper on the module, run once the match
	///     is constructed. The module resolves and closes its (possibly <c>internal</c>) <c>OnActivated</c> hook at
	///     its own build and emits this public wrapper so the consumer can run it without naming the internal hook;
	///     the wrapper's parameters after the instance resolve from the consumer's graph. Null when the scan
	///     declared no activation hook.
	/// </summary>
	public string? OnActivated { get; set; }

	/// <summary>
	///     The name of the generated <c>public static void</c> release wrapper on the module, run when the match's
	///     owner is disposed. The module counterpart of <see cref="OnActivated" /> for the <c>OnRelease</c> hook.
	///     Null when the scan declared no release hook.
	/// </summary>
	public string? OnRelease { get; set; }
}

#pragma warning restore S2326
