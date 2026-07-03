using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of opt-in property injection: a settable or <c>init</c> property marked
///     <c>[Inject]</c> is filled after construction through an object initializer (no reflection, never
///     observed half-set). Property injection is opt-in - a plain property is not auto-injected. A
///     property edge resolves exactly like a constructor parameter - direct, keyed via <c>[FromKey]</c>,
///     a relationship type, a collection, or async-initialized (awaited through the async surface). The
///     containers and services are nested types, so the enclosing class is <c>partial</c>.
/// </summary>
public partial class PropertyInjectionTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task InjectSettableProperty_IsFilled()
	{
		using InjectContainer.Root container = new();

		Logger logger = container.Resolve<Logger>();

		await That(logger.Bus).Is<Bus>();
	}

	[Fact]
	public async Task PlainProperty_WithoutInject_IsNotFilled()
	{
		using InjectContainer.Root container = new();

		Logger logger = container.Resolve<Logger>();

		await That(logger.Plain).IsNull()
			.Because("property injection is opt-in: a property without [Inject] is left to the caller");
	}

	[Fact]
	public async Task InheritedInjectProperty_OnABaseType_IsFilled()
	{
		using InheritedContainer.Root container = new();

		DerivedConsumer consumer = container.Resolve<DerivedConsumer>();

		await That(consumer.Bus).Is<Bus>()
			.Because("the base-type walk finds a plain inherited [Inject] property, so it is filled without re-annotating on the derived type ([Inject] stays Inherited = false; this is not attribute inheritance)");
	}

	[Fact]
	public async Task OverriddenProperty_WithoutInject_IsNotFilled()
	{
		using OverrideContainer.Root container = new();

		OverridingConsumer consumer = container.Resolve<OverridingConsumer>();

		await That(consumer.Bus).IsNull()
			.Because("[Inject] is Inherited = false: an override that does not repeat [Inject] shadows the base declaration, so the most-derived one wins and injection is skipped - the override keeps full control to opt out");
	}

	[Fact]
	public async Task FromKeyProperty_SelectsTheKeyedRegistration()
	{
		using KeyedContainer.Root container = new();

		KeyedConsumer consumer = container.Resolve<KeyedConsumer>();

		await That(consumer.Primary).Is<FastChannel>();
		await That(consumer.Backup).Is<SlowChannel>();
	}

	[Fact]
	public async Task RelationshipProperty_DefersResolution()
	{
		using RelationshipContainer.Root container = new();

		DeferredConsumer consumer = container.Resolve<DeferredConsumer>();

		await That(consumer.Bus!()).Is<Bus>()
			.Because("a Func<T> injected property defers to the target's resolver, just like a constructor parameter");
	}

	[Fact]
	public async Task CollectionProperty_ResolvesEveryRegistration()
	{
		using CollectionContainer.Root container = new();

		PluginHost host = container.Resolve<PluginHost>();

		await That(host.Plugins!.Select(p => p.GetType()))
			.IsEqualTo(new[] { typeof(Alpha), typeof(Beta), }).InAnyOrder();
	}

	[Fact]
	public async Task AsyncInitializedProperty_IsAwaitedThroughTheAsyncSurface()
	{
		using AsyncContainer.Root container = new();

		// The consumer is async-tainted through its [Inject] Connection, so it is reachable only asynchronously;
		// the member is awaited inside the initializer, so the connection it injects is already initialized.
		AsyncConsumer consumer = await container.ResolveAsync<AsyncConsumer>(Ct);

		await That(consumer.Connection).Is<Connection>();
		await That(consumer.Connection!.Initialized).IsTrue();
	}

	public sealed class Bus;

	public sealed class Logger
	{
		[Inject]
		public Bus? Bus { get; set; }

		public Bus? Plain { get; set; }
	}

	[Container]
	[Transient<Bus>]
	[Transient<Logger>]
	public static partial class InjectContainer;

	public abstract class ConsumerBase
	{
		[Inject]
		public Bus? Bus { get; set; }
	}

	public sealed class DerivedConsumer : ConsumerBase;

	[Container]
	[Transient<Bus>]
	[Transient<DerivedConsumer>]
	public static partial class InheritedContainer;

	public abstract class VirtualConsumerBase
	{
		[Inject]
		public virtual Bus? Bus { get; set; }
	}

	public sealed class OverridingConsumer : VirtualConsumerBase
	{
		// Overrides the [Inject] property without repeating [Inject]: the most-derived declaration wins and,
		// carrying no [Inject], shadows the base one so the property is not filled.
		public override Bus? Bus { get; set; }
	}

	[Container]
	[Transient<Bus>]
	[Transient<OverridingConsumer>]
	public static partial class OverrideContainer;

	public interface IChannel;

	public sealed class FastChannel : IChannel;

	public sealed class SlowChannel : IChannel;

	public sealed class KeyedConsumer
	{
		[Inject]
		[FromKey("fast")]
		public IChannel? Primary { get; set; }

		[Inject]
		[FromKey("slow")]
		public IChannel? Backup { get; set; }
	}

	[Container]
	[Singleton<FastChannel, IChannel>(Key = "fast")]
	[Singleton<SlowChannel, IChannel>(Key = "slow")]
	[Singleton<KeyedConsumer>]
	public static partial class KeyedContainer;

	public sealed class DeferredConsumer
	{
		[Inject]
		public Func<Bus>? Bus { get; set; }
	}

	[Container]
	[Transient<Bus>]
	[Singleton<DeferredConsumer>]
	public static partial class RelationshipContainer;

	public interface IPlugin;

	public sealed class Alpha : IPlugin;

	public sealed class Beta : IPlugin;

	public sealed class PluginHost
	{
		[Inject]
		public IEnumerable<IPlugin>? Plugins { get; set; }
	}

	[Container]
	[Transient<Alpha, IPlugin>]
	[Transient<Beta, IPlugin>]
	[Transient<PluginHost>]
	public static partial class CollectionContainer;

	public sealed class Connection : IAsyncInitializable
	{
		public bool Initialized { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}
	}

	public sealed class AsyncConsumer
	{
		[Inject]
		public Connection? Connection { get; set; }
	}

	[Container]
	[Singleton<Connection>]
	[Singleton<AsyncConsumer>]
	public static partial class AsyncContainer;
}
