using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     A domain interface the test containers and modules name in their <c>[AsyncInitializer&lt;T&gt;]</c> convention, so
///     a test service is async-initialized without referencing Awaiten.
/// </summary>
public interface IInitializable
{
	Task InitializeAsync(CancellationToken cancellationToken);
}
