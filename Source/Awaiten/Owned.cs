using System;

namespace Awaiten;

/// <summary>
///     A disposal handle over a resolved <typeparamref name="T" />. Resolving a service as
///     <see cref="Owned{T}" /> (or through a <c>Func&lt;Owned&lt;T&gt;&gt;</c> factory) builds it in a
///     dedicated throwaway scope and transfers ownership to the caller: disposing the
///     <see cref="Owned{T}" /> disposes that scope, releasing the instance and everything built for it,
///     while shared singletons continue to live on the container. This is the leak-free way to obtain a
///     disposable transient on demand - the container never accumulates it on the root.
/// </summary>
/// <typeparam name="T">The resolved service type.</typeparam>
public readonly struct Owned<T> : IDisposable
{
	private readonly IAwaitenScope _scope;

	/// <summary>
	///     Initializes a new instance of the <see cref="Owned{T}" /> struct over <paramref name="value" />
	///     resolved from <paramref name="scope" />. Generated container code calls this; disposing the
	///     handle disposes <paramref name="scope" />.
	/// </summary>
	/// <param name="scope">The scope that owns <paramref name="value" /> and is disposed with this handle.</param>
	/// <param name="value">The resolved service instance.</param>
	public Owned(IAwaitenScope scope, T value)
	{
		_scope = scope;
		Value = value;
	}

	/// <summary>
	///     The resolved service instance. Valid until this handle is disposed.
	/// </summary>
	public T Value { get; }

	/// <summary>
	///     Disposes the scope backing this handle, releasing <see cref="Value" /> and everything built for
	///     it. Shared singletons are unaffected. Disposing more than once, or disposing a
	///     <see langword="default" /> handle, is a no-op.
	/// </summary>
	public void Dispose() => _scope?.Dispose();
}
