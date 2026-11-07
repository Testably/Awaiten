using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     Implementations that are private to a public container or to a module. The generated resolvers of a container
///     return such an implementation as an accessible type it converts to, while their bodies, nested in the
///     container, still construct, initialize, hook and dispose it as its own type. A module builds its private
///     implementations through its generated factory.
/// </summary>
public sealed partial class PrivateNestedImplementationTests
{
	public interface IClock;

	public interface ITimer;

	public interface ILedger;

	public interface IReceipt
	{
		string Text { get; }
	}

	public interface IWarm
	{
		bool Initialized { get; }
	}

	public interface IInner
	{
		IClock Inner { get; }
	}

	public interface IRepository<T>;

	public interface IGreeter;

	public sealed class Tracker
	{
		public List<string> Events { get; } = new();
	}

	public sealed class SystemClock : IClock;

	public abstract class ClockBase : IClock, ITimer;

	public sealed class LoggingClock(IClock inner) : IClock, IInner
	{
		public IClock Inner => inner;
	}

	public sealed class KeyedConsumer([FromKey("keyed")] IClock clock)
	{
		public IClock Clock => clock;
	}

	public sealed class ContextConsumer(IClock clock)
	{
		public IClock Clock => clock;
	}

	public sealed class RepositoryUser(IRepository<string> repository)
	{
		public IRepository<string> Repository => repository;
	}

	public interface ICategorized
	{
		string Category { get; }
	}

	public interface IHandler<in T>;

	public class Event;

	public sealed class OrderPlaced : Event;

	public interface IChicken
	{
		IEgg? Egg { get; }
	}

	public interface IEgg
	{
		IChicken? Chicken { get; }
	}

	public sealed class LoggerUser(ICategorized logger)
	{
		public ICategorized Logger => logger;
	}

	public sealed class OrderConsumer(IHandler<OrderPlaced> handler, IEnumerable<IHandler<OrderPlaced>> handlers)
	{
		public IHandler<OrderPlaced> Handler => handler;

		public IEnumerable<IHandler<OrderPlaced>> Handlers => handlers;
	}

	public sealed class DeferredClockUser(Lazy<IClock> lazy, Func<IClock> factory)
	{
		public Lazy<IClock> Lazy => lazy;

		public Func<IClock> Factory => factory;
	}

	public sealed class AsyncDeferredClockUser(Lazy<Task<IClock>> lazy, Func<Task<IClock>> factory)
	{
		public Lazy<Task<IClock>> Lazy => lazy;

		public Func<Task<IClock>> Factory => factory;
	}

	private static string NameOf(object instance) => instance.GetType().Name;

	public sealed class Lifetimes
	{
		[Test]
		public async Task Singleton_IsOneInstancePerContainer()
		{
			using LifetimeShop.Root shop = new();
			using LifetimeShop.Scope scope = shop.CreateScope();

			IClock clock = shop.Resolve<IClock>();

			await That(NameOf(clock)).IsEqualTo("FakeClock");
			await That(scope.Resolve<IClock>()).IsSameAs(clock)
				.Because("the scope reaches the singleton through the Root's resolver, which returns the accessible service type");
		}

		[Test]
		public async Task Scoped_IsOneInstancePerScope()
		{
			using LifetimeShop.Root shop = new();
			using LifetimeShop.Scope first = shop.CreateScope();
			using LifetimeShop.Scope second = shop.CreateScope();

			ILedger ledger = first.Resolve<ILedger>();

			await That(NameOf(ledger)).IsEqualTo("FakeLedger");
			await That(first.Resolve<ILedger>()).IsSameAs(ledger);
			await That(second.Resolve<ILedger>()).IsNotSameAs(ledger);
		}

		[Test]
		public async Task Transient_IsFreshPerResolve()
		{
			using LifetimeShop.Root shop = new();

			IReceipt receipt = shop.Resolve<IReceipt>();

			await That(NameOf(receipt)).IsEqualTo("FakeReceipt");
			await That(shop.Resolve<IReceipt>()).IsNotSameAs(receipt);
		}

		[Test]
		public async Task Eager_IsBuiltWithTheRoot()
		{
			using LifetimeShop.Root shop = new();

			await That(shop.Resolve<Tracker>().Events).Contains("eager")
				.Because("an eager private singleton is constructed when the Root is created");
		}
	}

	public sealed class Routing
	{
		[Test]
		public async Task Collection_ContainsThePrivateMembersInOrder()
		{
			using RoutingShop.Root shop = new();

			string[] names = shop.Resolve<IEnumerable<IClock>>().Select(NameOf).ToArray();

			await That(names).IsEqualTo(["SystemClock", "FakeClock",]).InAnyOrder().Because("both unkeyed registrations are collection members");
			await That(NameOf(shop.Resolve<IClock>())).IsEqualTo("SystemClock")
				.Because("the first registration wins single resolution");
		}

		[Test]
		public async Task Key_ResolvesThePrivateKeyedImplementation()
		{
			using RoutingShop.Root shop = new();

			await That(NameOf(shop.Resolve<KeyedConsumer>().Clock)).IsEqualTo("KeyedClock");
		}

		[Test]
		public async Task WhenInjectedInto_ResolvesThePrivateImplementationForItsConsumerOnly()
		{
			using RoutingShop.Root shop = new();

			await That(NameOf(shop.Resolve<ContextConsumer>().Clock)).IsEqualTo("ContextClock");
		}

		[Test]
		public async Task PrivateTestDouble_OfAnExtendingContainer_ReplacesTheBaseRegistration()
		{
			using ExtendingTestShop.Root shop = new();

			await That(NameOf(shop.Resolve<IClock>())).IsEqualTo("FakeClock")
				.Because("the extending container constructs its own private double and replaces the base's registration");
			await That(shop.Resolve<IEnumerable<IClock>>()).HasCount(1);
		}

		[Test]
		public async Task PrivateRegistration_OverridesTheFallbackOfAModule()
		{
			using OverridingShop.Root shop = new();

			await That(NameOf(shop.Resolve<IClock>())).IsEqualTo("FakeClock")
				.Because("the container's own registration replaces the module's fallback");
		}
	}

	public sealed class Async
	{
		[Test]
		public async Task SyncResolve_IsGuarded()
		{
			using AsyncShop.Root shop = new();

			await That(() => shop.Resolve<IClock>()).Throws<InvalidOperationException>()
				.WithMessage("*ResolveAsync*").AsWildcard()
				.Because("the private implementation is async-initializable, read from its own symbol");
		}

		[Test]
		public async Task ResolveAsync_InitializesOnce()
		{
			await using AsyncShop.Root shop = new();

			IClock clock = await shop.ResolveAsync<IClock>();

			await That(((IWarm)clock).Initialized).IsTrue();
			await That(await shop.ResolveAsync<IClock>()).IsSameAs(clock);
		}

		[Test]
		public async Task AsyncCollection_AwaitsThePrivateMember()
		{
			await using AsyncShop.Root shop = new();

			List<IClock> clocks = new();
			await foreach (IClock clock in await shop.ResolveAsync<IAsyncEnumerable<IClock>>())
			{
				clocks.Add(clock);
			}

			await That(clocks).HasCount(1);
			await That(clocks[0]).IsSameAs(await shop.ResolveAsync<IClock>())
				.Because("the async collection awaits the same memoized task the typed resolve returns");
		}

		[Test]
		public async Task SyncAfterInit_ResolvesSynchronouslyThroughTheAsyncResolver()
		{
			using SyncAfterInitShop.Root shop = new();

			IClock clock = shop.Resolve<IClock>();

			await That(((IWarm)clock).Initialized).IsTrue();
			await That(await shop.ResolveAsync<IClock>()).IsSameAs(clock);
		}
	}

	public sealed class Disposal
	{
		[Test]
		public async Task Disposable_IsDisposedWithItsScope()
		{
			await using DisposalShop.Root shop = new();
			Tracker tracker = shop.Resolve<Tracker>();
			await using (DisposalShop.Scope scope = shop.CreateScope())
			{
				scope.Resolve<ILedger>();
			}

			await That(tracker.Events).IsEqualTo(["disposed",]);
		}

		[Test]
		public async Task AsyncDisposable_IsDisposedAsynchronouslyWithItsScope()
		{
			await using DisposalShop.Root shop = new();
			Tracker tracker = shop.Resolve<Tracker>();
			await using (DisposalShop.Scope scope = shop.CreateScope())
			{
				scope.Resolve<IReceipt>();
			}

			await That(tracker.Events).IsEqualTo(["async disposed",]);
		}

		[Test]
		public async Task SuppressDisposal_LeavesItUndisposed()
		{
			await using DisposalShop.Root shop = new();
			Tracker tracker = shop.Resolve<Tracker>();
			await using (DisposalShop.Scope scope = shop.CreateScope())
			{
				scope.Resolve<ITimer>();
			}

			await That(tracker.Events).IsEmpty();
		}
	}

	public sealed class Hooks
	{
		[Test]
		public async Task Hooks_ReceiveThePrivateType()
		{
			Tracker tracker;
			using (HookShop.Root shop = new())
			{
				tracker = shop.Resolve<Tracker>();
				shop.Resolve<IClock>();
				await That(tracker.Events).IsEqualTo(["activated FakeClock",]);
			}

			await That(tracker.Events).IsEqualTo(["activated FakeClock", "released FakeClock",])
				.Because("the hooks take the private type itself, which the resolver body passes without a cast");
		}
	}

	public sealed class Decoration
	{
		[Test]
		public async Task ContainerDecorator_WrapsThePrivateImplementation()
		{
			using DecoratedShop.Root shop = new();

			IClock clock = shop.Resolve<IClock>();

			await That(clock).IsExactly<LoggingClock>();
			await That(NameOf(((IInner)clock).Inner)).IsEqualTo("FakeClock");
		}

		[Test]
		public async Task PrivateDecorator_WrapsTheService()
		{
			using PrivateDecoratorShop.Root shop = new();

			IClock clock = shop.Resolve<IClock>();

			await That(NameOf(clock)).IsEqualTo("AuditingClock");
			await That(((IInner)clock).Inner).IsExactly<SystemClock>();
		}

		[Test]
		public async Task PrivateComposite_FrontsTheMembers()
		{
			using PrivateCompositeShop.Root shop = new();

			IClock clock = shop.Resolve<IClock>();

			await That(NameOf(clock)).IsEqualTo("CompositeClock");
		}
	}

	public sealed class Production
	{
		[Test]
		public async Task FactoryAndInstance_OfPrivateRegisteredTypes()
		{
			using ProductionShop.Root shop = new();

			await That(NameOf(shop.Resolve<IClock>())).IsEqualTo("FactoryClock");
			await That(NameOf(shop.Resolve<ITimer>())).IsEqualTo("InstanceTimer");
		}

		[Test]
		public async Task Arg_IsPassedToThePrivateConstructor()
		{
			using ProductionShop.Root shop = new();

			IReceipt receipt = shop.Resolve<Func<string, IReceipt>>()("espresso");

			await That(receipt.Text).IsEqualTo("espresso");
		}

		[Test]
		public async Task FromKey_OnThePrivateConstructorIsHonored()
		{
			using ProductionShop.Root shop = new();

			await That(((IInner)shop.Resolve<ILedger>()).Inner).IsExactly<SystemClock>();
		}

		[Test]
		public async Task SeveralServices_ShareOneInstanceThroughAnAccessibleBase()
		{
			using MultiServiceShop.Root shop = new();

			await That(shop.Resolve<ITimer>()).IsSameAs(shop.Resolve<IClock>())
				.Because("the resolver returns the accessible base class, which converts to both services");
		}

		[Test]
		public async Task OpenGeneric_ClosesThePrivateImplementation()
		{
			using MultiServiceShop.Root shop = new();

			await That(NameOf(shop.Resolve<RepositoryUser>().Repository)).IsEqualTo("Repository`1");
		}
	}

	public sealed class Relationships
	{
		[Test]
		public async Task RequestingTypeFactory_OfAPrivateType_ReceivesEachConsumer()
		{
			using RelationshipShop.Root shop = new();

			await That(NameOf(shop.Resolve<LoggerUser>().Logger)).IsEqualTo("CategoryLogger");
			await That(shop.Resolve<LoggerUser>().Logger.Category).IsEqualTo(nameof(LoggerUser));
			await That(shop.Resolve<ICategorized>().Category).IsEqualTo("<root>");
		}

		[Test]
		public async Task RequestingTypeFactory_PassesAPrivateConsumerAsTheRequestingType()
		{
			using RelationshipShop.Root shop = new();

			await That(shop.Resolve<IReceipt>().Text).IsEqualTo("AuditReceipt")
				.Because("the resolver body may name the private consumer in its typeof(…)");
		}

		[Test]
		public async Task AsyncRequestingTypeFactory_OfAPrivateType_ReceivesEachConsumer()
		{
			using AsyncRelationshipShop.Root shop = new();

			LoggerUser user = await shop.ResolveAsync<LoggerUser>();

			await That(NameOf(user.Logger)).IsEqualTo("CategoryLogger");
			await That(user.Logger.Category).IsEqualTo(nameof(LoggerUser));
		}

		[Test]
		public async Task Variance_SatisfiesTheRequestWithThePrivateImplementation()
		{
			using RelationshipShop.Root shop = new();

			await That(NameOf(shop.Resolve<OrderConsumer>().Handler)).IsEqualTo("EventSink")
				.Because("the contravariant registration of the private handler satisfies the derived request");
			await That(shop.Resolve<OrderConsumer>().Handlers.Select(NameOf)).IsEqualTo(["EventSink",]);
		}

		[Test]
		public async Task Owned_OfThePrivateImplementation_IsDisposedWithItsHandle()
		{
			using RelationshipShop.Root shop = new();
			Tracker tracker = shop.Resolve<Tracker>();

			Owned<ILedger> owned = shop.Resolve<Owned<ILedger>>();
			await That(NameOf(owned.Value)).IsEqualTo("DisposableLedger");
			owned.Dispose();

			await That(tracker.Events).IsEqualTo(["ledger disposed",]);
		}

		[Test]
		public async Task KeyedDictionary_HoldsThePrivateKeyedMembers()
		{
			using RelationshipShop.Root shop = new();

			IReadOnlyDictionary<string, IClock> clocks = shop.Resolve<IReadOnlyDictionary<string, IClock>>();

			await That(clocks.Keys).IsEqualTo(["fast", "slow",]).InAnyOrder();
			await That(NameOf(clocks["fast"])).IsEqualTo("FastClock");
			await That(clocks["slow"]).IsSameAs(shop.Resolve<IClock>("slow"))
				.Because("the dictionary and the keyed resolve share the singleton");
		}

		[Test]
		public async Task LazyAndFunc_ResolveThePrivateImplementationOnDemand()
		{
			using RelationshipShop.Root shop = new();

			DeferredClockUser user = shop.Resolve<DeferredClockUser>();

			await That(NameOf(user.Lazy.Value)).IsEqualTo("FastClock");
			await That(user.Factory()).IsSameAs(user.Lazy.Value);
		}

		[Test]
		public async Task AsyncLazyAndFunc_InitializeThePrivateImplementation()
		{
			await using AsyncRelationshipShop.Root shop = new();

			AsyncDeferredClockUser user = await shop.ResolveAsync<AsyncDeferredClockUser>();
			IClock clock = await user.Lazy.Value;

			await That(((IWarm)clock).Initialized).IsTrue();
			await That(await user.Factory()).IsSameAs(clock);
		}

		[Test]
		public async Task DeferredInject_WiresAPrivateImplementationIntoItsCycle()
		{
			using RelationshipShop.Root shop = new();

			IChicken chicken = shop.Resolve<IChicken>();

			await That(chicken.Egg!.Chicken).IsSameAs(chicken)
				.Because("the deferred member of the private type is wired after construction, inside the resolver body");
		}
	}

	public sealed class Modules
	{
		[Test]
		public async Task ModulePrivateImplementation_IsBuiltThroughTheGeneratedFactory()
		{
			Tracker tracker;
			using (ModuleShop.Root shop = new())
			{
				tracker = shop.Resolve<Tracker>();
				await That(NameOf(shop.Resolve<IGreeter>())).IsEqualTo("Greeter");
				await That(shop.Resolve<IGreeter>()).IsSameAs(shop.Resolve<IGreeter>());
			}

			await That(tracker.Events).IsEqualTo(["greeter disposed",])
				.Because("the disposable trait is read from the private implementation, not from the service the factory returns");
		}

		[Test]
		public async Task ModulePrivateHooks_RunThroughTheGeneratedWrappers()
		{
			Tracker tracker;
			using (ModuleHookShop.Root shop = new())
			{
				tracker = shop.Resolve<Tracker>();
				shop.Resolve<IGreeter>();
				await That(tracker.Events).IsEqualTo(["activated Greeter",]);
			}

			await That(tracker.Events).IsEqualTo(["activated Greeter", "released Greeter",])
				.Because("the container calls the module's public wrappers, which pass the private type to the module's private hooks");
		}
	}

	[Container]
	[Singleton<Tracker>]
	[Singleton<FakeClock, IClock>]
	[Scoped<FakeLedger, ILedger>]
	[Transient<FakeReceipt, IReceipt>]
	[Singleton<EagerTimer, ITimer>(Eager = true)]
	public static partial class LifetimeShop
	{
		private sealed class FakeClock : IClock;

		private sealed class FakeLedger : ILedger;

		private sealed class FakeReceipt : IReceipt
		{
			public string Text => "receipt";
		}

		private sealed class EagerTimer : ITimer
		{
			public EagerTimer(Tracker tracker) => tracker.Events.Add("eager");
		}
	}

	[Container]
	[Singleton<SystemClock, IClock>]
	[Singleton<FakeClock, IClock>]
	[Singleton<KeyedClock, IClock>(Key = "keyed")]
	[Singleton<ContextClock, IClock>(WhenInjectedInto = typeof(ContextConsumer))]
	[Transient<KeyedConsumer>]
	[Transient<ContextConsumer>]
	public static partial class RoutingShop
	{
		private sealed class FakeClock : IClock;

		private sealed class KeyedClock : IClock;

		private sealed class ContextClock : IClock;
	}

	[Container]
	[Singleton<SystemClock, IClock>]
	public static partial class BaseShop;

	[Container(Extends = typeof(BaseShop))]
	[Singleton<FakeClock, IClock>]
	public static partial class ExtendingTestShop
	{
		private sealed class FakeClock : IClock;
	}

	[Module]
	[Singleton<SystemClock, IClock>(Fallback = Fallback.Silent)]
	public static partial class DefaultClockModule;

	[Container]
	[Import(typeof(DefaultClockModule))]
	[Singleton<FakeClock, IClock>]
	public static partial class OverridingShop
	{
		private sealed class FakeClock : IClock;
	}

	[Container]
	[Singleton<WarmClock, IClock>]
	public static partial class AsyncShop
	{
		private sealed class WarmClock : IClock, IWarm, IAsyncInitializable
		{
			public bool Initialized { get; private set; }

			public Task InitializeAsync(CancellationToken cancellationToken)
			{
				Initialized = true;
				return Task.CompletedTask;
			}
		}
	}

	[Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
	[Singleton<WarmClock, IClock>]
	public static partial class SyncAfterInitShop
	{
		private sealed class WarmClock : IClock, IWarm, IAsyncInitializable
		{
			public bool Initialized { get; private set; }

			public Task InitializeAsync(CancellationToken cancellationToken)
			{
				Initialized = true;
				return Task.CompletedTask;
			}
		}
	}

	[Container]
	[Singleton<Tracker>]
	[Scoped<DisposableLedger, ILedger>]
	[Scoped<AsyncDisposableReceipt, IReceipt>]
	[Scoped<SuppressedTimer, ITimer>(SuppressDisposal = true)]
	public static partial class DisposalShop
	{
		private sealed class DisposableLedger(Tracker tracker) : ILedger, IDisposable
		{
			public void Dispose() => tracker.Events.Add("disposed");
		}

		private sealed class AsyncDisposableReceipt(Tracker tracker) : IReceipt, IAsyncDisposable
		{
			public string Text => "receipt";

			public ValueTask DisposeAsync()
			{
				tracker.Events.Add("async disposed");
				return default;
			}
		}

		private sealed class SuppressedTimer(Tracker tracker) : ITimer, IDisposable
		{
			public void Dispose() => tracker.Events.Add("suppressed disposed");
		}
	}

	[Container]
	[Singleton<Tracker>]
	[Singleton<FakeClock, IClock>(OnActivated = nameof(Activate), OnRelease = nameof(Release))]
	public static partial class HookShop
	{
		private sealed class FakeClock : IClock;

		private static void Activate(FakeClock clock, Tracker tracker) => tracker.Events.Add($"activated {clock.GetType().Name}");

		private static void Release(FakeClock clock, Tracker tracker) => tracker.Events.Add($"released {clock.GetType().Name}");
	}

	[Container]
	[Singleton<FakeClock, IClock>]
	[Decorate<LoggingClock, IClock>]
	public static partial class DecoratedShop
	{
		private sealed class FakeClock : IClock;
	}

	[Container]
	[Singleton<SystemClock, IClock>]
	[Decorate<AuditingClock, IClock>]
	public static partial class PrivateDecoratorShop
	{
		private sealed class AuditingClock(IClock inner) : IClock, IInner
		{
			public IClock Inner => inner;
		}
	}

	[Container]
	[Singleton<SystemClock, IClock>]
	[Composite<CompositeClock, IClock>]
	public static partial class PrivateCompositeShop
	{
		private sealed class CompositeClock(IEnumerable<IClock> clocks) : IClock
		{
			public IEnumerable<IClock> Clocks => clocks;
		}
	}

	[Container]
	[Singleton<SystemClock>]
	[Singleton<SystemClock, IClock>(Key = "keyed")]
	[Singleton<FactoryClock, IClock>(Factory = nameof(CreateClock))]
	[Singleton<InstanceTimer, ITimer>(Instance = nameof(Timer))]
	[Transient<ArgReceipt, IReceipt>]
	[Transient<KeyedLedger, ILedger>]
	public static partial class ProductionShop
	{
		private static readonly InstanceTimer Timer = new();

		private static FactoryClock CreateClock() => new();

		private sealed class FactoryClock : IClock;

		private sealed class InstanceTimer : ITimer;

		private sealed class ArgReceipt([Arg] string text) : IReceipt
		{
			public string Text => text;
		}

		private sealed class KeyedLedger([FromKey("keyed")] IClock clock) : ILedger, IInner
		{
			public IClock Inner => clock;
		}
	}

	[Container]
	[Singleton<DualClock, IClock>]
	[Singleton<DualClock, ITimer>]
	[Transient(typeof(Repository<>), typeof(IRepository<>))]
	[Transient<RepositoryUser>]
	public static partial class MultiServiceShop
	{
		private sealed class DualClock : ClockBase;

		private sealed class Repository<T> : IRepository<T>;
	}

	[Container]
	[Singleton<Tracker>]
	[Transient<CategoryLogger, ICategorized>(Factory = nameof(CreateLogger))]
	[Transient<LoggerUser>]
	[Transient<AuditReceipt, IReceipt>]
	[Singleton<EventSink, IHandler<Event>>]
	[Transient<OrderConsumer>]
	[Transient<DisposableLedger, ILedger>]
	[Singleton<FastClock, IClock>]
	[Singleton<FastClock, IClock>(Key = "fast")]
	[Singleton<SlowClock, IClock>(Key = "slow")]
	[Transient<DeferredClockUser>]
	[Singleton<Chicken, IChicken>]
	[Singleton<Egg, IEgg>]
	public static partial class RelationshipShop
	{
		private static CategoryLogger CreateLogger([RequestingType] Type? requestingType) => new(requestingType?.Name ?? "<root>");

		private sealed class CategoryLogger(string category) : ICategorized
		{
			public string Category => category;
		}

		private sealed class AuditReceipt(ICategorized logger) : IReceipt
		{
			public string Text => logger.Category;
		}

		private sealed class EventSink : IHandler<Event>;

		private sealed class DisposableLedger(Tracker tracker) : ILedger, IDisposable
		{
			public void Dispose() => tracker.Events.Add("ledger disposed");
		}

		private sealed class FastClock : IClock;

		private sealed class SlowClock : IClock;

		private sealed class Chicken : IChicken
		{
			[Inject(Deferred = true)]
			public IEgg? Egg { get; set; }
		}

		private sealed class Egg : IEgg
		{
			[Inject(Deferred = true)]
			public IChicken? Chicken { get; set; }
		}
	}

	[Container]
	[Transient<CategoryLogger, ICategorized>(Factory = nameof(CreateLoggerAsync))]
	[Transient<LoggerUser>]
	[Singleton<WarmClock, IClock>]
	[Transient<AsyncDeferredClockUser>]
	public static partial class AsyncRelationshipShop
	{
		private static Task<CategoryLogger> CreateLoggerAsync([RequestingType] Type? requestingType) => Task.FromResult(new CategoryLogger(requestingType?.Name ?? "<root>"));

		private sealed class CategoryLogger(string category) : ICategorized
		{
			public string Category => category;
		}

		private sealed class WarmClock : IClock, IWarm, IAsyncInitializable
		{
			public bool Initialized { get; private set; }

			public Task InitializeAsync(CancellationToken cancellationToken)
			{
				Initialized = true;
				return Task.CompletedTask;
			}
		}
	}

	[Module]
	[Singleton<Greeter, IGreeter>]
	public static partial class GreeterModule
	{
		private sealed class Greeter(Tracker tracker) : IGreeter, IDisposable
		{
			public void Dispose() => tracker.Events.Add("greeter disposed");
		}
	}

	[Container]
	[Import(typeof(GreeterModule))]
	[Singleton<Tracker>]
	public static partial class ModuleShop;

	[Module]
	[Singleton<Greeter, IGreeter>(OnActivated = nameof(Activate), OnRelease = nameof(Release))]
	public static partial class HookedGreeterModule
	{
		private sealed class Greeter : IGreeter;

		private static void Activate(Greeter greeter, Tracker tracker) => tracker.Events.Add($"activated {greeter.GetType().Name}");

		private static void Release(IGreeter greeter, Tracker tracker) => tracker.Events.Add($"released {greeter.GetType().Name}");
	}

	[Container]
	[Import(typeof(HookedGreeterModule))]
	[Singleton<Tracker>]
	public static partial class ModuleHookShop;
}
