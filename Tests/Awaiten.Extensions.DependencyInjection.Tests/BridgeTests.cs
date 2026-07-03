using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed partial class BridgeTests
{
	public interface IService;

	public sealed class SingletonService : IService;

	public sealed class ScopedService;

	public sealed class TransientService;

	// A transient consuming a scoped dependency: resolved inside an MS.DI scope it must share that scope's
	// instance, not the container root's.
	public sealed class TransientConsumer
	{
		public TransientConsumer(ScopedService scoped) => Scoped = scoped;

		public ScopedService Scoped { get; }
	}

	public sealed class AsyncService : IAsyncInitializable
	{
		public bool Initialized { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}
	}

	public sealed class DisposableSingleton : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	public sealed class DisposableScoped : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	[Container]
	[Singleton<SingletonService, IService>]
	[Singleton<DisposableSingleton>]
	[Singleton<AsyncService>]
	[Scoped<ScopedService>]
	[Scoped<DisposableScoped>]
	[Transient<TransientService>]
	[Transient<TransientConsumer>]
	public static partial class BridgeContainer;

	public sealed class SecondScopedService;

	[Container]
	[Scoped<SecondScopedService>]
	public static partial class SecondContainer;

	[Fact]
	public async Task AddGeneratedContainer_ResolvesSingletonThroughMsDi()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<BridgeContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		IService first = provider.GetRequiredService<IService>();
		IService second = provider.GetRequiredService<IService>();

		await That(first).Is<SingletonService>();
		await That(first).IsSameAs(second);
	}

	[Fact]
	public async Task AddGeneratedContainer_AlignsScopedToMsDiScope()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<BridgeContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		ScopedService outer1;
		ScopedService outer2;
		using (IServiceScope scope = provider.CreateScope())
		{
			outer1 = scope.ServiceProvider.GetRequiredService<ScopedService>();
			outer2 = scope.ServiceProvider.GetRequiredService<ScopedService>();
		}

		ScopedService inner;
		using (IServiceScope scope = provider.CreateScope())
		{
			inner = scope.ServiceProvider.GetRequiredService<ScopedService>();
		}

		await That(outer1).IsSameAs(outer2);
		await That(ReferenceEquals(outer1, inner)).IsFalse();
	}

	[Fact]
	public async Task AddGeneratedContainer_TransientIsFreshEachResolve()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<BridgeContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
		using IServiceScope scope = provider.CreateScope();

		TransientService first = scope.ServiceProvider.GetRequiredService<TransientService>();
		TransientService second = scope.ServiceProvider.GetRequiredService<TransientService>();

		await That(ReferenceEquals(first, second)).IsFalse();
	}

	[Fact]
	public async Task AddGeneratedContainer_DisposesScopedOnceWithMsDiScope()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<BridgeContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		DisposableScoped scoped;
		using (IServiceScope scope = provider.CreateScope())
		{
			scoped = scope.ServiceProvider.GetRequiredService<DisposableScoped>();
			await That(scoped.DisposeCount).IsEqualTo(0);
		}

		await That(scoped.DisposeCount).IsEqualTo(1);
	}

	[Fact]
	public async Task AddGeneratedContainer_DisposesSingletonOnceWithRootProvider()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<BridgeContainer.Root>();
		ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		DisposableSingleton singleton = provider.GetRequiredService<DisposableSingleton>();
		await That(singleton.DisposeCount).IsEqualTo(0);

		await provider.DisposeAsync();

		await That(singleton.DisposeCount).IsEqualTo(1);
	}

	[Fact]
	public async Task AwaitenServiceProvider_MapsGetServiceToTryResolve()
	{
		using BridgeContainer.Root container = new();
		using AwaitenServiceProvider provider = new(container);

		object? service = provider.GetService(typeof(IService));
		object? missing = provider.GetService(typeof(string));

		await That(service).Is<SingletonService>();
		await That(missing).IsNull();
	}

	[Fact]
	public async Task AwaitenServiceProvider_CreatesScopeOverAwaitenScope()
	{
		using BridgeContainer.Root container = new();
		using AwaitenServiceProvider provider = new(container);

		using IServiceScope scope = provider.CreateScope();
		ScopedService a = (ScopedService)scope.ServiceProvider.GetService(typeof(ScopedService))!;
		ScopedService b = (ScopedService)scope.ServiceProvider.GetService(typeof(ScopedService))!;

		await That(a).IsSameAs(b);
	}

	[Fact]
	public async Task AwaitenServiceProvider_ExposesItselfAndScopeFactory()
	{
		using BridgeContainer.Root container = new();
		using AwaitenServiceProvider provider = new(container);

		await That(provider.GetService(typeof(IServiceProvider))).IsSameAs(provider);
		await That(provider.GetService(typeof(IServiceScopeFactory))).IsSameAs(provider);
	}

	[Fact]
	public async Task AwaitenServiceProvider_ScopeExposesScopeFactory()
	{
		using BridgeContainer.Root container = new();
		using AwaitenServiceProvider provider = new(container);
		using IServiceScope scope = provider.CreateScope();

		// The scope's provider must itself be able to open nested scopes.
		IServiceScopeFactory factory =
			(IServiceScopeFactory)scope.ServiceProvider.GetService(typeof(IServiceScopeFactory))!;
		await That(factory).IsNotNull();

		using IServiceScope nested = factory.CreateScope();
		await That(nested.ServiceProvider.GetService(typeof(ScopedService))).IsNotNull();
	}

	[Fact]
	public async Task AddGeneratedContainer_ResolvesTransientFromRootProvider()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<BridgeContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		// Resolved directly from the root provider (not a scope), and fresh each time.
		TransientService first = provider.GetRequiredService<TransientService>();
		TransientService second = provider.GetRequiredService<TransientService>();

		await That(ReferenceEquals(first, second)).IsFalse();
	}

	[Fact]
	public async Task AddGeneratedContainer_ProjectsAsyncServiceAsTask()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<BridgeContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		// An async-initialized service has no synchronous path, so the bare type is not registered...
		await That(provider.GetService<AsyncService>()).IsNull();

		// ...only Task<T>, which resolves through ResolveAsync and awaits initialization.
		AsyncService resolved = await provider.GetRequiredService<Task<AsyncService>>();
		await That(resolved.Initialized).IsTrue();
	}

	[Fact]
	public async Task AwaitenServiceProviderFactory_BuildsProviderFromEmptyCollection()
	{
		AwaitenServiceProviderFactory<BridgeContainer.Root> factory = new();

		BridgeContainer.Root root = factory.CreateBuilder(new ServiceCollection());
		using ServiceProvider provider = (ServiceProvider)factory.CreateServiceProvider(root);

		await That(provider.GetService(typeof(IService))).Is<SingletonService>();
	}

	// A host service consuming an Awaiten service: the factory projects the container into the host's
	// collection, so cross-container constructor injection works.
	public sealed class HostConsumer
	{
		public HostConsumer(IService service) => Service = service;

		public IService Service { get; }
	}

	[Fact]
	public async Task AwaitenServiceProviderFactory_CoexistsWithHostRegistrations()
	{
		AwaitenServiceProviderFactory<BridgeContainer.Root> factory = new();
		ServiceCollection services = new();

		// A host (HostBuilder, WebApplicationBuilder) seeds the collection with its own registrations
		// before calling CreateBuilder; they must resolve side by side with the Awaiten services.
		services.AddSingleton("host-registration");
		services.AddSingleton<HostConsumer>();

		BridgeContainer.Root root = factory.CreateBuilder(services);
		using ServiceProvider provider = (ServiceProvider)factory.CreateServiceProvider(root);

		await That(provider.GetRequiredService<string>()).IsEqualTo("host-registration");
		await That(provider.GetRequiredService<IService>()).Is<SingletonService>();
		await That(provider.GetRequiredService<HostConsumer>().Service).Is<SingletonService>();
	}

	[Fact]
	public async Task AddGeneratedContainer_TransientSharesScopedDependenciesWithItsScope()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<BridgeContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
		using IServiceScope scope = provider.CreateScope();

		ScopedService scoped = scope.ServiceProvider.GetRequiredService<ScopedService>();
		TransientConsumer consumer = scope.ServiceProvider.GetRequiredService<TransientConsumer>();

		await That(consumer.Scoped).IsSameAs(scoped)
			.Because("a transient resolved inside an MS.DI scope resolves from the Awaiten scope aligned to it, so it shares that scope's instances");
	}

	[Fact]
	public async Task AddGeneratedContainer_TwoContainers_KeepSeparateScopeAlignment()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<BridgeContainer.Root>();
		services.AddGeneratedContainer<SecondContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
		using IServiceScope scope = provider.CreateScope();

		// Each bridged container aligns its own Awaiten scope to the MS.DI scope; the second registration
		// must not shadow the first container's alignment.
		await That(scope.ServiceProvider.GetRequiredService<ScopedService>()).IsNotNull();
		await That(scope.ServiceProvider.GetRequiredService<SecondScopedService>()).IsNotNull();
	}

	[Fact]
	public async Task AwaitenServiceProvider_ServesAsyncServiceAsTask()
	{
		using BridgeContainer.Root container = new();
		using AwaitenServiceProvider provider = new(container);

		// An async-initialized service has no synchronous path, so the bare type is not resolvable...
		await That(provider.GetService(typeof(AsyncService))).IsNull();

		// ...but the registration metadata advertises it, so Task<T> resolves through ResolveAsync.
		AsyncService resolved = await (Task<AsyncService>)provider.GetService(typeof(Task<AsyncService>))!;
		await That(resolved.Initialized).IsTrue();

		// A Task<T> over a type the container does not advertise stays unresolved.
		await That(provider.GetService(typeof(Task<string>))).IsNull();
	}

	[Fact]
	public async Task AwaitenServiceProvider_ScopeServesAsyncServiceAsTask()
	{
		using BridgeContainer.Root container = new();
		using AwaitenServiceProvider provider = new(container);
		using IServiceScope scope = provider.CreateScope();

		// The registration metadata flows into scope providers, so async services stay reachable there.
		AsyncService resolved = await (Task<AsyncService>)scope.ServiceProvider.GetService(typeof(Task<AsyncService>))!;
		await That(resolved.Initialized).IsTrue();
	}
}
