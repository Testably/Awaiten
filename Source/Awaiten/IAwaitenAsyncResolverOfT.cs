using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     A typed asynchronous resolution fast path: a container or scope implements
///     <see cref="IAwaitenAsyncResolver{T}" /> for each registered service type, so the generic
///     <c>ResolveAsync&lt;T&gt;</c> convenience (<see cref="AwaitenResolverExtensions" />) returns the typed task
///     directly instead of converting the <c>Task&lt;object&gt;</c> of the <see cref="System.Type" />-based
///     <see cref="IAwaitenAsyncResolver.ResolveAsync(System.Type, CancellationToken)" />. Relationship types and
///     unregistered services fall back to the <see cref="System.Type" />-based path.
/// </summary>
/// <typeparam name="T">The exact service type this resolver can produce.</typeparam>
#pragma warning disable S3246 // Invariance is required here, so the type parameter is intentionally not co/contravariant.
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IAwaitenAsyncResolver<T>
#pragma warning restore S3246
{
	/// <summary>
	///     Resolves the service of type <typeparamref name="T" /> asynchronously, awaiting any asynchronous
	///     initialization exactly like <see cref="IAwaitenAsyncResolver.ResolveAsync(System.Type, CancellationToken)" />.
	/// </summary>
	Task<T> ResolveAsync(CancellationToken cancellationToken);
}
