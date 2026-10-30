namespace Awaiten.Benchmarks.Helpers;

[Container]
[Singleton<SyncSingleton>]
[Singleton<AsyncSingleton>]
[Scoped<SyncScoped>]
[Transient<SyncTransient>]
public static partial class AwaitenAsyncContainer;

public sealed class SyncSingleton;

public sealed class AsyncSingleton : IAsyncInitializable
{
	public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SyncScoped;

public sealed class SyncTransient;
