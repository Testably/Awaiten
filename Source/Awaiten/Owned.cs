using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     A disposal handle over a resolved <typeparamref name="T" />. Resolving a service as <see cref="Owned{T}" />
///     (or through a <c>Func&lt;Owned&lt;T&gt;&gt;</c> factory) builds it in a dedicated throwaway scope and
///     transfers ownership to the caller. Disposing the handle disposes that scope, releasing the instance and
///     everything built for it, while shared singletons live on. This is the leak-free way to obtain a disposable
///     transient on demand: the container never accumulates it on the root.
/// </summary>
/// <typeparam name="T">The resolved service type.</typeparam>
[SuppressMessage("Awaiten", "AWT135:Service locator: a resolver interface is injected into a service", Justification = "Owned<T> is a framework disposal handle: it holds the throwaway scope it owns so it can dispose it, handed in by generated container code. It never resolves arbitrary services, so this is not a service-locator dependency.")]
public readonly struct Owned<T> :
#if NET || NETSTANDARD2_1_OR_GREATER
	IAsyncDisposable,
#endif
	IDisposable
{
	private readonly IAwaitenScope _scope;

	/// <summary>Wraps <paramref name="value" /> resolved from <paramref name="scope" />. Called by generated container code.</summary>
	/// <param name="scope">The scope that owns <paramref name="value" /> and is disposed with this handle.</param>
	/// <param name="value">The resolved service instance.</param>
	public Owned(IAwaitenScope scope, T value)
	{
		_scope = scope;
		Value = value;
	}

	/// <summary>The resolved service instance. Valid until this handle is disposed.</summary>
	public T Value { get; }

	/// <summary>
	///     Disposes the scope backing this handle, releasing <see cref="Value" /> and everything built for it.
	///     Shared singletons are unaffected. Disposing more than once, or disposing a <see langword="default" />
	///     handle, is a no-op.
	/// </summary>
	public void Dispose() => _scope?.Dispose();

#if NET || NETSTANDARD2_1_OR_GREATER
	/// <summary>
	///     Asynchronously disposes the scope backing this handle, awaiting the <c>DisposeAsync</c> of the instances
	///     it owns and falling back to <see cref="IDisposable.Dispose" /> for the rest. Use this through
	///     <c>await using</c> when <see cref="Value" /> (or anything built for it) is <see cref="IAsyncDisposable" />.
	///     A synchronous <see cref="Dispose" /> of such a handle throws. Disposing a <see langword="default" /> handle is a no-op.
	/// </summary>
	public ValueTask DisposeAsync()
		=> _scope is IAsyncDisposable asyncScope ? asyncScope.DisposeAsync() : default;
#endif
}
