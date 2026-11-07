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
	///     The name of the generated <c>static</c> factory method that constructs the match and returns
	///     <typeparamref name="TService" />, relative to the module, so it starts with the generated nested
	///     <c>Internals</c> class; its parameters are resolved from the consumer's graph. For a match
	///     the module generates no factory for, the match's metadata name instead, which only a consumer that can
	///     construct the match registers.
	/// </param>
	public GeneratedScanRegistrationAttribute(string factory)
	{
		Factory = factory;
	}

	/// <summary>
	///     The generated factory method that constructs the match, or the match's metadata name when the module
	///     generates no factory for it.
	/// </summary>
	public string Factory { get; }

	/// <summary>The lifetime the scan declared. Defaults to <see cref="Lifetime.Transient" />, matching <c>[Scan]</c>.</summary>
	public Lifetime Lifetime { get; set; } = Lifetime.Transient;

	/// <summary>
	///     Whether the scan declared <c>SkipUnconstructable</c>: the consuming container drops the match with a
	///     warning (AWT141) instead of an error when a factory parameter is not satisfiable from its graph.
	/// </summary>
	public bool SkipUnconstructable { get; set; }

	/// <summary>
	///     The name of the generated <c>public static</c> activation wrapper, relative to the module like
	///     <see cref="Factory" />, run once the match
	///     is constructed. The module resolves and closes its (possibly <c>internal</c>) <c>OnActivated</c> hook at
	///     its own build and emits this public wrapper so the consumer can run it without naming the internal hook;
	///     the wrapper's parameters after the instance resolve from the consumer's graph. It returns what the hook
	///     returns, so the consumer awaits a <c>Task</c> or <c>ValueTask</c> one. When the module generates no wrapper
	///     for the hook, because an assembly it grants <c>InternalsVisibleTo</c> calls the hook directly, the name of
	///     the module's own hook instead. Null when the scan declared no activation hook.
	/// </summary>
	public string? OnActivated { get; set; }

	/// <summary>
	///     The name of the generated <c>public static</c> release wrapper, relative to the module like
	///     <see cref="Factory" />, run when the match's
	///     owner is disposed. The module counterpart of <see cref="OnActivated" /> for the <c>OnRelease</c> hook,
	///     including the module's own hook when it generates no wrapper. Null when the scan declared no release hook.
	/// </summary>
	public string? OnRelease { get; set; }

	/// <summary>
	///     The types of the module's <see cref="AsyncInitializerAttribute{T}" /> conventions the match is, which the
	///     consumer cannot read off <typeparamref name="TService" />, so it runs those conventions after construction.
	///     Null when no convention of the module matches.
	/// </summary>
	public Type[]? AsyncInitializers { get; set; }

	/// <summary>
	///     Whether the match implements <see cref="IDisposable" />, so the consumer owns its disposal like that of a
	///     constructed type.
	/// </summary>
	public bool Disposable { get; set; }

	/// <summary>
	///     Whether the match implements <c>IAsyncDisposable</c>, so the consumer owns its asynchronous disposal like
	///     that of a constructed type.
	/// </summary>
	public bool AsyncDisposable { get; set; }

	/// <summary>
	///     The metadata name of the match the generated factory constructs, so the consumer shares one instance between
	///     this match and any other registration or scan match of the same implementation, as a container in the
	///     module's own assembly does. Null when the module generates no factory for the match, since
	///     <see cref="Factory" /> names the match then.
	/// </summary>
	public string? Implementation { get; set; }
}

#pragma warning restore S2326
