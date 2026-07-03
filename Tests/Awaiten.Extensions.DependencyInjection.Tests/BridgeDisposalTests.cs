using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     Disposal behaviour of the bridge. For the provider-replacement path (<see cref="AwaitenServiceProvider" />)
///     the Awaiten container is the single owner:
///     every instance - and everything built for it - is disposed exactly once; these pin the two cases the
///     collection projection (<see cref="AwaitenServiceCollectionExtensions.AddGeneratedContainer{TRoot}" />)
///     documents it does not guarantee (an implementation exposed under several service types, and a
///     disposable reached only as a nested dependency). For the projection, these pin the ownership rules:
///     disposable transients are bounded by the MS.DI scope they are resolved in, instances awaited through
///     the <c>Task&lt;T&gt;</c> projection are disposed with their scope (or the root provider), and a
///     pre-built <c>Instance</c> member stays user-owned.
/// </summary>
public sealed partial class BridgeDisposalTests
{
	public interface IFirst;

	public interface ISecond;

	// One implementation exposed under two service types. Awaiten shares - and disposes - a single instance.
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

	// Reaches InnerDependency only as a constructor dependency - it is never resolved on its own.
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

	[Fact]
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

	[Fact]
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

	[Fact]
	public async Task Projection_DisposableTransient_ResolvesInScope_DisposedOnceWithScope()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<TransientContainer.Root>();
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

		await That(first.DisposeCount).IsEqualTo(1);
	}

	[Fact]
	public async Task Projection_DisposableTransient_FromRootProvider_SurfacesTheContainersGuidance()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<TransientContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		// Resolved from the root provider the transient resolves on the container root, where the strict
		// default withholds a disposable transient - each resolution would accumulate on the root for the
		// container's lifetime - so the container's guidance surfaces instead of a silent leak.
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

	[Fact]
	public async Task Projection_AsyncDisposableSingleton_DisposedOnceWithRootProvider()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<AsyncDisposalContainer.Root>();
		ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		// MS.DI captures only the Task<T> wrapper, so the bridge owns the awaited instance's disposal.
		AsyncDisposableSingleton singleton = await provider.GetRequiredService<Task<AsyncDisposableSingleton>>();
		await That(singleton.DisposeCount).IsEqualTo(0);

		await provider.DisposeAsync();

		await That(singleton.DisposeCount).IsEqualTo(1);
	}

	[Fact]
	public async Task Projection_AsyncDisposableScoped_DisposedOnceWithScope()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<AsyncDisposalContainer.Root>();
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

	[Fact]
	public async Task Projection_AwaitedInstance_DisposedBeforeItsSyncRelayedDependency()
	{
		OrderedDependency.DisposeOrder.Clear();
		ServiceCollection services = new();
		services.AddGeneratedContainer<OrderedDisposalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		using (IServiceScope scope = provider.CreateScope())
		{
			// The dependency is captured by MS.DI first; the awaited instance's disposal slot is captured
			// at the Task<T> resolution, so reverse-order teardown disposes the dependent first.
			scope.ServiceProvider.GetRequiredService<OrderedDependency>();
			await scope.ServiceProvider.GetRequiredService<Task<OrderedAsyncService>>();
		}

		await That(OrderedDependency.DisposeOrder.Count).IsEqualTo(2);
		await That(OrderedDependency.DisposeOrder[0]).IsEqualTo(nameof(OrderedAsyncService));
		await That(OrderedDependency.DisposeOrder[1]).IsEqualTo(nameof(OrderedDependency));
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

	[Fact]
	public async Task Projection_ExternallyOwnedInstance_NotDisposedByProvider()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<ExternalInstanceContainer.Root>();
		ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		ExternalProbe resolved = provider.GetRequiredService<ExternalProbe>();
		await That(resolved).IsSameAs(ExternalInstanceContainer.External);

		await provider.DisposeAsync();

		await That(resolved.DisposeCount).IsEqualTo(0)
			.Because("a pre-built Instance member is user-owned; neither the container nor MS.DI disposes it");
	}

#if NET
	// The generated async-disposal surface exists only where the Awaiten runtime exposes it (net8.0+);
	// net48 binds the netstandard2.0 asset, which has none, so an IAsyncDisposable-only service is not
	// supported there.
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

	[Fact]
	public async Task ProviderReplacement_AsyncOnlyDisposable_DisposedThroughAsyncScopeTeardown()
	{
		using AsyncOnlyDisposalContainer.Root container = new();
		using AwaitenServiceProvider provider = new(container, ownsContainer: false);

		IServiceScope scope = provider.CreateScope();
		AsyncOnlyDisposable service = (AsyncOnlyDisposable)scope.ServiceProvider.GetService(typeof(AsyncOnlyDisposable))!;

		// The bridge scope surfaces IAsyncDisposable, so a host tearing it down asynchronously reaches the
		// generated scope's DisposeAsync - the only way an IAsyncDisposable-only instance can be drained.
		await ((IAsyncDisposable)scope).DisposeAsync();

		await That(service.DisposeCount).IsEqualTo(1);
	}
#endif
}
