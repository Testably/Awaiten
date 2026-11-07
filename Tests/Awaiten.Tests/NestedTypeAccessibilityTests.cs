using System.Collections.Generic;
using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     Private and protected nested types in a graph: the generated resolvers are internal members of the
///     container's <c>Root</c> and <c>Scope</c>, so they may name such a type only where C# allows it, and the
///     container must resolve every graph it accepts.
/// </summary>
public sealed partial class NestedTypeAccessibilityTests
{
	private interface IClock;

	private sealed class FakeClock : IClock;

	[Test]
	public async Task PrivateContainerBesidePrivateTestDouble_ResolvesIt()
	{
		using TestShop.Root shop = new();

		IClock clock = shop.Resolve<IClock>();

		await That(clock).IsExactly<FakeClock>()
			.Because("a container nested privately beside a private test double is no more visible than the double, so its resolvers may name it");
		await That(shop.Resolve<IClock>()).IsSameAs(clock);
	}

	[Test]
	public async Task PrivateServiceTypeOfSingleton_ResolvesSynchronouslyAndAsynchronously()
	{
		using PrivateServiceShop.Root shop = new();

		object clock = PrivateServiceShop.ResolveClock(shop);
		object asyncClock = await PrivateServiceShop.ResolveClockAsync(shop);
		object warm = await PrivateServiceShop.ResolveWarmAsync(shop);

		await That(clock).IsExactly<PrivateServiceShop.SystemClock>();
		await That(asyncClock).IsSameAs(clock)
			.Because("the typed async path of a private service resolves the same singleton without caching its task on the root");
		await That(warm).IsExactly<PrivateServiceShop.WarmClock>();
		await That(((PrivateServiceShop.WarmClock)warm).Initialized).IsTrue();
	}

	[Test]
	public async Task PrivateServiceTypeOfAsyncCollection_ResolvesEveryMember()
	{
		using PrivateServiceShop.Root shop = new();
		await using PrivateServiceShop.Scope scope = shop.CreateScope();

		List<object> members = await PrivateServiceShop.ResolveAllAsync(scope);

		await That(members).HasCount(2)
			.Because("the async collection resolver of a private service type is a private member, so it may name the service");
		await That(members[1]).IsExactly<PrivateServiceShop.WarmLedger>();
		await That(((PrivateServiceShop.WarmLedger)members[1]).Initialized).IsTrue();
	}

	[Test]
	public async Task PrivateServiceTypeInRelationships_ResolvesThem()
	{
		using PrivateServiceShop.Root shop = new();
		using PrivateServiceShop.Scope scope = shop.CreateScope();

		List<object> members = PrivateServiceShop.ResolveAll(scope);
		object fromFactory = PrivateServiceShop.ResolveClockThroughFunc(scope);

		await That(members).HasCount(1)
			.Because("a synchronous collection of a private service type is resolved without exposing the service from an internal member");
		await That(fromFactory).IsSameAs(PrivateServiceShop.ResolveClock(shop))
			.Because("a Func of a private service type hands out the same singleton");
	}

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<FakeClock, IClock>]
	private static partial class TestShop;

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<SystemClock, IPrivateClock>]
	[Singleton<WarmClock, IPrivateWarmClock>]
	[Scoped<PlainLedger, IPrivateLedger>]
	[Scoped<WarmLedger, IPrivateLedger>]
	public static partial class PrivateServiceShop
	{
		private interface IPrivateClock;

		private interface IPrivateWarmClock;

		private interface IPrivateLedger;

		public sealed class SystemClock : IPrivateClock;

		public sealed class WarmClock : IPrivateWarmClock, IInitializable
		{
			public bool Initialized { get; private set; }

			public Task InitializeAsync(CancellationToken cancellationToken)
			{
				Initialized = true;
				return Task.CompletedTask;
			}
		}

		public sealed class PlainLedger : IPrivateLedger;

		public sealed class WarmLedger : IPrivateLedger, IInitializable
		{
			public bool Initialized { get; private set; }

			public Task InitializeAsync(CancellationToken cancellationToken)
			{
				Initialized = true;
				return Task.CompletedTask;
			}
		}

		public static object ResolveClock(Root shop) => shop.Resolve<IPrivateClock>();

		public static async Task<object> ResolveClockAsync(Root shop) => await shop.ResolveAsync<IPrivateClock>();

		public static async Task<object> ResolveWarmAsync(Root shop) => await shop.ResolveAsync<IPrivateWarmClock>();

		public static List<object> ResolveAll(Scope scope) => new(scope.Resolve<IEnumerable<IPrivateClock>>());

		public static object ResolveClockThroughFunc(Scope scope) => scope.Resolve<Func<IPrivateClock>>()();

		public static async Task<List<object>> ResolveAllAsync(Scope scope)
		{
			List<object> members = new();
			await foreach (IPrivateLedger ledger in await scope.ResolveAsync<IAsyncEnumerable<IPrivateLedger>>())
			{
				members.Add(ledger);
			}

			return members;
		}
	}
}
