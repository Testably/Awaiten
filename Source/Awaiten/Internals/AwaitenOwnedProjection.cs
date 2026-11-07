using System;
using System.ComponentModel;

namespace Awaiten.Internals;

/// <summary>
///     Infrastructure for generated code. Projects an <see cref="Owned{T}" /> handle to another value while it keeps
///     the scope of the original handle, so disposing the projected handle releases everything built for the original
///     one. A module's generated factory uses it to hand an <c>Owned&lt;T&gt;</c> of an internal service to the
///     constructor, from the <c>Owned</c> of the generated public type that holds the instance.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AwaitenOwnedProjection
{
	/// <summary>
	///     Returns a handle over <paramref name="map" /> applied to the value of <paramref name="owned" />, owning the same
	///     scope.
	/// </summary>
	/// <typeparam name="T">The type of the original value.</typeparam>
	/// <typeparam name="TResult">The type of the projected value.</typeparam>
	/// <param name="owned">The original handle.</param>
	/// <param name="map">The projection of the original value.</param>
	public static Owned<TResult> Map<T, TResult>(Owned<T> owned, Func<T, TResult> map) => owned.Map(map);
}
