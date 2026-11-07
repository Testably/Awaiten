using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.AotSample.Domain;

/// <summary>A domain interface for services that warm up asynchronously before use.</summary>
public interface IWarmUp
{
	Task WarmUpAsync(CancellationToken cancellationToken);
}

/// <summary>
///     An async-initialized Awaiten service. Because the container's <c>[AsyncInitializer&lt;IWarmUp&gt;]</c>
///     convention initializes it, the bridge has no synchronous resolution path and projects it as
///     <c>Task&lt;Warmup&gt;</c>, building the closed <c>Task&lt;T&gt;</c> and its converter from generator-emitted
///     metadata so it publishes natively without reflection.
/// </summary>
public sealed class Warmup : IWarmUp
{
	public bool Ready { get; private set; }

	public Task WarmUpAsync(CancellationToken cancellationToken)
	{
		Ready = true;
		return Task.CompletedTask;
	}
}
