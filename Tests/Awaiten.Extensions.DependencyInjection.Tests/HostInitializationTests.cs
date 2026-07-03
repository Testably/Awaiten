using System.Linq;
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
		internal static int InitializeCount;

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

	public interface IClock
	{
		string Now { get; }
	}

	public sealed class FixedClock : IClock
	{
		public string Now => "noon";
	}

	public sealed class ExternalAsyncResource : IAsyncInitializable
	{
		internal static int InitializeCount;

		// Drawn from the host provider (not the Awaiten graph): warm-up resolves the container root directly,
		// so the hosted service must wire the external resolver before this singleton can be constructed.
		public ExternalAsyncResource([FromServices] IClock clock) => Clock = clock;

		public IClock Clock { get; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref InitializeCount);
			return Task.CompletedTask;
		}
	}

	[Container(SyncResolveAfterInit = true)]
	[Singleton<ExternalAsyncResource>]
	public static partial class ExternalHostContainer;

	[Fact]
	public async Task AddAwaitenInitialization_WithExternalDependency_WiresResolverBeforeWarming()
	{
		using IHost host = new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddSingleton<IClock>(new FixedClock());
				services.AddGeneratedContainer<ExternalHostContainer.Root>();
				services.AddAwaitenInitialization<ExternalHostContainer.Root>();
			})
			.Build();

		await That(ExternalAsyncResource.InitializeCount).IsEqualTo(0);

		// Without the hosted service wiring the external resolver, constructing the singleton's [FromServices]
		// dependency during warm-up would throw - the resolver is null until a bridged resolution wires it.
		await host.StartAsync(TestContext.Current.CancellationToken);

		await That(ExternalAsyncResource.InitializeCount).IsEqualTo(1);

		// The external dependency was resolved from the host provider, and the singleton is warm.
		ExternalAsyncResource resource = host.Services.GetRequiredService<ExternalAsyncResource>();
		await That(resource.Clock.Now).IsEqualTo("noon");
		await That(ExternalAsyncResource.InitializeCount).IsEqualTo(1);

		await host.StopAsync(TestContext.Current.CancellationToken);
	}

	[Container]
	[Singleton<AsyncResource>]
	public static partial class DoubleContainer;

	[Fact]
	public async Task AddAwaitenInitialization_CalledTwice_RegistersHostedServiceOnce()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<DoubleContainer.Root>();
		services.AddAwaitenInitialization<DoubleContainer.Root>();
		services.AddAwaitenInitialization<DoubleContainer.Root>();

		// TryAddEnumerable dedupes on (IHostedService, implementation type), so the second call is a no-op;
		// a bare service collection holds no other hosted services.
		int hostedServices = services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService));
		await That(hostedServices).IsEqualTo(1);
	}

	[Fact]
	public async Task AddAwaitenInitialization_WhenServicesIsNull_ShouldThrowArgumentNullException()
	{
		void Act() => AwaitenInitializationServiceCollectionExtensions.AddAwaitenInitialization<HostContainer.Root>(null!);

		await That(Act).Throws<ArgumentNullException>().WithParamName("services");
	}
}
