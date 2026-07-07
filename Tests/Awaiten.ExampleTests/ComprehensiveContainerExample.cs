using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Awaiten.ExampleTests;

/// <summary>
///     A deliberately exhaustive composition root, themed as a coffee shop so each registration's role
///     reads from its name: one <see cref="Menu" /> (singleton), one <see cref="Order" /> per customer
///     (scoped), a fresh <see cref="Receipt" /> each time (transient), an <see cref="EspressoMachine" /> that
///     warms up (async init), a <see cref="Cup" /> reached through <c>Owned&lt;T&gt;</c> (disposable), keyed
///     milk, decorators, a composite inspection, an external <c>[ImportService&lt;T&gt;]</c> card reader, and an
///     imported roastery module. One <see cref="CoffeeShop" /> container registers every kind, so the tests
///     exercise the full breadth of its resolution, scoping and disposal code paths. Types in the parent
///     <c>Awaiten</c> namespace are in scope without an explicit <c>using</c>.
/// </summary>
/// <remarks>
///     The shop runs under the strict-safety default: an async-initialized service is reached only through
///     <c>ResolveAsync</c>, and a build-on-demand disposable is withheld from by-type resolution and reached
///     through <c>Owned&lt;T&gt;</c>. The direct <c>Func&lt;disposable&gt;</c> and pragmatic
///     synchronous-after-init paths belong to other container <em>modes</em> and are covered by the small
///     <see cref="SelfServeKiosk" /> and <see cref="DriveThru" /> variants below. The
///     <see cref="DishStation" /> (an <c>IAsyncDisposable</c>-only service) is compiled only on modern target
///     frameworks: on <c>net48</c> the generated container is not <c>IAsyncDisposable</c>, and synchronously
///     disposing an async-only service throws by design.
/// </remarks>
public partial class ComprehensiveContainerExample
{
	// ---- Plain lifetimes -------------------------------------------------------------------------------

	// One menu for the whole shop.
	public interface IMenu
	{
		string Special { get; }
	}

	public sealed class Menu : IMenu
	{
		public string Special => "pumpkin-spice";
	}

	// One order per customer (scope).
	public interface IOrder;

	public sealed class Order : IOrder;

	// A fresh receipt every time.
	public sealed class Receipt;

	// ---- Open generic ----------------------------------------------------------------------------------

	// The pantry shelves any kind of ingredient, one shelf per ingredient type (open generic singleton).
	public interface IPantry<T>;

	public sealed class Pantry<T> : IPantry<T>;

	// A shelf is set up per order (open generic scoped); a label is printed fresh (open generic transient).
	public interface IShelf<T>;

	public sealed class Shelf<T> : IShelf<T>;

	public interface ILabel<T>;

	public sealed class Label<T> : ILabel<T>;

	// A closed consumer seeds the open-generic expansion for the int type argument: the generator only emits a
	// closed Pantry<int> / Shelf<int> / Label<int> for the arguments it can see at compile time, which is what
	// makes those closed services resolvable by type.
	public sealed class BeanBin
	{
		public BeanBin(IPantry<int> pantry, IShelf<int> shelf, ILabel<int> label)
		{
			Pantry = pantry;
			Shelf = shelf;
			Label = label;
		}

		public IPantry<int> Pantry { get; }

		public IShelf<int> Shelf { get; }

		public ILabel<int> Label { get; }
	}

	// ---- Factory and pre-built instance ----------------------------------------------------------------

	// Blended by a house recipe (a factory method) rather than a plain constructor.
	public sealed class HouseBlend
	{
		public string Roast { get; init; } = "";
	}

	// A cash drawer the shop is handed and hands back, but never builds or disposes.
	public sealed class CashDrawer;

	// Cold brew steeps slowly: it is produced by an asynchronous factory that receives the resolve-time
	// cancellation token, so it is async-tainted and reached through ResolveAsync.
	public sealed class ColdBrew
	{
		public ColdBrew(CancellationToken token) => Token = token;

		public CancellationToken Token { get; }
	}

	// The shop's books: one Ledger exposed under two service faces. Registering the same implementation under
	// both with the same factory shares a single instance across them.
	public interface IReadLedger;

	public interface IWriteLedger;

	public sealed class Ledger : IReadLedger, IWriteLedger;

	// ---- Asynchronous initialization (async-tainted) ---------------------------------------------------

	// The espresso machine must warm up before it can pour.
	public sealed class EspressoMachine : IAsyncInitializable
	{
		public bool Warm { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Warm = true;
			return Task.CompletedTask;
		}
	}

	// The grinder calibrates itself per order (scoped, async).
	public sealed class Grinder : IAsyncInitializable
	{
		public bool Calibrated { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Calibrated = true;
			return Task.CompletedTask;
		}
	}

	// A fresh jug of milk is steamed to temperature each time (transient, async).
	public sealed class MilkSteamer : IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	// ---- Disposable and async-disposable (scope-bounded) -----------------------------------------------

	// A tray is cleared away when the order (scope) ends.
	public interface ITray;

	public sealed class Tray : ITray, IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

#if NET8_0_OR_GREATER
	// The dish station needs an asynchronous rinse-down at close. An IAsyncDisposable-only service requires a
	// container that can DisposeAsync, so it is registered (and covered) only on frameworks whose generated
	// container implements IAsyncDisposable.
	public sealed class DishStation : IAsyncDisposable
	{
		public bool Disposed { get; private set; }

		public ValueTask DisposeAsync()
		{
			Disposed = true;
			return default;
		}
	}
#endif

	// A cup is a build-on-demand disposable: strict safety withholds it from by-type resolution, so it is
	// taken through Owned<Cup> / Func<Owned<Cup>>, which bound its disposal to a throwaway scope.
	public sealed class Cup : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	// ---- Keyed registrations + [FromKey] ---------------------------------------------------------------

	// Milk comes in named varieties; a drink maker asks for one by name.
	public interface IMilk
	{
		string Kind { get; }
	}

	public sealed class OatMilk : IMilk
	{
		public string Kind => "oat";
	}

	public sealed class WholeMilk : IMilk
	{
		public string Kind => "whole";
	}

	public sealed class LatteMaker
	{
		public LatteMaker([FromKey("oat")] IMilk milk) => Milk = milk;

		public IMilk Milk { get; }
	}

	// ---- Decorator chain ------------------------------------------------------------------------------

	// A drink is dressed up by wrapping the one below it: syrup(milk(espresso)).
	public interface IDrink
	{
		string Describe();
	}

	public sealed class Espresso : IDrink
	{
		public string Describe() => "espresso";
	}

	public sealed class SteamedMilk : IDrink
	{
		private readonly IDrink _inner;

		public SteamedMilk(IDrink inner) => _inner = inner;

		public string Describe() => $"milk({_inner.Describe()})";
	}

	public sealed class VanillaSyrup : IDrink
	{
		private readonly IDrink _inner;

		public VanillaSyrup(IDrink inner) => _inner = inner;

		public string Describe() => $"vanilla({_inner.Describe()})";
	}

	// ---- Composite ------------------------------------------------------------------------------------

	// A full inspection combines every individual quality check, never itself.
	public interface IQualityCheck
	{
		bool Passes();
	}

	public sealed class FreshnessCheck : IQualityCheck
	{
		public bool Passes() => true;
	}

	public sealed class TemperatureCheck : IQualityCheck
	{
		public bool Passes() => true;
	}

	public sealed class FullInspection : IQualityCheck
	{
		private readonly IQualityCheck[] _checks;

		public FullInspection(IQualityCheck[] checks) => _checks = checks;

		public int Count => _checks.Length;

		public bool Passes() => _checks.All(check => check.Passes());
	}

	// ---- Parameterized ([Arg]) ------------------------------------------------------------------------

	// A coffee is poured to a size chosen at order time; the menu is resolved from the graph.
	public sealed class Coffee
	{
		public Coffee([Arg] int ounces, IMenu menu)
		{
			Ounces = ounces;
			Menu = menu;
		}

		public int Ounces { get; }

		public IMenu Menu { get; }
	}

	// ---- Property injection (plain and deferred cycle) -------------------------------------------------

	// The chalkboard is filled in from the menu after it is hung.
	public sealed class Chalkboard
	{
		[Inject]
		public IMenu Menu { get; set; } = null!;
	}

	// Front and back of house each reference the other; the mutual cycle is broken by building and caching
	// both first, then wiring the deferred edges.
	public sealed class FrontOfHouse
	{
		[Inject(Deferred = true)]
		public BackOfHouse Peer { get; set; } = null!;
	}

	public sealed class BackOfHouse
	{
		[Inject(Deferred = true)]
		public FrontOfHouse Peer { get; set; } = null!;
	}

	// ---- External dependency ([ImportService<T>]) ------------------------------------------------------

	// The card reader is owned by the payment provider (the host), not the shop.
	public interface IPaymentGateway
	{
		string Provider { get; }
	}

	public sealed class Register
	{
		public Register(IPaymentGateway gateway) => Gateway = gateway;

		public IPaymentGateway Gateway { get; }
	}

	// ---- Relationship / collection fan-in --------------------------------------------------------------

	// The counter pulls together the whole relationship surface at once: every collection shape, a
	// Func<T> / Lazy<T> deferred factory, an Owned<T> handle and its factory, and a parameterized factory.
	public sealed class Counter
	{
		public Counter(
			IEnumerable<IQualityCheck> allChecks,
			IReadOnlyList<IQualityCheck> checkList,
			IQualityCheck[] checkArray,
			Func<Receipt> receiptPrinter,
			Lazy<IMenu> lazyMenu,
			Owned<Cup> cup,
			Func<Owned<Cup>> cupDispenser,
			Func<int, Coffee> coffeePourer)
		{
			AllChecks = allChecks;
			CheckList = checkList;
			CheckArray = checkArray;
			ReceiptPrinter = receiptPrinter;
			LazyMenu = lazyMenu;
			Cup = cup;
			CupDispenser = cupDispenser;
			CoffeePourer = coffeePourer;
		}

		public IEnumerable<IQualityCheck> AllChecks { get; }

		public IReadOnlyList<IQualityCheck> CheckList { get; }

		public IQualityCheck[] CheckArray { get; }

		public Func<Receipt> ReceiptPrinter { get; }

		public Lazy<IMenu> LazyMenu { get; }

		public Owned<Cup> Cup { get; }

		public Func<Owned<Cup>> CupDispenser { get; }

		public Func<int, Coffee> CoffeePourer { get; }
	}

	// The opening routine covers the async relationship shapes over the espresso machine: a hot Task<T> that
	// starts warming at construction, a Lazy<Task<T>> that warms on first touch, and a Func<Task<T>> that
	// warms per call. All three are deferred (the task is produced, not awaited, at construction), so this
	// service stays synchronously resolvable.
	public sealed class OpeningChecklist
	{
		public OpeningChecklist(
			Task<EspressoMachine> eagerWarmUp,
			Lazy<Task<EspressoMachine>> lazyWarmUp,
			Func<Task<EspressoMachine>> machineWarmer)
		{
			EagerWarmUp = eagerWarmUp;
			LazyWarmUp = lazyWarmUp;
			MachineWarmer = machineWarmer;
		}

		public Task<EspressoMachine> EagerWarmUp { get; }

		public Lazy<Task<EspressoMachine>> LazyWarmUp { get; }

		public Func<Task<EspressoMachine>> MachineWarmer { get; }
	}

	// ---- Module (imported roastery catalogue) ----------------------------------------------------------

	public interface IBeanSupplier;

	public sealed class BeanSupplier : IBeanSupplier;

	public sealed class DeliveryNote;

	// The grind setting: the roastery ships a house default, but the shop overrides it with its own.
	public interface IGrind;

	public sealed class HouseGrind : IGrind;

	public sealed class CustomGrind : IGrind;

	// The roaster: contributed with Fallback.Silent, so the roastery's choice stands when the shop registers none.
	public interface IRoaster;

	public sealed class HouseRoaster : IRoaster;

	[Module]
	// An unopposed overridable default: nothing else registers IBeanSupplier, so this is used.
	[Singleton<BeanSupplier, IBeanSupplier>(Fallback = Fallback.Warn)]
	// An overridable default the shop replaces: the container's own IGrind registration wins over this.
	[Singleton<HouseGrind, IGrind>(Fallback = Fallback.Warn)]
	// A Fallback.Silent contribution: added because the shop registers no IRoaster of its own.
	[Singleton<HouseRoaster, IRoaster>(Fallback = Fallback.Silent)]
	[Transient<DeliveryNote>]
	public static class RoasteryModule;

	// ---- Async collection members ----------------------------------------------------------------------

	// A pour on the tasting flight; one variety steeps asynchronously, so any collection of pours has an
	// async-tainted member.
	public interface IPour
	{
		string Style { get; }
	}

	public sealed class EspressoPour : IPour
	{
		public string Style => "espresso";
	}

	public sealed class ColdDripPour : IPour, IAsyncInitializable
	{
		public string Style => "cold-drip";

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	// Consumes the pours as an IAsyncEnumerable<T>: because a member is async-tainted, the collection awaits
	// each member, so this consumer is itself async-tainted and reached through ResolveAsync.
	public sealed class TastingFlight
	{
		public TastingFlight(IAsyncEnumerable<IPour> pours) => Pours = pours;

		public IAsyncEnumerable<IPour> Pours { get; }
	}

	// Consumes the pours as an awaited Task<IReadOnlyList<T>>: the awaited collection launders its members'
	// taint (they are awaited inside the produced task, not at construction), so this consumer stays
	// synchronously resolvable even though a member needs async initialization.
	public sealed class MenuBoard
	{
		public MenuBoard(Task<IReadOnlyList<IPour>> pours) => Pours = pours;

		public Task<IReadOnlyList<IPour>> Pours { get; }
	}

	// ---- The comprehensive container -------------------------------------------------------------------

	[Container]
	[Import(typeof(RoasteryModule))]
	[ImportService<IPaymentGateway>]
	[Singleton<Menu, IMenu>]
	[Scoped<Order, IOrder>]
	[Transient<Receipt>]
	[Singleton(typeof(Pantry<>), typeof(IPantry<>))]
	[Scoped(typeof(Shelf<>), typeof(IShelf<>))]
	[Transient(typeof(Label<>), typeof(ILabel<>))]
	[Transient<BeanBin>]
	[Singleton<HouseBlend>(Factory = nameof(BlendHouseBlend))]
	[Singleton<CashDrawer>(Instance = nameof(CashDrawerInstance))]
	[Singleton<ColdBrew>(Factory = nameof(SteepColdBrewAsync))]
	[Singleton<Ledger, IReadLedger>(Factory = nameof(OpenLedger))]
	[Singleton<Ledger, IWriteLedger>(Factory = nameof(OpenLedger))]
	[Singleton<CustomGrind, IGrind>]
	[Singleton<EspressoMachine>]
	[Scoped<Grinder>]
	[Transient<MilkSteamer>]
	[Scoped<Tray, ITray>]
#if NET8_0_OR_GREATER
	[Scoped<DishStation>]
#endif
	[Transient<Cup>]
	[Singleton<OatMilk, IMilk>(Key = "oat")]
	[Singleton<WholeMilk, IMilk>(Key = "whole")]
	[Singleton<LatteMaker>]
	[Singleton<Espresso, IDrink>]
	[Decorate<SteamedMilk, IDrink>(Order = 1)]
	[Decorate<VanillaSyrup, IDrink>(Order = 2)]
	[Singleton<FreshnessCheck, IQualityCheck>]
	[Singleton<TemperatureCheck, IQualityCheck>]
	[Composite<FullInspection, IQualityCheck>(Lifetime = AwaitenLifetime.Singleton)]
	[Transient<Coffee>]
	[Singleton<Chalkboard>]
	[Singleton<FrontOfHouse>]
	[Singleton<BackOfHouse>]
	[Singleton<Register>]
	[Transient<Counter>]
	[Singleton<OpeningChecklist>]
	[Singleton<EspressoPour, IPour>]
	[Singleton<ColdDripPour, IPour>]
	[Singleton<TastingFlight>]
	[Singleton<MenuBoard>]
	public static partial class CoffeeShop
	{
		// A pre-built instance the shop hands back but never constructs or disposes.
		internal static readonly CashDrawer CashDrawerInstance = new();

		// A static factory method; its parameters (none here) would resolve from the graph.
		private static HouseBlend BlendHouseBlend() => new() { Roast = "house" };

		// An asynchronous factory: its CancellationToken parameter receives the resolve-time token.
		private static Task<ColdBrew> SteepColdBrewAsync(CancellationToken cancellationToken)
			=> Task.FromResult(new ColdBrew(cancellationToken));

		// One factory named by two service registrations: the shared factory yields a single Ledger instance
		// exposed through both IReadLedger and IWriteLedger.
		private static Ledger OpenLedger() => new();
	}

	// ---- Alternate-mode variants (mutually exclusive with the strict default) --------------------------

	// LifetimeSafety.Loose: a build-on-demand disposable is resolvable by type and a Func<disposable> is
	// allowed, exercising the non-withheld dispatch and Func-over-disposable code paths (a self-serve kiosk
	// where the customer, not the shop, manages the cup's lifetime).
	[Container(LifetimeSafety = LifetimeSafety.Loose)]
	[Transient<Cup>]
	[Singleton<Menu, IMenu>]
	public static partial class SelfServeKiosk;

	// SyncResolveAfterInit: an async-initialized service also gets a delegating synchronous resolver that
	// blocks on the async path once warmed, exercising the pragmatic sync-after-init emission (a drive-thru
	// that serves synchronously once the machine is hot).
	[Container(SyncResolveAfterInit = true)]
	[Singleton<EspressoMachine>]
	public static partial class DriveThru;

	private sealed class StripeGateway : IPaymentGateway
	{
		public string Provider => "stripe";
	}

	// ---- Tests ----------------------------------------------------------------------------------------

	// The tests below tear their Root down with a synchronous `using`: the example also compiles for net48,
	// whose generated Root has no DisposeAsync surface. That is safe because the shop's only
	// IAsyncDisposable-only service, the scoped DishStation (net8.0+), is resolved solely in
	// ScopedAsyncDisposable_IsTornDownAsynchronouslyWithItsScope inside a child scope drained through
	// DisposeAsync, so a root never tracks one (AWT156 warns on what the disposed owner could track).
#pragma warning disable AWT156
	[Fact]
	public async Task Singleton_Scoped_Transient_ResolveAndShareCorrectly()
	{
		using CoffeeShop.Root shop = new();
		using IAwaitenScope order = shop.CreateScope();

		await That(shop.Resolve<IMenu>()).IsSameAs(shop.Resolve<IMenu>())
			.Because("a singleton is one instance per container");
		await That(order.Resolve<IOrder>()).IsNotSameAs(shop.CreateScope().Resolve<IOrder>())
			.Because("a scoped service is one instance per scope");
		await That(shop.Resolve<Receipt>()).IsNotSameAs(shop.Resolve<Receipt>())
			.Because("a transient is a fresh instance per resolve");
	}

	[Fact]
	public async Task OpenGeneric_Factory_And_Instance_Resolve()
	{
		using CoffeeShop.Root shop = new();

		await That(shop.Resolve<IPantry<int>>()).IsNotNull()
			.Because("a closed generic is resolvable once a consumer has seeded its expansion");
		await That(shop.Resolve<IShelf<int>>()).IsNotNull()
			.Because("scoped open generics expand and resolve just like singleton ones");
		await That(shop.Resolve<ILabel<int>>()).IsNotNull()
			.Because("transient open generics expand and resolve just like singleton ones");

		BeanBin bin = shop.Resolve<BeanBin>();
		await That(bin.Pantry).IsNotNull();
		await That(bin.Shelf).IsNotNull();
		await That(bin.Label).IsNotNull();

		await That(shop.Resolve<HouseBlend>().Roast).IsEqualTo("house");
		await That(shop.Resolve<CashDrawer>()).IsSameAs(CoffeeShop.CashDrawerInstance)
			.Because("a pre-built instance registration hands back the container member");

		await That((object)shop.Resolve<IReadLedger>()).IsSameAs(shop.Resolve<IWriteLedger>())
			.Because("one implementation registered under two services with the same factory shares one instance");
	}

	[Fact]
	public async Task AsyncInitialized_Services_ResolveThroughResolveAsync()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		using CoffeeShop.Root shop = new();
		await shop.InitializeAsync(ct);

		EspressoMachine machine = await shop.ResolveAsync<EspressoMachine>(ct);
		await That(machine.Warm).IsTrue()
			.Because("ResolveAsync constructs and initializes the async-tainted singleton");

		using IAwaitenScope order = await shop.CreateScopeAsync(ct);
		Grinder grinder = await order.ResolveAsync<Grinder>(ct);
		await That(grinder.Calibrated).IsTrue();

		MilkSteamer first = await shop.ResolveAsync<MilkSteamer>(ct);
		MilkSteamer second = await shop.ResolveAsync<MilkSteamer>(ct);
		await That(first).IsNotSameAs(second)
			.Because("an async transient is a fresh, initialized instance per resolve");
	}

	[Fact]
	public async Task ScopedDisposable_IsTornDownWithItsScope()
	{
		using CoffeeShop.Root shop = new();

		Tray tray;
		using (IAwaitenScope order = shop.CreateScope())
		{
			tray = (Tray)order.Resolve<ITray>();
			await That(tray.Disposed).IsFalse();
		}

		await That(tray.Disposed).IsTrue()
			.Because("a scoped IDisposable is disposed with its scope");
	}

#if NET8_0_OR_GREATER
	[Fact]
	public async Task ScopedAsyncDisposable_IsTornDownAsynchronouslyWithItsScope()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		await using CoffeeShop.Root shop = new();

		IAwaitenScope order = await shop.CreateScopeAsync(ct);
		DishStation station = order.Resolve<DishStation>();
		await That(station.Disposed).IsFalse();

		await ((IAsyncDisposable)order).DisposeAsync();
		await That(station.Disposed).IsTrue()
			.Because("a scoped IAsyncDisposable is disposed asynchronously with its scope");
	}
#endif

	[Fact]
	public async Task Keyed_Decorated_And_Composite_Resolve()
	{
		using CoffeeShop.Root shop = new();

		await That(shop.Resolve<LatteMaker>().Milk.Kind).IsEqualTo("oat")
			.Because("[FromKey] selects the keyed registration");
		await That(shop.Resolve<IDrink>().Describe()).IsEqualTo("vanilla(milk(espresso))")
			.Because("decorators chain by Order, the last declared being outermost");

		IQualityCheck inspection = shop.Resolve<IQualityCheck>();
		await That(inspection).Is<FullInspection>()
			.Because("the composite is the single public winner for the service");
		await That(((FullInspection)inspection).Count).IsEqualTo(2)
			.Because("the composite fans out to the other two members, never itself");
		await That((object)inspection).IsSameAs(shop.Resolve<IQualityCheck>())
			.Because("the composite carries an explicit Singleton lifetime, so it is shared");
	}

	[Fact]
	public async Task Relationships_Collections_And_Parameterized_Resolve()
	{
		using CoffeeShop.Root shop = new();

		Counter counter = shop.Resolve<Counter>();

		await That(counter.CheckArray.Length).IsEqualTo(2)
			.Because("the collection excludes the composite and holds the bare members");
		await That(counter.CheckList.Count).IsEqualTo(2);
		await That(counter.AllChecks.Count()).IsEqualTo(2);
		await That(counter.ReceiptPrinter()).IsNotSameAs(counter.ReceiptPrinter())
			.Because("a Func<T> over a transient builds a fresh instance per call");
		await That(counter.LazyMenu.Value).IsSameAs(shop.Resolve<IMenu>())
			.Because("a Lazy<T> over a singleton yields the shared instance");
		await That(counter.CoffeePourer(12).Ounces).IsEqualTo(12)
			.Because("a Func<TArg, T> forwards the runtime [Arg] argument");

		using (Owned<Cup> cup = counter.CupDispenser())
		{
			await That(cup.Value.Disposed).IsFalse();
		}

		await That(counter.Cup.Value).IsNotNull();
	}

	[Fact]
	public async Task Owned_DisposesOnlyItsOwnScope()
	{
		using CoffeeShop.Root shop = new();

		Owned<Cup> owned = shop.Resolve<Counter>().Cup;
		Cup cup = owned.Value;
		await That(cup.Disposed).IsFalse();

		owned.Dispose();
		await That(cup.Disposed).IsTrue()
			.Because("disposing the Owned handle disposes the throwaway scope backing it");
	}

	[Fact]
	public async Task PropertyInjection_Plain_And_DeferredCycle_AreWired()
	{
		using CoffeeShop.Root shop = new();

		await That(shop.Resolve<Chalkboard>().Menu).IsNotNull()
			.Because("a plain [Inject] property is filled from the graph");

		FrontOfHouse front = shop.Resolve<FrontOfHouse>();
		await That(front.Peer).IsNotNull();
		await That(front.Peer.Peer).IsSameAs(front)
			.Because("the deferred mutual cycle is wired back to the same cached singletons");
	}

	[Fact]
	public async Task AsyncFactoryRelationship_ProducesAnInitializedInstance()
	{
		using CoffeeShop.Root shop = new();

		OpeningChecklist checklist = shop.Resolve<OpeningChecklist>();

		await That((await checklist.EagerWarmUp).Warm).IsTrue()
			.Because("a hot Task<T> resolves and initializes the instance");
		await That((await checklist.LazyWarmUp.Value).Warm).IsTrue()
			.Because("a Lazy<Task<T>> produces the initializing task on first touch");
		await That((await checklist.MachineWarmer()).Warm).IsTrue()
			.Because("a Func<Task<T>> produces an initialized instance per call");
	}

	[Fact]
	public async Task AsyncFactory_WithCancellationToken_ReceivesTheResolveTimeToken()
	{
		using CoffeeShop.Root shop = new();
		using CancellationTokenSource cts = new();

		ColdBrew coldBrew = await shop.ResolveAsync<ColdBrew>(cts.Token);

		await That(coldBrew.Token).IsEqualTo(cts.Token)
			.Because("the async factory's CancellationToken parameter receives the resolve-time token");
	}

	[Fact]
	public async Task AsyncCollections_AwaitTheirAsyncTaintedMember()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		using CoffeeShop.Root shop = new();

		// IAsyncEnumerable<T> injection taints its consumer, so the tasting flight is reached through ResolveAsync
		// and awaits its cold-drip member as it materializes.
		TastingFlight flight = await shop.ResolveAsync<TastingFlight>(ct);
		int poured = 0;
		await foreach (IPour pour in flight.Pours)
		{
			_ = pour;
			poured++;
		}

		await That(poured).IsEqualTo(2)
			.Because("the async collection materializes every registered pour, awaiting the async member");

		// The awaited Task<IReadOnlyList<T>> launders the taint, so the menu board stays synchronously
		// resolvable while its produced task still awaits the async member.
		MenuBoard board = shop.Resolve<MenuBoard>();
		IReadOnlyList<IPour> pours = await board.Pours;
		await That(pours.Count).IsEqualTo(2)
			.Because("the awaited collection produces every registered pour once its task completes");
	}

	[Fact]
	public async Task ExternalDependency_ResolvesThroughTheWiredResolver()
	{
		using CoffeeShop.Root shop = new();
		((IExternalResolverHost)shop).ExternalResolver = new GatewayResolver();

		await That(shop.Resolve<Register>().Gateway.Provider).IsEqualTo("stripe")
			.Because("an [ImportService<T>] dependency is satisfied by the wired external resolver");
	}

	[Fact]
	public async Task ImportedModule_ContributesItsRegistrations()
	{
		using CoffeeShop.Root shop = new();

		await That(shop.Resolve<IBeanSupplier>()).Is<BeanSupplier>()
			.Because("the imported module's overridable default fills the gap");
		await That(shop.Resolve<DeliveryNote>()).IsNotSameAs(shop.Resolve<DeliveryNote>())
			.Because("the imported transient registration is resolvable and fresh per call");
		await That(shop.Resolve<IGrind>()).Is<CustomGrind>()
			.Because("the container's own registration overrides the module's overridable default");
		await That(shop.Resolve<IRoaster>()).Is<HouseRoaster>()
			.Because("a module Fallback.Silent registration stands when the container registers none of its own");
	}
#pragma warning restore AWT156

	[Fact]
	public async Task SelfServeKiosk_ResolvesADisposableByTypeAndThroughAFunc()
	{
		using SelfServeKiosk.Root kiosk = new();

		await That(kiosk.Resolve<Cup>()).IsNotNull()
			.Because("loose safety allows by-type resolution of a build-on-demand disposable");

		// Invoke the resolved factory rather than asserting on the delegate itself (a Func handed to That is
		// treated as a delegate expectation): loose safety allows a Func over a disposable.
		Func<Cup> dispenser = kiosk.Resolve<Func<Cup>>();
		await That(dispenser()).IsNotNull();
	}

	[Fact]
	public async Task DriveThru_ResolvesAnAsyncServiceSynchronouslyAfterWarmUp()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		using DriveThru.Root driveThru = new();
		await driveThru.InitializeAsync(ct);

		await That(driveThru.Resolve<EspressoMachine>().Warm).IsTrue()
			.Because("SyncResolveAfterInit serves the warmed async singleton synchronously");
	}

	private sealed class GatewayResolver : IExternalResolver
	{
		public bool TryResolve(Type serviceType, object? serviceKey, out object? instance)
		{
			if (serviceType == typeof(IPaymentGateway))
			{
				instance = new StripeGateway();
				return true;
			}

			instance = null;
			return false;
		}
	}
}
