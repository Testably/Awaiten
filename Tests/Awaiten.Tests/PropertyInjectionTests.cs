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

	[Fact]
	public async Task DeferredProperty_BreaksAMutualSingletonCycle_AndWiresBothBackReferences()
	{
		using DeferredCycleContainer.Root container = new();

		OrderService order = container.Resolve<OrderService>();
		InvoiceService invoice = container.Resolve<InvoiceService>();

		// Both deferred back-references are wired after construction, and each points at the shared singleton.
		await That(order.Invoice).IsSameAs(invoice);
		await That(invoice.Order).IsSameAs(order);
		await That(order.Invoice!.Order).IsSameAs(order);
	}

	[Fact]
	public async Task DeferredProperty_IsAssignedExactlyOnce_AndACacheHitDoesNotReassign()
	{
		using DeferredCycleContainer.Root container = new();

		OrderService first = container.Resolve<OrderService>();
		// A second resolve returns the cached singleton; the deferred assignment ran only on construction.
		OrderService second = container.Resolve<OrderService>();

		await That(second).IsSameAs(first);
		await That(first.WiredCount).IsEqualTo(1)
			.Because("the deferred assignment runs once inside the cache-miss block, so a cache hit never reassigns");
	}

	[Fact]
	public async Task DeferredProperty_InANonCyclicGraph_IsStillFilled()
	{
		using DeferredNonCyclicContainer.Root container = new();

		Reader reader = container.Resolve<Reader>();

		await That(reader.Bus).Is<Bus>();
	}

	[Fact]
	public async Task DeferredProperty_OnAScopedCycle_ResolvesPerScope()
	{
		using ScopedDeferredCycleContainer.Root container = new();

		using IAwaitenScope scope = container.CreateScope();
		ScopedLeft left = scope.Resolve<ScopedLeft>();
		ScopedRight right = scope.Resolve<ScopedRight>();

		await That(left.Right).IsSameAs(right);
		await That(right.Left).IsSameAs(left);
	}

	[Fact]
	public async Task DeferredProperty_ToAnAsyncTarget_MakesOwnerAsyncAndFillsAnInitializedInstance()
	{
		using DeferredAsyncTargetContainer.Root container = new();

		// The owner is not itself IAsyncInitializable, but its deferred member targets an async-initialized
		// service, so async taint reaches the owner (reachable only through ResolveAsync) and its deferred
		// assignment awaits the target - rather than emitting a synchronous resolve of an async-only service.
		DeferredAsyncConsumer consumer = await container.ResolveAsync<DeferredAsyncConsumer>(Ct);

		await That(consumer.Connection).Is<Connection>();
		await That(consumer.Connection!.Initialized).IsTrue()
			.Because("a deferred member to an async target is awaited after construction, so the injected instance is already initialized");
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

	public sealed class DeferredAsyncConsumer
	{
		[Inject(Deferred = true)]
		public Connection? Connection { get; set; }
	}

	[Container]
	[Singleton<Connection>]
	[Singleton<DeferredAsyncConsumer>]
	public static partial class DeferredAsyncTargetContainer;

	public sealed class OrderService
	{
		private InvoiceService? _invoice;

		[Inject(Deferred = true)]
		public InvoiceService? Invoice
		{
			get => _invoice;
			set
			{
				_invoice = value;
				WiredCount++;
			}
		}

		public int WiredCount { get; private set; }
	}

	public sealed class InvoiceService
	{
		[Inject(Deferred = true)]
		public OrderService? Order { get; set; }
	}

	[Container]
	[Singleton<OrderService>]
	[Singleton<InvoiceService>]
	public static partial class DeferredCycleContainer;

	public sealed class Reader
	{
		[Inject(Deferred = true)]
		public Bus? Bus { get; set; }
	}

	[Container]
	[Singleton<Bus>]
	[Singleton<Reader>]
	public static partial class DeferredNonCyclicContainer;

	[Fact]
	public async Task DeferredProperty_OnAMixedSingletonTransientCycle_Terminates_AndWiresThroughTheCachedSingleton()
	{
		using MixedLifetimeCycleContainer.Root container = new();

		// The singleton is cached before it is wired, so the transient's back-reference resolves to it and the
		// cycle terminates from the singleton entry point rather than recursing forever.
		CachedHub hub = container.Resolve<CachedHub>();

		await That(hub.Spoke).IsNotNull();
		await That(hub.Spoke!.Hub).IsSameAs(hub)
			.Because("the transient's deferred back-reference is wired to the already-cached singleton");
	}

	public sealed class CachedHub
	{
		[Inject(Deferred = true)]
		public FreshSpoke? Spoke { get; set; }
	}

	public sealed class FreshSpoke
	{
		[Inject(Deferred = true)]
		public CachedHub? Hub { get; set; }
	}

	[Container]
	[Singleton<CachedHub>]
	[Transient<FreshSpoke>]
	public static partial class MixedLifetimeCycleContainer;

	public sealed class ScopedLeft
	{
		[Inject(Deferred = true)]
		public ScopedRight? Right { get; set; }
	}

	public sealed class ScopedRight
	{
		[Inject(Deferred = true)]
		public ScopedLeft? Left { get; set; }
	}

	[Container]
	[Scoped<ScopedLeft>]
	[Scoped<ScopedRight>]
	public static partial class ScopedDeferredCycleContainer;

	[Fact]
	public async Task DeferredProperty_SelfReferential_OnASingleton_WiresToItself()
	{
		using SelfReferentialContainer.Root container = new();

		Node node = container.Resolve<Node>();

		await That(node.Self).IsSameAs(node)
			.Because("the singleton is cached before its deferred member is wired, so the re-entrant self-resolve returns the same cached instance and the self-cycle terminates");
	}

	public sealed class Node
	{
		[Inject(Deferred = true)]
		public Node? Self { get; set; }
	}

	[Container]
	[Singleton<Node>]
	public static partial class SelfReferentialContainer;

	[Fact]
	public async Task DeferredProperty_OnAThreeNodeSingletonCycle_WiresEveryBackReference()
	{
		using ThreeNodeCycleContainer.Root container = new();

		Ring1 one = container.Resolve<Ring1>();
		Ring2 two = container.Resolve<Ring2>();
		Ring3 three = container.Resolve<Ring3>();

		// A cycle longer than two nodes terminates the same way: each singleton is cached before it is wired, so the
		// resolve that laps back to an already-cached participant returns it instead of recursing.
		await That(one.Next).IsSameAs(two);
		await That(two.Next).IsSameAs(three);
		await That(three.Next).IsSameAs(one);
	}

	public sealed class Ring1
	{
		[Inject(Deferred = true)]
		public Ring2? Next { get; set; }
	}

	public sealed class Ring2
	{
		[Inject(Deferred = true)]
		public Ring3? Next { get; set; }
	}

	public sealed class Ring3
	{
		[Inject(Deferred = true)]
		public Ring1? Next { get; set; }
	}

	[Container]
	[Singleton<Ring1>]
	[Singleton<Ring2>]
	[Singleton<Ring3>]
	public static partial class ThreeNodeCycleContainer;

	[Fact]
	public async Task DeferredProperty_OverATransientConstructorBackEdge_TerminatesFromBothEntryPoints()
	{
		using CtorSpokeContainer.Root container = new();

		// The singleton hub is cached before it is wired, so the transient spoke's constructor edge resolves the
		// already-cached hub: the mixed cycle terminates whether resolution starts at the hub or at the spoke.
		CtorHub hub = container.Resolve<CtorHub>();
		CtorSpoke spoke = container.Resolve<CtorSpoke>();

		await That(hub.Spoke).IsNotNull();
		await That(hub.Spoke!.Hub).IsSameAs(hub);
		await That(spoke.Hub).IsSameAs(hub)
			.Because("the spoke's constructor edge resolves the cached hub instead of re-entering its construction");
	}

	public sealed class CtorHub
	{
		[Inject(Deferred = true)]
		public CtorSpoke? Spoke { get; set; }
	}

	public sealed class CtorSpoke
	{
		public CtorSpoke(CtorHub hub)
		{
			Hub = hub;
		}

		public CtorHub Hub { get; }
	}

	[Container]
	[Singleton<CtorHub>]
	[Transient<CtorSpoke>]
	public static partial class CtorSpokeContainer;

	[Fact]
	public async Task DeferredProperty_WhenWiringThrows_TheCacheIsRolledBack_AndTheNextResolveRetries()
	{
		using FlakyContainer.Root container = new();

		// The first resolve fails while wiring the deferred member; the half-wired owner must not stay published,
		// or every later resolve would silently return it with the member still null.
		await That(() => container.Resolve<FlakyOwner>()).Throws<InvalidOperationException>()
			.Because("the wiring failure surfaces to the resolving caller");

		FlakyOwner owner = container.Resolve<FlakyOwner>();

		await That(owner.Dep).IsNotNull()
			.Because("the failed wiring episode unpublished the owner, so the retry rebuilds and wires it completely");
	}

	public sealed class FlakyDep
	{
		private static int Attempts;

		public FlakyDep()
		{
			if (Attempts++ == 0)
			{
				throw new InvalidOperationException("first construction fails");
			}
		}
	}

	public sealed class FlakyOwner
	{
		[Inject(Deferred = true)]
		public FlakyDep? Dep { get; set; }
	}

	[Container]
	[Singleton<FlakyOwner>]
	[Transient<FlakyDep>]
	public static partial class FlakyContainer;

	[Fact]
	public async Task DeferredProperty_ADependencyFirstBuiltDuringWiring_IsDisposedAfterItsOwner()
	{
		DisposeOrder.Clear();
		DisposingWriter writer;
		using (DisposalOrderContainer.Root container = new())
		{
			writer = container.Resolve<DisposingWriter>();
		}

		// The dependency was first materialized while wiring the owner's deferred member, so it registers for
		// disposal before the owner; reverse-order teardown then disposes the owner first and the dependency
		// after it - the owner's Dispose can still use the dependency, like with plain constructor injection.
		await That(writer.Dep).IsNotNull();
		await That(DisposeOrder).IsEqualTo(new[] { "Writer", "Dep", });
	}

	internal static readonly List<string> DisposeOrder = new();

	public sealed class DisposingDep : IDisposable
	{
		public void Dispose() => DisposeOrder.Add("Dep");
	}

	public sealed class DisposingWriter : IDisposable
	{
		[Inject(Deferred = true)]
		public DisposingDep? Dep { get; set; }

		public void Dispose() => DisposeOrder.Add("Writer");
	}

	[Container]
	[Singleton<DisposingWriter>]
	[Singleton<DisposingDep>]
	public static partial class DisposalOrderContainer;
}
