using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of keyed registration: implementations share one service type under different keys,
///     a consumer selects one with <c>[FromKey]</c>, and an unkeyed resolution returns the unkeyed registration.
/// </summary>
public partial class KeyedTests
{
	[Fact]
	public async Task FromKey_SelectsTheImplementationRegisteredUnderThatKey()
	{
		using KeyedContainer.Root container = new();

		Router router = container.Resolve<Router>();

		await That(router.Primary).Is<FastChannel>()
			.Because("the [FromKey(\"fast\")] parameter resolves the implementation keyed 'fast'");
		await That(router.Backup).Is<SlowChannel>()
			.Because("the [FromKey(\"slow\")] parameter resolves the implementation keyed 'slow'");
	}

	[Fact]
	public async Task KeyedRegistrations_ShareOneServiceTypeWithoutColliding()
	{
		using KeyedContainer.Root container = new();

		Router router = container.Resolve<Router>();

		await That(router.Primary).IsNotSameAs(router.Backup)
			.Because("the two keys resolve to distinct implementations of the one service type");
	}

	[Fact]
	public async Task UnkeyedResolution_ReturnsTheUnkeyedRegistration()
	{
		using MixedContainer.Root container = new();

		IClock clock = container.Resolve<IClock>();
		Consumer consumer = container.Resolve<Consumer>();

		await That(clock).Is<DefaultClock>()
			.Because("an unkeyed resolution returns the unkeyed registration, not a keyed one");
		await That(consumer.Clock).Is<FastChannelClock>()
			.Because("the keyed consumer still selects the keyed implementation");
	}

	[Fact]
	public async Task FromKey_SelectsTheKeyedImplementationThroughFuncAndLazyRelationships()
	{
		using KeyedContainer.Root container = new();

		DeferredRouter router = container.Resolve<DeferredRouter>();

		await That(router.Primary()).Is<FastChannel>()
			.Because("a [FromKey(\"fast\")] Func<IChannel> defers to the implementation keyed 'fast'");
		await That(router.Backup.Value).Is<SlowChannel>()
			.Because("a [FromKey(\"slow\")] Lazy<IChannel> defers to the implementation keyed 'slow'");
	}

	[Fact]
	public async Task ResolveWithKey_ReturnsTheImplementationRegisteredUnderThatKey()
	{
		using KeyedContainer.Root container = new();

		await That(container.Resolve<IChannel>("fast")).Is<FastChannel>()
			.Because("Resolve<T>(\"fast\") returns the registration keyed 'fast'");
		await That(container.Resolve<IChannel>("slow")).Is<SlowChannel>()
			.Because("Resolve<T>(\"slow\") returns the registration keyed 'slow'");
	}

	[Fact]
	public async Task ResolveWithKey_ForAnUnknownKey_Throws()
	{
		using KeyedContainer.Root container = new();

		await That(() => container.Resolve<IChannel>("unknown")).Throws<InvalidOperationException>()
			.Because("no registration is keyed 'unknown'");
	}

	[Fact]
	public async Task TryResolveWithKey_ReportsHitAndMiss()
	{
		using KeyedContainer.Root container = new();

		await That(container.TryResolve<IChannel>("fast", out IChannel? hit)).IsTrue();
		await That(hit).Is<FastChannel>();

		await That(container.TryResolve<IChannel>("unknown", out IChannel? miss)).IsFalse();
		await That(miss).IsNull();
	}

	[Fact]
	public async Task ResolveWithKey_AndUnkeyed_SelectTheirRespectiveRegistrations()
	{
		using MixedContainer.Root container = new();

		await That(container.Resolve<IClock>()).Is<DefaultClock>()
			.Because("an unkeyed resolution returns the unkeyed registration");
		await That(container.Resolve<IClock>("fast")).Is<FastChannelClock>()
			.Because("the keyed resolution returns the registration keyed 'fast'");
		await That(container.TryResolve<IClock>("fast", out IClock? keyed)).IsTrue();
		await That(keyed).Is<FastChannelClock>();
	}

	[Fact]
	public async Task ResolveAsyncWithKey_ReturnsTheKeyedRegistration()
	{
		using KeyedContainer.Root container = new();

		IChannel channel = await container.ResolveAsync<IChannel>("fast", TestContext.Current.CancellationToken);

		await That(channel).Is<FastChannel>()
			.Because("ResolveAsync<T>(\"fast\") returns the registration keyed 'fast' even when it needs no async init");
	}

	[Fact]
	public async Task Singleton_CapturesKeyedNonSingletonDependency_ThroughFuncAndLazy()
	{
		using CapturingContainer.Root container = new();

		Capturer capturer = container.Resolve<Capturer>();

		await That(capturer.Scoped()).Is<ScopedFast>()
			.Because("the singleton's [FromKey(\"fast\")] Func<IWork> selects the keyed scoped registration");
		await That(capturer.Transient.Value).Is<TransientSlow>()
			.Because("the singleton's [FromKey(\"slow\")] Lazy<IWork> selects the keyed transient registration");
	}

	public interface IChannel;

	public sealed class FastChannel : IChannel;

	public sealed class SlowChannel : IChannel;

	public sealed class Router
	{
		public Router([FromKey("fast")] IChannel primary, [FromKey("slow")] IChannel backup)
		{
			Primary = primary;
			Backup = backup;
		}

		public IChannel Primary { get; }

		public IChannel Backup { get; }
	}

	public sealed class DeferredRouter
	{
		public DeferredRouter([FromKey("fast")] Func<IChannel> primary, [FromKey("slow")] Lazy<IChannel> backup)
		{
			Primary = primary;
			Backup = backup;
		}

		public Func<IChannel> Primary { get; }

		public Lazy<IChannel> Backup { get; }
	}

	[Container]
	[Singleton<FastChannel, IChannel>(Key = "fast")]
	[Singleton<SlowChannel, IChannel>(Key = "slow")]
	[Singleton<Router>]
	[Singleton<DeferredRouter>]
	public static partial class KeyedContainer;

	public interface IClock;

	public sealed class DefaultClock : IClock;

	public sealed class FastChannelClock : IClock;

	public sealed class Consumer
	{
		public Consumer([FromKey("fast")] IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	// Registered both unkeyed and under a key: the keyed one is reachable only through [FromKey].
	[Container]
	[Singleton<DefaultClock, IClock>]
	[Singleton<FastChannelClock, IClock>(Key = "fast")]
	[Singleton<Consumer>]
	public static partial class MixedContainer;

	public interface IGauge;

	public sealed class SyncGauge : IGauge;

	public sealed class AsyncGauge : IGauge, IAsyncInitializable
	{
		public bool Initialized { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}
	}

	[Container]
	[Singleton<SyncGauge, IGauge>(Key = "sync")]
	[Singleton<AsyncGauge, IGauge>(Key = "async")]
	public static partial class AsyncKeyedContainer;

	[Fact]
	public async Task KeyedResolve_OfAnAsyncTaintedService_FollowsTheStrictAsyncRules()
	{
		using AsyncKeyedContainer.Root container = new();

		// The strict default has no synchronous path for an async-initialized service, keyed or not.
		await That(() => container.Resolve<IGauge>("async")).Throws<InvalidOperationException>()
			.Because("a keyed async-tainted service is not synchronously resolvable in the strict default");
		await That(container.TryResolve<IGauge>("async", out IGauge? _)).IsFalse()
			.Because("TryResolve stays non-throwing for an async-only keyed service");

		IGauge gauge = await container.ResolveAsync<IGauge>("async", TestContext.Current.CancellationToken);
		await That(gauge).Is<AsyncGauge>();
		await That(((AsyncGauge)gauge).Initialized).IsTrue()
			.Because("keyed ResolveAsync awaits the async initialization");
	}

	public interface IWork;

	public sealed class ScopedFast : IWork;

	public sealed class TransientSlow : IWork;

	// A singleton can capture a shorter-lived dependency only through a relationship (direct capture is AWT105),
	// so the keyed scoped/transient targets are reached via Func<T>/Lazy<T>.
	public sealed class Capturer
	{
		public Capturer([FromKey("fast")] Func<IWork> scoped, [FromKey("slow")] Lazy<IWork> transient)
		{
			Scoped = scoped;
			Transient = transient;
		}

		public Func<IWork> Scoped { get; }

		public Lazy<IWork> Transient { get; }
	}

	[Container]
	[Singleton<Capturer>]
	[Scoped<ScopedFast, IWork>(Key = "fast")]
	[Transient<TransientSlow, IWork>(Key = "slow")]
	public static partial class CapturingContainer;
}
