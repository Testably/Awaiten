namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     How a constructor parameter is satisfied from the graph. <see cref="Direct" /> resolves the
///     service itself; <see cref="Func" /> and <see cref="Lazy" /> are relationship types that defer
///     resolution behind a <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c> over the owning container or scope.
///     <see cref="Arg" /> is supplied at resolve time from a <c>Func&lt;TArg…, T&gt;</c> relationship
///     rather than from the graph. <see cref="Owned" /> resolves the service into a dedicated throwaway
///     scope and hands the caller an <c>Owned&lt;T&gt;</c> disposal handle (it defers like a relationship
///     type, so it contributes no graph edge). <see cref="CancellationToken" /> is an asynchronous factory
///     method's <c>System.Threading.CancellationToken</c> parameter, satisfied by forwarding the resolve-time
///     token (the async creator's) rather than from the graph - so, like <see cref="Arg" />, it contributes no
///     edge. A synchronous factory or a constructor has no ambient token, so its <c>CancellationToken</c> is an
///     ordinary <see cref="Direct" /> dependency instead.
/// </summary>
internal enum DependencyKind
{
	Direct,
	Func,
	Lazy,
	Arg,
	Owned,
	CancellationToken,
}