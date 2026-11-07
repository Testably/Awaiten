using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed partial class AsyncReleaseHookBridgeTests
{
	public enum BridgePath
	{
		Projection,
		HostFactory,
	}

	public sealed class Till
	{
		public bool Closed { get; set; }
	}

	[Container]
	[Scoped<Till>(OnRelease = nameof(Close))]
	public static partial class Shop
	{
		private static async Task Close(Till till)
		{
			await Task.Yield();
			till.Closed = true;
		}
	}

	private static ServiceProvider Build(BridgePath path)
	{
		ServiceCollection services = new();
		if (path == BridgePath.Projection)
		{
			services.AddAwaitenContainer<Shop.Root>();
			return services.BuildServiceProvider(validateScopes: true);
		}

		AwaitenServiceProviderFactory<Shop.Root> factory = new();
		return (ServiceProvider)factory.CreateServiceProvider(factory.CreateBuilder(services));
	}

	[Test]
	[Arguments(BridgePath.Projection)]
	[Arguments(BridgePath.HostFactory)]
	public async Task AnAsyncScopeTeardown_AwaitsTheHook(BridgePath path)
	{
		await using ServiceProvider provider = Build(path);
		AsyncServiceScope scope = provider.CreateAsyncScope();
		Till till = scope.ServiceProvider.GetRequiredService<Till>();

		await scope.DisposeAsync();

		await That(till.Closed).IsTrue()
			.Because("an asynchronous MS.DI scope teardown drains the aligned Awaiten scope asynchronously, awaiting its release hooks");
	}

	[Test]
	[Arguments(BridgePath.Projection)]
	[Arguments(BridgePath.HostFactory)]
	public async Task ASynchronousScopeTeardown_Throws_AndSkipsTheHook(BridgePath path)
	{
		await using ServiceProvider provider = Build(path);
		IServiceScope scope = provider.CreateScope();
		Till till = scope.ServiceProvider.GetRequiredService<Till>();

		await That(() => scope.Dispose()).Throws<InvalidOperationException>()
			.WithMessage("*OnRelease hook 'Close' of 'Awaiten.Extensions.DependencyInjection.Tests.AsyncReleaseHookBridgeTests.Till' is asynchronous*DisposeAsync*").AsWildcard()
			.Because("a synchronous teardown cannot await the release hook, as for an async-only disposable, and the guidance names the hook and its service");
		await That(till.Closed).IsFalse();
	}

	[Test]
	public async Task TheReplacementProvider_AwaitsTheHookOnAsyncScopeTeardown()
	{
		await using Shop.Root container = new();
		await using AwaitenServiceProvider provider = new(container, ownsContainer: false);
		IServiceScope scope = provider.CreateScope();
		Till till = scope.ServiceProvider.GetRequiredService<Till>();

		await ((IAsyncDisposable)scope).DisposeAsync();

		await That(till.Closed).IsTrue()
			.Because("the bridge scope surfaces IAsyncDisposable, so a host tearing it down asynchronously reaches the generated scope's DisposeAsync");
	}
}
