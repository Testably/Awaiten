using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed partial class HostInitializationTests
{
	public sealed class AsyncResource : IAsyncInitializable
	{
		// Counted statically so the test can observe initialization without resolving the singleton - a
		// synchronous resolve of a SyncResolveAfterInit service before warm-up would itself drive it.
		public static int InitializeCount;

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref InitializeCount);
			return Task.CompletedTask;
		}
	}

	// Pragmatic mode so the warmed async singleton can be resolved synchronously through MS.DI.
	[Container(SyncResolveAfterInit = true)]
	[Singleton<AsyncResource>]
	public static partial class HostContainer;

	[Fact]
	public async Task AddAwaitenInitialization_WarmsAsyncSingletonsOnStartup()
	{
		using IHost host = new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddGeneratedContainer<HostContainer.Root>();
				services.AddAwaitenInitialization<HostContainer.Root>();
			})
			.Build();

		await That(AsyncResource.InitializeCount).IsEqualTo(0);

		await host.StartAsync(TestContext.Current.CancellationToken);

		// The hosted service warmed the async singleton on startup, before any manual resolution.
		await That(AsyncResource.InitializeCount).IsEqualTo(1);

		// It is now resolvable synchronously through the relay (pragmatic mode) without re-initializing.
		AsyncResource resource = host.Services.GetRequiredService<AsyncResource>();
		await That(AsyncResource.InitializeCount).IsEqualTo(1);
		await That(resource).IsNotNull();

		await host.StopAsync(TestContext.Current.CancellationToken);
	}
}
