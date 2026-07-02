using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed partial class BridgeTests
{
	public interface IService;

	public sealed class SingletonService : IService;

	public sealed class ScopedService;

	public sealed class TransientService;

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
	[Scoped<ScopedService>]
	[Scoped<DisposableScoped>]
	[Transient<TransientService>]
	public static partial class BridgeContainer;

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
}
