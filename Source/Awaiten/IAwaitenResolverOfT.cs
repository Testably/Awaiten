namespace Awaiten;

/// <summary>
///     A typed resolution fast path: a container or scope implements <see cref="IAwaitenResolver{T}" />
///     for each registered service type, so a caller with a compile-time type can resolve through a
///     JIT-specialized generic dispatch instead of the runtime <see cref="System.Type" />-keyed lookup on
///     <see cref="IAwaitenResolver" />. The generic <c>Resolve&lt;T&gt;</c> convenience
///     (<see cref="AwaitenResolverExtensions" />) tries this interface first and falls back to the
///     <see cref="System.Type" />-based path for relationship types and unregistered services.
/// </summary>
/// <typeparam name="T">The exact service type this resolver can produce.</typeparam>
public interface IAwaitenResolver<T>
{
	/// <summary>Resolves the service of type <typeparamref name="T" />.</summary>
	T Resolve();
}
