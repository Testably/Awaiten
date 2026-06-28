namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of keyed registration: several implementations share one service type under
///     different keys, and a consumer selects one with <c>[FromKey]</c>. An unkeyed resolution still
///     returns the unkeyed registration. The containers and services are nested types, so the enclosing
///     class is <c>partial</c>.
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

	// A service type registered both unkeyed and under a key: the unkeyed registration backs the bare
	// resolution, the keyed one is reachable only through [FromKey].
	[Container]
	[Singleton<DefaultClock, IClock>]
	[Singleton<FastChannelClock, IClock>(Key = "fast")]
	[Singleton<Consumer>]
	public static partial class MixedContainer;

	public interface IWork;

	public sealed class ScopedFast : IWork;

	public sealed class TransientSlow : IWork;

	// A singleton may only capture a shorter-lived dependency through a relationship (a direct capture is
	// AWT105), so the keyed scoped/transient targets are reached via Func<T>/Lazy<T>.
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
