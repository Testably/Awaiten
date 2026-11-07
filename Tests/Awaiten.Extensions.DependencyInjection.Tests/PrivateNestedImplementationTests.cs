using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     A container whose implementations are private nested types is projected into Microsoft.Extensions.DependencyInjection
///     like any other: the host sees the accessible services, and the container still owns and disposes the instances.
/// </summary>
public sealed partial class PrivateNestedImplementationTests
{
	[Test]
	public async Task Projection_ResolvesThePrivateImplementationsThroughTheirServices()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<Shop.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		IClock clock = provider.GetRequiredService<IClock>();

		await That(clock.GetType().Name).IsEqualTo("FakeClock");
		await That(provider.GetRequiredService<IClock>()).IsSameAs(clock);
		await That(provider.GetRequiredService<Barista>().Clock).IsSameAs(clock);
	}

	[Test]
	public async Task ServiceProvider_DisposesAPrivateScopedImplementationWithItsScope()
	{
		await using AwaitenServiceProvider provider = new(new Shop.Root());
		Tracker tracker = provider.GetRequiredService<Tracker>();

		using (IServiceScope scope = provider.CreateScope())
		{
			ILedger ledger = scope.ServiceProvider.GetRequiredService<ILedger>();
			await That(ledger.GetType().Name).IsEqualTo("FakeLedger");
			await That(scope.ServiceProvider.GetRequiredService<ILedger>()).IsSameAs(ledger);
		}

		await That(tracker.Events).IsEqualTo(["ledger disposed",])
			.Because("the container tracks the private scoped instance it built and disposes it once with the scope");
	}

	public interface IClock;

	public interface ILedger;

	public sealed class Tracker
	{
		public List<string> Events { get; } = new();
	}

	public sealed class Barista(IClock clock)
	{
		public IClock Clock { get; } = clock;
	}

	[Container]
	[Singleton<Tracker>]
	[Singleton<FakeClock, IClock>]
	[Scoped<FakeLedger, ILedger>]
	[Transient<Barista>]
	public static partial class Shop
	{
		private sealed class FakeClock : IClock;

		private sealed class FakeLedger(Tracker tracker) : ILedger, IDisposable
		{
			public void Dispose() => tracker.Events.Add("ledger disposed");
		}
	}
}
