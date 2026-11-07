namespace Awaiten.Benchmarks.Helpers;

[Container]
[AsyncInitializer<IWarmUp>(nameof(IWarmUp.WarmUpAsync))]
[Singleton<SyncSingleton>]
[Singleton<AsyncSingleton>]
[Scoped<SyncScoped>]
[Transient<SyncTransient>]
public static partial class AwaitenAsyncContainer;

public interface IWarmUp
{
	Task WarmUpAsync(CancellationToken cancellationToken);
}

public sealed class SyncSingleton;

public sealed class AsyncSingleton : IWarmUp
{
	public Task WarmUpAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SyncScoped;

public sealed class SyncTransient;
