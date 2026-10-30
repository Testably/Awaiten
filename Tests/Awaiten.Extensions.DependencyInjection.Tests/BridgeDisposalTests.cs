using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     Disposal behaviour of the bridge. On the provider-replacement path
///     (<see cref="AwaitenServiceProvider" />) the Awaiten container is the single owner: every instance,
///     and everything built for it, is disposed exactly once. On the collection projection
///     (<see cref="AwaitenServiceCollectionExtensions.AddAwaitenContainer{TRoot}(IServiceCollection)" />) and the host factory
///     (<see cref="AwaitenServiceProviderFactory{TRoot}" />) Awaiten still disposes everything it built exactly
///     once, nested dependencies included, with the MS.DI scope or root provider that owns it. MS.DI
///     additionally disposes each instance a projected factory handed out, once per descriptor, which these
///     tests pin as deliberate. A pre-built <c>Instance</c> member stays user-owned.
/// </summary>
public sealed partial class BridgeDisposalTests
{
	public enum BridgePath
	{
		Projection,
		HostFactory,
	}

	private static ServiceProvider Build<TRoot>(BridgePath path)
		where TRoot : class, IAwaitenRoot, new()
	{
		ServiceCollection services = new();
		if (path == BridgePath.Projection)
		{
			services.AddAwaitenContainer<TRoot>();
			return services.BuildServiceProvider(validateScopes: true);
		}

		AwaitenServiceProviderFactory<TRoot> factory = new();
		return (ServiceProvider)factory.CreateServiceProvider(factory.CreateBuilder(services));
	}

	[Container]
	[Singleton<OuterService>]
	[Singleton<InnerDependency>]
	public static partial class NestedSingletonContainer;

	[Test]
	[Arguments(BridgePath.Projection)]
	[Arguments(BridgePath.HostFactory)]
	public async Task Bridged_NestedScopedDependency_DisposedOnceWithMsDiScope(BridgePath path)
	{
		using ServiceProvider provider = Build<NestedContainer.Root>(path);

		InnerDependency inner;
		using (IServiceScope scope = provider.CreateScope())
		{
			inner = scope.ServiceProvider.GetRequiredService<OuterService>().Inner;
			await That(inner.DisposeCount).IsEqualTo(0);
		}

		await That(inner.DisposeCount).IsEqualTo(1)
			.Because("the Awaiten scope aligned to the MS.DI scope owns the nested dependency MS.DI never saw");
	}

	[Test]
	[Arguments(BridgePath.Projection)]
	[Arguments(BridgePath.HostFactory)]
	public async Task Bridged_NestedSingletonDependency_DisposedOnceWithRootProvider(BridgePath path)
	{
		ServiceProvider provider = Build<NestedSingletonContainer.Root>(path);

		InnerDependency inner = provider.GetRequiredService<OuterService>().Inner;
		using (IServiceScope scope = provider.CreateScope())
		{
			_ = scope.ServiceProvider.GetRequiredService<OuterService>();
		}

		await That(inner.DisposeCount).IsEqualTo(0)
			.Because("disposing an MS.DI scope leaves the singletons of the root alive");

		provider.Dispose();

		await That(inner.DisposeCount).IsEqualTo(1)
			.Because("the root provider owns the container root, which disposes the nested singleton");
	}

	[Test]
	[Arguments(BridgePath.Projection)]
	[Arguments(BridgePath.HostFactory)]
	public async Task Bridged_MultiServiceSingleton_DisposedOnceByAwaitenAndOncePerDescriptorByMsDi(BridgePath path)
	{
		ServiceProvider provider = Build<MultiServiceContainer.Root>(path);

		MultiService first = (MultiService)provider.GetRequiredService<IFirst>();
		MultiService second = (MultiService)provider.GetRequiredService<ISecond>();
		await That(first).IsSameAs(second);

		provider.Dispose();

		await That(first.DisposeCount).IsEqualTo(3)
			.Because("MS.DI disposes what each of the two factory descriptors returned, as it does for a plain factory relay, and the container root disposes its one instance once more");
	}

	public sealed class WarmedResource : IAsyncInitializable, IDisposable
	{
		public int DisposeCount { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public void Dispose() => DisposeCount++;
	}

	[Container]
	[Singleton<WarmedResource>]
	public static partial class WarmedContainer;

	[Test]
	public async Task HostFactory_RootWarmedDirectly_DisposedWithProvider()
	{
		ServiceProvider provider = Build<WarmedContainer.Root>(BridgePath.HostFactory);
		IAwaitenRoot root = provider.GetRequiredService<IAwaitenRoot>();
		await root.InitializeAsync(TestContext.Current!.Execution.CancellationToken);
		WarmedResource resource = (WarmedResource)await root.ResolveAsync(typeof(WarmedResource));

		provider.Dispose();

		await That(resource.DisposeCount).IsEqualTo(1)
			.Because("the host factory's provider owns the root even when only the root itself was used");
	}

	[Test]
	public async Task Initialization_WarmedSingleton_DisposedWithHost()
	{
		IHost host = new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddAwaitenContainer<WarmedContainer.Root>().WithInitialization();
			})
			.Build();
		await host.StartAsync(TestContext.Current!.Execution.CancellationToken);
		WarmedResource resource = (WarmedResource)await host.Services.GetRequiredService<WarmedContainer.Root>()
			.ResolveAsync(typeof(WarmedResource));
		await host.StopAsync(TestContext.Current!.Execution.CancellationToken);

		host.Dispose();

		await That(resource.DisposeCount).IsEqualTo(1)
			.Because("the warm-up built the singleton on the root, so the root provider must own the root although nothing was resolved through a bridged registration");
	}

	public sealed class AsyncOnlyConsumer
	{
		public AsyncOnlyConsumer(AsyncOnlyDisposable dependency) => Dependency = dependency;

		public AsyncOnlyDisposable Dependency { get; }
	}

	[Container]
	[Scoped<AsyncOnlyConsumer>]
	[Scoped<AsyncOnlyDisposable>]
	public static partial class NestedAsyncOnlyScopedContainer;

	[Container]
	[Singleton<AsyncOnlyConsumer>]
	[Singleton<AsyncOnlyDisposable>]
	public static partial class NestedAsyncOnlySingletonContainer;

	[Test]
	[Arguments(BridgePath.Projection)]
	[Arguments(BridgePath.HostFactory)]
	public async Task Bridged_NestedAsyncOnlyScopedDependency_DisposedOnceWithAsyncScopeTeardown(BridgePath path)
	{
		await using ServiceProvider provider = Build<NestedAsyncOnlyScopedContainer.Root>(path);

		AsyncServiceScope scope = provider.CreateAsyncScope();
		AsyncOnlyDisposable dependency = scope.ServiceProvider.GetRequiredService<AsyncOnlyConsumer>().Dependency;

		await scope.DisposeAsync();

		await That(dependency.DisposeCount).IsEqualTo(1)
			.Because("an asynchronous MS.DI scope teardown drains the aligned Awaiten scope asynchronously");
	}

	[Test]
	[Arguments(BridgePath.Projection)]
	[Arguments(BridgePath.HostFactory)]
	public async Task Bridged_NestedAsyncOnlySingletonDependency_DisposedOnceWithAsyncProviderTeardown(BridgePath path)
	{
		ServiceProvider provider = Build<NestedAsyncOnlySingletonContainer.Root>(path);

		AsyncOnlyDisposable dependency = provider.GetRequiredService<AsyncOnlyConsumer>().Dependency;

		await provider.DisposeAsync();

		await That(dependency.DisposeCount).IsEqualTo(1)
			.Because("an asynchronous root provider teardown drains the container root asynchronously");
	}

	public interface IFirst;

	public interface ISecond;

	// One implementation exposed under two service types. Awaiten shares and disposes a single instance.
	public sealed class MultiService : IFirst, ISecond, IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	public sealed class InnerDependency : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	// Reaches InnerDependency only as a constructor dependency, never resolved on its own.
	public sealed class OuterService
	{
		public OuterService(InnerDependency inner) => Inner = inner;

		public InnerDependency Inner { get; }
	}

	[Container]
	[Singleton<MultiService, IFirst>]
	[Singleton<MultiService, ISecond>]
	public static partial class MultiServiceContainer;

	[Container]
	[Scoped<OuterService>]
	[Scoped<InnerDependency>]
	public static partial class NestedContainer;

	[Test]
	public async Task ProviderReplacement_MultiServiceSingleton_DisposedExactlyOnce()
	{
		MultiServiceContainer.Root container = new();
		AwaitenServiceProvider provider = new(container);

		MultiService first = (MultiService)provider.GetService(typeof(IFirst))!;
		MultiService second = (MultiService)provider.GetService(typeof(ISecond))!;
		await That(first).IsSameAs(second)
			.Because("the two service types share one Awaiten singleton instance");

		provider.Dispose();

		await That(first.DisposeCount).IsEqualTo(1);
	}

	[Test]
	public async Task ProviderReplacement_NestedScopedDependency_DisposedWithScope()
	{
		using NestedContainer.Root container = new();
		using AwaitenServiceProvider provider = new(container, ownsContainer: false);

		InnerDependency inner;
		using (IServiceScope scope = provider.CreateScope())
		{
			OuterService outer = (OuterService)scope.ServiceProvider.GetService(typeof(OuterService))!;
			inner = outer.Inner;
			await That(inner.DisposeCount).IsEqualTo(0);
		}

		await That(inner.DisposeCount).IsEqualTo(1);
	}

	public sealed class DisposableTransient : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	[Container]
	[Transient<DisposableTransient>]
	public static partial class TransientContainer;

	[Test]
	public async Task Projection_DisposableTransient_ResolvesInScope_DisposedWithScope()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<TransientContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		DisposableTransient first;
		using (IServiceScope scope = provider.CreateScope())
		{
			// Under the strict default a disposable transient resolves inside a scope (the aligned Awaiten
			// scope bounds it), fresh per request, and MS.DI disposes it with the scope.
			first = scope.ServiceProvider.GetRequiredService<DisposableTransient>();
			DisposableTransient second = scope.ServiceProvider.GetRequiredService<DisposableTransient>();
			await That(ReferenceEquals(first, second)).IsFalse();
			await That(first.DisposeCount).IsEqualTo(0);
		}

		await That(first.DisposeCount).IsEqualTo(2)
			.Because("MS.DI disposes the instance its factory handed out, and the aligned Awaiten scope disposes what it built once more");
	}

	[Test]
	public async Task Projection_DisposableTransient_FromRootProvider_SurfacesTheContainersGuidance()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<TransientContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		// Resolved from the root provider, the transient resolves on the container root, where the strict
		// default withholds a disposable transient (each resolution would accumulate on the root for the
		// container's lifetime), so the container's guidance surfaces instead of a silent leak.
		void Act() => provider.GetRequiredService<DisposableTransient>();

		await That(Act).Throws<InvalidOperationException>();
	}

	public sealed class AsyncDisposableSingleton : IAsyncInitializable, IDisposable
	{
		public int DisposeCount { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public void Dispose() => DisposeCount++;
	}

	public sealed class AsyncDisposableScoped : IAsyncInitializable, IDisposable
	{
		public int DisposeCount { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public void Dispose() => DisposeCount++;
	}

	[Container]
	[Singleton<AsyncDisposableSingleton>]
	[Scoped<AsyncDisposableScoped>]
	public static partial class AsyncDisposalContainer;

	[Test]
	public async Task Projection_AsyncDisposableSingleton_DisposedOnceWithRootProvider()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<AsyncDisposalContainer.Root>();
		ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		AsyncDisposableSingleton singleton = await provider.GetRequiredService<Task<AsyncDisposableSingleton>>();
		await That(singleton.DisposeCount).IsEqualTo(0);

		await provider.DisposeAsync();

		await That(singleton.DisposeCount).IsEqualTo(1)
			.Because("MS.DI captures only the Task<T> wrapper, so the container root alone disposes the awaited instance");
	}

	[Test]
	public async Task Projection_AsyncDisposableScoped_DisposedOnceWithScope()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<AsyncDisposalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		AsyncDisposableScoped scoped;
		using (IServiceScope scope = provider.CreateScope())
		{
			scoped = await scope.ServiceProvider.GetRequiredService<Task<AsyncDisposableScoped>>();
			await That(scoped.DisposeCount).IsEqualTo(0);
		}

		await That(scoped.DisposeCount).IsEqualTo(1);
	}

	public sealed class OrderedDependency : IDisposable
	{
		public static List<string> DisposeOrder { get; } = new();

		public void Dispose() => DisposeOrder.Add(nameof(OrderedDependency));
	}

	// An async-initialized dependent of a sync-relayed disposable: on scope teardown it must be disposed
	// before the dependency it was built on.
	public sealed class OrderedAsyncService : IAsyncInitializable, IDisposable
	{
		public OrderedAsyncService(OrderedDependency dependency) => _ = dependency;

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public void Dispose() => OrderedDependency.DisposeOrder.Add(nameof(OrderedAsyncService));
	}

	[Container]
	[Scoped<OrderedAsyncService>]
	[Scoped<OrderedDependency>]
	public static partial class OrderedDisposalContainer;

	[Test]
	public async Task Projection_AwaitedInstance_DisposedByItsScopeBeforeItsDependency()
	{
		OrderedDependency.DisposeOrder.Clear();
		ServiceCollection services = new();
		services.AddAwaitenContainer<OrderedDisposalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		using (IServiceScope scope = provider.CreateScope())
		{
			scope.ServiceProvider.GetRequiredService<OrderedDependency>();
			await scope.ServiceProvider.GetRequiredService<Task<OrderedAsyncService>>();
		}

		await That(OrderedDependency.DisposeOrder).IsEqualTo([
				nameof(OrderedDependency),
				nameof(OrderedAsyncService),
				nameof(OrderedDependency),
			])
			.Because("MS.DI first disposes the dependency its factory handed out, then the aligned Awaiten scope disposes everything it built in reverse creation order, the awaited dependent before its dependency");
	}

	public sealed class ExternalProbe : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	[Container]
	[Singleton<ExternalProbe>(Instance = nameof(External))]
	public static partial class ExternalInstanceContainer
	{
		// A pre-built instance is a static member the container hands back but never owns or disposes.
		internal static readonly ExternalProbe External = new();
	}

	[Test]
	public async Task Projection_ExternallyOwnedInstance_NotDisposedByProvider()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<ExternalInstanceContainer.Root>();
		ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		ExternalProbe resolved = provider.GetRequiredService<ExternalProbe>();
		await That(resolved).IsSameAs(ExternalInstanceContainer.External);

		await provider.DisposeAsync();

		await That(resolved.DisposeCount).IsEqualTo(0)
			.Because("a pre-built Instance member is user-owned; neither the container nor MS.DI disposes it");
	}

	public sealed class AsyncOnlyDisposable : IAsyncDisposable
	{
		public int DisposeCount { get; private set; }

		public ValueTask DisposeAsync()
		{
			DisposeCount++;
			return default;
		}
	}

	[Container]
	[Scoped<AsyncOnlyDisposable>]
	public static partial class AsyncOnlyDisposalContainer;

	[Test]
	public async Task ProviderReplacement_AsyncOnlyDisposable_DisposedThroughAsyncScopeTeardown()
	{
		// The scoped AsyncOnlyDisposable only ever lives in the child scope, drained asynchronously below, so
		// the root never tracks one and its synchronous using is safe here (AWT156 checks what the disposed
		// owner could track, not what it actually tracked, and the root is itself a scope that could track one).
#pragma warning disable AWT156
		using AsyncOnlyDisposalContainer.Root container = new();
#pragma warning restore AWT156
		using AwaitenServiceProvider provider = new(container, ownsContainer: false);

		IServiceScope scope = provider.CreateScope();
		AsyncOnlyDisposable service = (AsyncOnlyDisposable)scope.ServiceProvider.GetService(typeof(AsyncOnlyDisposable))!;

		// The bridge scope surfaces IAsyncDisposable, so a host tearing it down asynchronously reaches the
		// generated scope's DisposeAsync, the only way an IAsyncDisposable-only instance can be drained.
		await ((IAsyncDisposable)scope).DisposeAsync();

		await That(service.DisposeCount).IsEqualTo(1);
	}
}
