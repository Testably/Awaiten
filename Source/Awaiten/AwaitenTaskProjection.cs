using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     Infrastructure for the <c>Awaiten.Extensions.DependencyInjection</c> bridge, referenced by
///     generated container code. Projects the container's <c>Task&lt;object&gt;</c> <c>ResolveAsync</c>
///     result to the strongly-typed <c>Task&lt;T&gt;</c> a host asks for, without reflection: the generated
///     registration metadata carries a delegate bound to the closed <see cref="AsTask{T}" /> for each async
///     service type, so the bridge never constructs the generic method at runtime and the projection is
///     native-AOT compatible for reference-type and value-type service types alike.
/// </summary>
public static class AwaitenTaskProjection
{
	/// <summary>
	///     Awaits the container's <paramref name="resolution" /> and returns it as a <c>Task&lt;T&gt;</c> (boxed
	///     as <see cref="object" />), so a host that asked for <c>Task&lt;T&gt;</c> receives a task of the right
	///     type.
	/// </summary>
	/// <typeparam name="T">The resolved service type.</typeparam>
	/// <param name="resolution">The container's asynchronous resolution.</param>
	public static async Task<T> AsTask<T>(Task<object> resolution) => (T)await resolution.ConfigureAwait(false);
}
