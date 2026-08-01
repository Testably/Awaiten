using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.AotSample.Domain;

/// <summary>
///     An async-initialized Awaiten service. Because it is <see cref="IAsyncInitializable" />, the bridge
///     has no synchronous resolution path and projects it as <c>Task&lt;Warmup&gt;</c>, building the closed
///     <c>Task&lt;T&gt;</c> and its converter from generator-emitted metadata so it publishes natively
///     without reflection.
/// </summary>
public sealed class Warmup : IAsyncInitializable
{
	public bool Ready { get; private set; }

	public Task InitializeAsync(CancellationToken cancellationToken)
	{
		Ready = true;
		return Task.CompletedTask;
	}
}
