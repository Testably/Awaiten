using System.Threading;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     A domain interface the test containers name in their <c>[AsyncInitializer&lt;T&gt;]</c> convention, so a test
///     service is async-initialized without referencing Awaiten.
/// </summary>
public interface IInitializable
{
	Task InitializeAsync(CancellationToken cancellationToken);
}
