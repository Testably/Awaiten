using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Awaiten.Tests.ContainerSupport;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of <c>[Container(Extends = typeof(Base))]</c>: the extending container inherits the base's
///     registrations, imports, scans, factories, hooks, decorators, composites and options, and its own registrations
///     replace the base's for the same service and key, collections included.
/// </summary>
public partial class ContainerExtendsTests
{
	private static CancellationToken Ct => TestContext.Current!.Execution.CancellationToken;

	public class Replacement
	{
		[Test]
		public async Task OwnRegistration_ReplacesTheInheritedOne()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<IClock>()).Is<FakeClock>();
		}

		[Test]
		public async Task ReplacedRegistration_IsAbsentFromTheServiceCollection()
		{
			using TestShop.Root container = new();

			IClock[] clocks = container.Resolve<IClock[]>();

			await That(clocks).HasCount(1)
				.Because("a replaced production clock must not be built when a consumer enumerates the clocks");
			await That(clocks[0]).Is<FakeClock>();
		}

		[Test]
		public async Task InheritedConsumer_IsRewiredToTheReplacement()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<Barista>().Clock).Is<FakeClock>();
		}

		[Test]
		public async Task Base_IsUnchanged()
		{
			using ProductionShop.Root container = new();

			await That(container.Resolve<IClock>()).Is<SystemClock>();
			await That(container.Resolve<IPaymentGateway>()).Is<BankGateway>();
		}

		[Test]
		public async Task InheritedModuleRegistration_IsReplacedToo()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<IPaymentGateway[]>()).HasCount(1)
				.Because("the base's imported module registration is part of the inherited composition");
			await That(container.Resolve<IPaymentGateway>()).Is<FakeGateway>();
		}

		[Test]
		public async Task Instance_ReplacesAnInheritedFactoryOfTheSameService()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<IRandom>()).IsSameAs(TestShop.FixedRandom)
				.Because("replacement drops the inherited factory before coalescing, so no AWT111 arises");
		}

		[Test]
		public async Task ReplacingOneService_ReplacesEveryInheritedRegistrationOfIt()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<IEnumerable<INotifier>>().Select(notifier => notifier.GetType()))
				.IsEqualTo([typeof(FakeNotifier),])
				.Because("a derived registration of a service replaces all inherited ones, so it cannot add to an inherited collection");
		}

		[Test]
		public async Task FallbackRegistration_OnlyFillsAGap()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<IGrinder>()).Is<BurrGrinder>()
				.Because("a Fallback registration of the extending container replaces nothing");
		}

		[Test]
		public async Task OwnRegistration_IsAddedWhenTheBaseHasNone()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<TestProbe>()).IsNotNull();
		}
	}

	public class Inheritance
	{
		[Test]
		public async Task InheritedScan_StillContributesItsMatches()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<IEnumerable<IExtendsPlugin>>().Count()).IsEqualTo(2);
		}

		[Test]
		public async Task InheritedFactory_IsCalledOnTheBase()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<Greeter>().Greeting).IsEqualTo("production");
		}

		[Test]
		public async Task InheritedLifecycleHook_RunsOnTheInheritedRegistration()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<Oven>().Preheated).IsTrue()
				.Because("the base's OnActivated hook belongs to its registration and comes along with it");
		}

		[Test]
		public async Task InheritedPropertyInjection_FillsTheProperty()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<Menu>().Clock).Is<FakeClock>()
				.Because("the base's [InjectProperty] is inherited and resolves the replaced clock");
		}

		[Test]
		public async Task InheritedComposite_FrontsTheReplacements()
		{
			using TestShop.Root container = new();

			IAlarm alarm = container.Resolve<IAlarm>();

			await That(alarm).Is<AlarmComposite>();
			await That(((AlarmComposite)alarm).Members.Select(member => member.GetType())).IsEqualTo([typeof(FakeAlarm),]);
		}

		[Test]
		public async Task InheritedScopedRegistration_IsSharedWithinAScope()
		{
			using TestShop.Root container = new();
			using IAwaitenScope first = container.CreateScope();
			using IAwaitenScope second = container.CreateScope();

			await That(first.Resolve<Order>()).IsSameAs(first.Resolve<Order>());
			await That(first.Resolve<Order>()).IsNotSameAs(second.Resolve<Order>());
		}

		[Test]
		public async Task InheritedDisposableSingleton_IsDisposedWithTheExtendingRoot()
		{
			Ledger ledger;
			using (TestShop.Root container = new())
			{
				ledger = container.Resolve<Ledger>();
			}

			await That(ledger.Disposed).IsTrue();
		}

		[Test]
		public async Task InheritedAsyncInitialization_RunsBeforeTheServiceIsHandedOut()
		{
			using TestShop.Root container = new();

			Inventory inventory = await container.ResolveAsync<Inventory>(Ct);

			await That(inventory.Loaded).IsTrue();
		}

		[Test]
		public async Task InheritedAsyncSafety_AllowsSyncResolutionAfterInitialization()
		{
			using LenientTestShop.Root container = new();
			await container.InitializeAsync(Ct);

			await That(container.Resolve<Inventory>().Loaded).IsTrue()
				.Because("the extending container validates and resolves with the AsyncSafety of its base");
		}

		[Test]
		public async Task InheritedEagerSingleton_IsBuiltWithTheRoot()
		{
			await That(() => new EagerShop.Root()).Throws<InvalidOperationException>()
				.Because("the production root builds its eager warm-up at construction");
		}

		[Test]
		public async Task ReplacedEagerSingleton_IsNotBuilt()
		{
			using EagerTestShop.Root container = new();

			await That(container.Resolve<IWarmup>()).Is<FakeWarmup>()
				.Because("replacing an eager registration drops its eager construction with it");
		}
	}

	public class KeysAndContexts
	{
		[Test]
		public async Task KeyedRegistration_ReplacesTheInheritedOneOfTheSameKey()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<IStore>("primary")).Is<FakeStore>();
			await That(container.Resolve<IStore>("archive")).Is<ArchiveStore>();
		}

		[Test]
		public async Task PlainRegistration_KeepsAnInheritedContextualOne()
		{
			using PlainContextTestShop.Root container = new();

			await That(container.Resolve<Barista>().Clock).Is<FakeClock>();
			await That(container.Resolve<Auditor>().Clock).Is<UtcClock>()
				.Because("a WhenInjectedInto registration has its own context key, which a plain registration does not replace");
		}

		[Test]
		public async Task ContextualRegistration_ReplacesTheInheritedOneForTheSameConsumer()
		{
			using ContextTestShop.Root container = new();

			await That(container.Resolve<Auditor>().Clock).Is<FakeClock>();
			await That(container.Resolve<Barista>().Clock).Is<SystemClock>();
		}
	}

	public class OpenGenerics
	{
		[Test]
		public async Task ClosedRegistration_ReplacesOnlyItsClosingOfAnInheritedOpenGeneric()
		{
			using TestShop.Root container = new();

			RepositoryUser user = container.Resolve<RepositoryUser>();

			await That(user.Orders).Is<FakeOrderRepository>();
			await That(user.Customers).Is<Repository<Customer>>();
		}

		[Test]
		public async Task OpenRegistration_ReplacesTheInheritedOpenRegistration()
		{
			using OpenTestShop.Root container = new();

			RepositoryUser user = container.Resolve<RepositoryUser>();

			await That(user.Orders).Is<InMemoryRepository<Order>>();
			await That(container.Resolve<IRepository<Customer>[]>()).HasCount(1)
				.Because("the inherited open registration is gone, so no closing of it joins the collection");
		}
	}

	public class Decorators
	{
		[Test]
		public async Task InheritedDecorator_WrapsTheReplacement()
		{
			using TestShop.Root container = new();

			await That(container.Resolve<IFormatter>()).Is<LoggingFormatter>()
				.Because("the base's decorator applies to whatever implements the service, and nothing removes it");
			await That(((LoggingFormatter)container.Resolve<IFormatter>()).Inner).Is<FakeFormatter>();
		}

		[Test]
		public async Task OwnDecorator_WrapsTheInheritedDecorators()
		{
			using DecoratedTestShop.Root container = new();

			IFormatter formatter = container.Resolve<IFormatter>();

			await That(formatter).Is<TracingFormatter>()
				.Because("the test's decorator goes outside production's, whatever the Order of either");
			await That(((TracingFormatter)formatter).Inner).Is<LoggingFormatter>();
			await That(((LoggingFormatter)((TracingFormatter)formatter).Inner).Inner).Is<PlainFormatter>();
		}
	}

	public class ImportedServices
	{
		[Test]
		public async Task OwnRegistration_ReplacesAnInheritedImportService()
		{
			using HostedTestShop.Root container = new();
			((IAwaitenExternalResolverHost)container).SetExternalResolver(new ClockResolver(new FakeClock()));

			await That(container.Resolve<SettingsReader>().Settings).Is<FakeSettings>()
				.Because("the test registers what production draws from the host, without an AWT175 contradiction");
		}

		[Test]
		public async Task OwnImportService_ReplacesAnInheritedRegistration()
		{
			FakeClock clock = new();
			using HostedTestShop.Root container = new();
			((IAwaitenExternalResolverHost)container).SetExternalResolver(new ClockResolver(clock));

			await That(container.Resolve<SettingsReader>().Clock).IsSameAs(clock)
				.Because("a per-test instance reaches the graph through the host, decided at compile time");
		}
	}

	public class CrossAssembly
	{
		[Test]
		public async Task OwnRegistration_ReplacesTheInheritedOne()
		{
			using SupportTestContainer.Root container = new();

			await That(container.Resolve<SupportReport>().Clock).Is<FakeSupportClock>();
		}

		[Test]
		public async Task InheritedModuleFactory_IsCalledAcrossTheAssemblyBoundary()
		{
			using SupportTestContainer.Root container = new();

			await That(container.Resolve<Awaiten.Tests.Support.SupportGreeter>().Greeting).IsEqualTo("hello from the support module");
		}

		[Test]
		public async Task InternalMembersOfTheBase_AreReachedThroughInternalsVisibleTo()
		{
			using SupportTestContainer.Root container = new();

			await That(container.Resolve<SupportGreeting>().Text).IsEqualTo("production");
			await That(container.Resolve<SupportWarmup>().Warmed).IsTrue();
		}

		[Test]
		public async Task InheritedScan_SweepsTheBaseAssembly()
		{
			using SupportTestContainer.Root container = new();
			using SupportBaseContainer.Root production = new();

			await That(container.Resolve<IEnumerable<ISupportBasePlugin>>().Count()).IsEqualTo(3)
				.Because("the scan sweeps the base's assembly, whose internal match the test assembly sees through InternalsVisibleTo");
			await That(production.Resolve<IEnumerable<ISupportBasePlugin>>().Count()).IsEqualTo(3);
		}
	}

	public interface IClock;
	public sealed class SystemClock : IClock;
	public sealed class FakeClock : IClock;
	public sealed class UtcClock : IClock;

	public sealed class Barista(IClock clock)
	{
		public IClock Clock { get; } = clock;
	}

	public sealed class Auditor(IClock clock)
	{
		public IClock Clock { get; } = clock;
	}

	public interface IExtendsPlugin;
	public sealed class AlphaPlugin : IExtendsPlugin;
	public sealed class BetaPlugin : IExtendsPlugin;

	public sealed class Greeter(string greeting)
	{
		public string Greeting { get; } = greeting;
	}

	public interface IPaymentGateway;
	public sealed class BankGateway : IPaymentGateway;
	public sealed class FakeGateway : IPaymentGateway;

	public interface IRandom;
	public sealed class SystemRandom : IRandom;
	public sealed class FixedRandomSource : IRandom;

	public interface IStore;
	public sealed class PrimaryStore : IStore;
	public sealed class ArchiveStore : IStore;
	public sealed class FakeStore : IStore;

	public sealed class Order;
	public sealed class Customer;
	public interface IRepository<T>;
	public sealed class Repository<T> : IRepository<T>;
	public sealed class InMemoryRepository<T> : IRepository<T>;
	public sealed class FakeOrderRepository : IRepository<Order>;

	public sealed class RepositoryUser(IRepository<Order> orders, IRepository<Customer> customers)
	{
		public IRepository<Order> Orders { get; } = orders;
		public IRepository<Customer> Customers { get; } = customers;
	}

	public interface IFormatter;
	public sealed class PlainFormatter : IFormatter;
	public sealed class FakeFormatter : IFormatter;

	public sealed class LoggingFormatter(IFormatter inner) : IFormatter
	{
		public IFormatter Inner { get; } = inner;
	}

	public sealed class TracingFormatter(IFormatter inner) : IFormatter
	{
		public IFormatter Inner { get; } = inner;
	}

	public interface INotifier;
	public sealed class EmailNotifier : INotifier;
	public sealed class SmsNotifier : INotifier;
	public sealed class FakeNotifier : INotifier;

	public interface IGrinder;
	public sealed class BurrGrinder : IGrinder;
	public sealed class BladeGrinder : IGrinder;

	public sealed class TestProbe;

	public sealed class Oven
	{
		public bool Preheated { get; set; }
	}

	public sealed class Menu
	{
		public IClock? Clock { get; set; }
	}

	public interface IAlarm;
	public sealed class BellAlarm : IAlarm;
	public sealed class FakeAlarm : IAlarm;

	public sealed class AlarmComposite(IEnumerable<IAlarm> members) : IAlarm
	{
		public IAlarm[] Members { get; } = members.ToArray();
	}

	public sealed class Ledger : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public sealed class Inventory : IInitializable
	{
		public bool Loaded { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Loaded = true;
			return Task.CompletedTask;
		}
	}

	public interface IWarmup;

	public sealed class ConnectionWarmup : IWarmup
	{
		public ConnectionWarmup() => throw new InvalidOperationException("The production warm-up must not run in a test.");
	}

	public sealed class FakeWarmup : IWarmup;

	public sealed class FakeSupportClock : ISupportBaseClock;

	public interface ISettings;
	public sealed class FakeSettings : ISettings;

	public sealed class SettingsReader(ISettings settings, IClock clock)
	{
		public ISettings Settings { get; } = settings;
		public IClock Clock { get; } = clock;
	}

	private sealed class ClockResolver(IClock clock) : IExternalResolver
	{
		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
		{
			instance = serviceType == typeof(IClock) ? clock : null;
			return instance is not null;
		}
	}

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[ImportService<ISettings>]
	[Singleton<SystemClock, IClock>]
	[Singleton<SettingsReader>]
	public static partial class HostedShop;

	[Container(Extends = typeof(HostedShop))]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<FakeSettings, ISettings>]
	[ImportService<IClock>]
	public static partial class HostedTestShop;

	[Module]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<BankGateway, IPaymentGateway>]
	public static class PaymentModule;

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Import(typeof(PaymentModule))]
	[Singleton<SystemClock, IClock>]
	[Singleton<Barista>]
	[Scan<IExtendsPlugin>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
	[Singleton<Greeter>(Factory = nameof(CreateGreeter))]
	[Singleton<IRandom>(Factory = nameof(CreateRandom))]
	[Singleton<PrimaryStore, IStore>(Key = "primary")]
	[Singleton<ArchiveStore, IStore>(Key = "archive")]
	[Singleton(typeof(Repository<>), typeof(IRepository<>))]
	[Singleton<RepositoryUser>]
	[Transient<PlainFormatter, IFormatter>]
	[Decorate<LoggingFormatter, IFormatter>(Order = 10)]
	[Singleton<EmailNotifier, INotifier>]
	[Singleton<SmsNotifier, INotifier>]
	[Singleton<BurrGrinder, IGrinder>]
	[Singleton<Oven>(OnActivated = nameof(Preheat))]
	[Singleton<Menu>]
	[InjectProperty<Menu>(nameof(Menu.Clock))]
	[Singleton<BellAlarm, IAlarm>]
	[Composite<AlarmComposite, IAlarm>]
	[Scoped<Order>]
	[Singleton<Ledger>]
	[Singleton<Inventory>]
	public static partial class ProductionShop
	{
		internal static Greeter CreateGreeter() => new("production");

		internal static IRandom CreateRandom() => new SystemRandom();

		internal static void Preheat(Oven oven) => oven.Preheated = true;
	}

	[Container(Extends = typeof(ProductionShop))]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<FakeClock, IClock>]
	[Singleton<FakeGateway, IPaymentGateway>]
	[Singleton<IRandom>(Instance = nameof(FixedRandom))]
	[Singleton<FakeStore, IStore>(Key = "primary")]
	[Singleton<FakeOrderRepository, IRepository<Order>>]
	[Transient<FakeFormatter, IFormatter>]
	[Singleton<FakeNotifier, INotifier>]
	[Singleton<BladeGrinder, IGrinder>(Fallback = Fallback.Silent)]
	[Singleton<TestProbe>]
	[Singleton<FakeAlarm, IAlarm>]
	public static partial class TestShop
	{
		public static IRandom FixedRandom { get; } = new FixedRandomSource();
	}

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<SystemClock, IClock>]
	[Singleton<ConnectionWarmup, IWarmup>(Eager = true)]
	public static partial class EagerShop;

	[Container(Extends = typeof(EagerShop))]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<FakeWarmup, IWarmup>]
	public static partial class EagerTestShop;

	[Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<Inventory>]
	public static partial class LenientShop;

	[Container(Extends = typeof(LenientShop))]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	public static partial class LenientTestShop;

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<SystemClock, IClock>]
	[Singleton<UtcClock, IClock>(WhenInjectedInto = typeof(Auditor))]
	[Singleton<Barista>]
	[Singleton<Auditor>]
	public static partial class ContextShop;

	[Container(Extends = typeof(ContextShop))]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<FakeClock, IClock>(WhenInjectedInto = typeof(Auditor))]
	public static partial class ContextTestShop;

	[Container(Extends = typeof(ContextShop))]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<FakeClock, IClock>]
	public static partial class PlainContextTestShop;

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton(typeof(Repository<>), typeof(IRepository<>))]
	[Singleton<RepositoryUser>]
	public static partial class OpenShop;

	[Container(Extends = typeof(OpenShop))]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton(typeof(InMemoryRepository<>), typeof(IRepository<>))]
	public static partial class OpenTestShop;

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Transient<PlainFormatter, IFormatter>]
	[Decorate<LoggingFormatter, IFormatter>(Order = 10)]
	public static partial class DecoratedShop;

	[Container(Extends = typeof(DecoratedShop))]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Decorate<TracingFormatter, IFormatter>]
	public static partial class DecoratedTestShop;

	[Container(Extends = typeof(SupportBaseContainer))]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<FakeSupportClock, ISupportBaseClock>]
	public static partial class SupportTestContainer;
}
