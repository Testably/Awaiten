using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     A root handed to <see cref="AwaitenServiceCollectionExtensions.AddAwaitenContainer{TRoot}(IServiceCollection, TRoot)" />
///     is projected as-is and stays owned by the caller, following MS.DI's convention for registered instances.
/// </summary>
public sealed partial class PrebuiltRootTests
{
	public sealed class InnerDependency : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	public sealed class OuterService
	{
		public OuterService(InnerDependency inner) => Inner = inner;

		public InnerDependency Inner { get; }
	}

	public sealed class ScopedResource : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	public sealed class ScopedConsumer
	{
		public ScopedConsumer(ScopedResource resource) => Resource = resource;

		public ScopedResource Resource { get; }
	}

	[Container]
	[Singleton<OuterService>]
	[Singleton<InnerDependency>]
	[Scoped<ScopedConsumer>]
	[Scoped<ScopedResource>]
	public static partial class OwnershipContainer;

	public interface IClock
	{
		string Now { get; }
	}

	public sealed class FixedClock : IClock
	{
		public FixedClock(string now) => Now = now;

		public string Now { get; }
	}

	public sealed class TimeReporter
	{
		public TimeReporter(IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	public sealed class WarmedReporter : IAsyncInitializable
	{
		public WarmedReporter(IClock clock) => Clock = clock;

		public IClock Clock { get; }

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	[Container]
	[ImportService<IClock>]
	[Singleton<TimeReporter>]
	public static partial class ExternalContainer;

	[Container(SyncResolveAfterInit = true)]
	[ImportService<IClock>]
	[Singleton<WarmedReporter>]
	public static partial class WarmedExternalContainer;

	public sealed class WarmedResource : IAsyncInitializable, IDisposable
	{
		public int DisposeCount { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public void Dispose() => DisposeCount++;
	}

	[Container]
	[Singleton<WarmedResource>]
	public static partial class WarmedContainer;

	private sealed class ClockResolver : IExternalResolver
	{
		private readonly IClock _clock;

		public ClockResolver(IClock clock) => _clock = clock;

		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
		{
			instance = serviceType == typeof(IClock) ? _clock : null;
			return instance is not null;
		}
	}

	[Test]
	public async Task AddAwaitenContainer_PrebuiltRoot_IsProjectedAsIs()
	{
		using OwnershipContainer.Root root = new();
		OuterService fromRoot = (OuterService)root.Resolve(typeof(OuterService));
		ServiceCollection services = new();
		services.AddAwaitenContainer(root);
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(provider.GetRequiredService<OwnershipContainer.Root>()).IsSameAs(root);
		await That(provider.GetRequiredService<OuterService>()).IsSameAs(fromRoot)
			.Because("the bridge resolves singletons from the root that was passed in, not from a fresh one");
	}

	[Test]
	public async Task AddAwaitenContainer_PrebuiltRoot_NotDisposedWithProvider()
	{
		using OwnershipContainer.Root root = new();
		ServiceCollection services = new();
		services.AddAwaitenContainer(root);
		ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
		InnerDependency inner = provider.GetRequiredService<OuterService>().Inner;

		provider.Dispose();

		await That(inner.DisposeCount).IsEqualTo(0)
			.Because("a root the caller passed in stays the caller's to dispose, as MS.DI does for registered instances");

		root.Dispose();

		await That(inner.DisposeCount).IsEqualTo(1);
	}

	[Test]
	public async Task AddAwaitenContainer_PrebuiltRoot_ScopedDisposedWithMsDiScope()
	{
		using OwnershipContainer.Root root = new();
		ServiceCollection services = new();
		services.AddAwaitenContainer(root);
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		ScopedResource resource;
		using (IServiceScope scope = provider.CreateScope())
		{
			resource = scope.ServiceProvider.GetRequiredService<ScopedConsumer>().Resource;
		}

		await That(resource.DisposeCount).IsEqualTo(1)
			.Because("the Awaiten scope aligned to the MS.DI scope is the bridge's own, whoever owns the root");
	}

	[Test]
	public async Task AddAwaitenContainer_PrebuiltRootWithExternalResolver_KeepsIt()
	{
		ClockResolver resolver = new(new FixedClock("custom"));
		using ExternalContainer.Root root = new(resolver);
		ServiceCollection services = new();
		services.AddSingleton<IClock>(new FixedClock("host"));
		services.AddAwaitenContainer(root);
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		TimeReporter reporter = provider.GetRequiredService<TimeReporter>();

		await That(reporter.Clock.Now).IsEqualTo("custom")
			.Because("the bridge wires the host's provider only when no resolver was set");
		await That(((IAwaitenRoot)root).ExternalResolver).IsSameAs(resolver);
	}

	[Test]
	public async Task WithInitialization_PrebuiltRootWithExternalResolver_KeepsIt()
	{
		ClockResolver resolver = new(new FixedClock("custom"));
		using WarmedExternalContainer.Root root = new(resolver);
		using IHost host = new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddSingleton<IClock>(new FixedClock("host"));
				services.AddAwaitenContainer(root).WithInitialization();
			})
			.Build();

		await host.StartAsync(TestContext.Current!.Execution.CancellationToken);
		WarmedReporter reporter = host.Services.GetRequiredService<WarmedReporter>();
		await host.StopAsync(TestContext.Current!.Execution.CancellationToken);

		await That(reporter.Clock.Now).IsEqualTo("custom")
			.Because("the warm-up wires the host's provider only when no resolver was set");
		await That(((IAwaitenRoot)root).ExternalResolver).IsSameAs(resolver);
	}

	[Test]
	public async Task WithInitialization_PrebuiltRoot_NotDisposedWithHost()
	{
		using WarmedContainer.Root root = new();
		IHost host = new HostBuilder()
			.ConfigureServices(services => services.AddAwaitenContainer(root).WithInitialization())
			.Build();
		await host.StartAsync(TestContext.Current!.Execution.CancellationToken);
		WarmedResource resource = (WarmedResource)await root.ResolveAsync(typeof(WarmedResource));
		await host.StopAsync(TestContext.Current!.Execution.CancellationToken);

		host.Dispose();

		await That(resource.DisposeCount).IsEqualTo(0)
			.Because("the warm-up makes the provider the root's disposal owner only for a root the bridge created");

		root.Dispose();

		await That(resource.DisposeCount).IsEqualTo(1);
	}
}
