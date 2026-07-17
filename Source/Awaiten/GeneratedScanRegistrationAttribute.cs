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
}

#pragma warning restore S2326
