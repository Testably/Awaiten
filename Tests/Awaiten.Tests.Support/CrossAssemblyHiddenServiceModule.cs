using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>A grinder whose implementation depends on an internal bean store.</summary>
public interface IHiddenGrinder
{
	/// <summary>The internal bean store the grinder was constructed with.</summary>
	object Store { get; }

	/// <summary>Whether the module's activation hook ran on the bean store.</summary>
	bool StoreActivated { get; }

	/// <summary>Whether the module's release hook ran on the bean store.</summary>
	bool StoreReleased { get; }

	/// <summary>Whether the container disposed the bean store.</summary>
	bool StoreDisposed { get; }

	/// <summary>The internal silo the bean store was constructed with.</summary>
	object Silo { get; }
}

/// <summary>A roaster that shares the internal bean store with the grinder.</summary>
public interface IHiddenRoaster
{
	/// <summary>The internal bean store the roaster was constructed with.</summary>
	object Store { get; }
}

/// <summary>A public service the internal bean store is registered under as well.</summary>
public interface IHiddenStock;

/// <summary>A scoped service that holds an internal scoped cup.</summary>
public interface IHiddenCupHolder
{
	/// <summary>The internal cup of the scope.</summary>
	object Cup { get; }

	/// <summary>Whether the container disposed the cup.</summary>
	bool CupDisposed { get; }
}

/// <summary>A transient service that takes internal services through relationship types.</summary>
public interface IHiddenBarista
{
	/// <summary>Resolves the internal bean store through a <c>Func</c>.</summary>
	object StoreFromFunc();

	/// <summary>The internal bean store through a <c>Lazy</c>.</summary>
	object StoreFromLazy { get; }

	/// <summary>The names of the internal filters the barista received as a collection.</summary>
	IReadOnlyList<string> Filters { get; }

	/// <summary>Builds an owned internal cup, returns it, and disposes the handle.</summary>
	object UseOwnedCup(out bool disposedWithHandle);
}

/// <summary>A singleton that depends on an internal service initialized by a module convention.</summary>
public interface IHiddenBakery
{
	/// <summary>Whether the internal oven was initialized before the bakery received it.</summary>
	bool OvenStarted { get; }
}

/// <summary>A singleton that depends on an internal service activated by an asynchronous hook.</summary>
public interface IHiddenTeaMaker
{
	/// <summary>Whether the internal kettle was heated before the tea maker received it.</summary>
	bool KettleHeated { get; }
}

/// <summary>A singleton that depends on an internal disposable whose disposal the module suppresses.</summary>
public interface IHiddenLogUser
{
	/// <summary>Whether anything disposed the internal log.</summary>
	bool LogDisposed { get; }
}

/// <summary>The first service of an internal implementation registered under two services no type covers.</summary>
public interface IHiddenFront;

/// <summary>The second service of an internal implementation registered under two services no type covers.</summary>
public interface IHiddenBack;

/// <summary>A singleton that depends on an internal service whose implementation is only asynchronously disposable.</summary>
public interface IHiddenVault
{
	/// <summary>Whether the container disposed the internal lock asynchronously.</summary>
	bool LockDisposed { get; }
}

/// <summary>Counts the constructions of the internal eager singleton, so a test sees when it is built.</summary>
public static class HiddenEagerCounter
{
	private static int _constructed;

	/// <summary>How often the internal eager singleton was constructed.</summary>
	public static int Constructed => Volatile.Read(ref _constructed);

	internal static void Increment() => Interlocked.Increment(ref _constructed);
}

internal interface IHiddenStartable
{
	Task StartAsync(CancellationToken cancellationToken);
}

internal interface IHiddenFilter
{
	string Name { get; }
}

internal sealed class HiddenSilo;

internal sealed class HiddenBeanStore(HiddenSilo silo) : IHiddenStock, IDisposable
{
	public HiddenSilo Silo => silo;

	public bool Activated { get; set; }

	public bool Released { get; set; }

	public bool Disposed { get; private set; }

	public void Dispose() => Disposed = true;
}

internal sealed class HiddenGrinder(HiddenBeanStore store) : IHiddenGrinder
{
	public object Store => store;

	public bool StoreActivated => store.Activated;

	public bool StoreReleased => store.Released;

	public bool StoreDisposed => store.Disposed;

	public object Silo => store.Silo;
}

internal sealed class HiddenRoaster(HiddenBeanStore store) : IHiddenRoaster
{
	public object Store => store;
}

internal sealed class HiddenCup : IDisposable
{
	public bool Disposed { get; private set; }

	public void Dispose() => Disposed = true;
}

internal sealed class HiddenCupHolder(HiddenCup cup) : IHiddenCupHolder
{
	public object Cup => cup;

	public bool CupDisposed => cup.Disposed;
}

internal sealed class PaperFilter : IHiddenFilter
{
	public string Name => "paper";
}

internal sealed class MetalFilter : IHiddenFilter
{
	public string Name => "metal";
}

internal sealed class HiddenBarista(Func<HiddenBeanStore> store, Lazy<HiddenBeanStore> lazyStore, IEnumerable<IHiddenFilter> filters, Func<Owned<HiddenCup>> cups) : IHiddenBarista
{
	public object StoreFromFunc() => store();

	public object StoreFromLazy => lazyStore.Value;

	public IReadOnlyList<string> Filters { get; } = filters.Select(filter => filter.Name).ToArray();

	public object UseOwnedCup(out bool disposedWithHandle)
	{
		HiddenCup cup;
		using (Owned<HiddenCup> owned = cups())
		{
			cup = owned.Value;
		}

		disposedWithHandle = cup.Disposed;
		return cup;
	}
}

internal sealed class HiddenOven : IHiddenStartable
{
	public bool Started { get; private set; }

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await Task.Yield();
		Started = true;
	}
}

internal sealed class HiddenBakery(HiddenOven oven) : IHiddenBakery
{
	public bool OvenStarted { get; } = oven.Started;
}

internal sealed class HiddenKettle
{
	public bool Heated { get; set; }
}

internal sealed class HiddenTeaMaker(HiddenKettle kettle) : IHiddenTeaMaker
{
	public bool KettleHeated { get; } = kettle.Heated;
}

internal sealed class HiddenEagerProbe
{
	public HiddenEagerProbe() => HiddenEagerCounter.Increment();
}

internal sealed class HiddenKeptLog : IDisposable
{
	public bool Disposed { get; private set; }

	public void Dispose() => Disposed = true;
}

internal sealed class HiddenLogUser(HiddenKeptLog log) : IHiddenLogUser
{
	public bool LogDisposed => log.Disposed;
}

internal sealed class HiddenShared : IHiddenFront, IHiddenBack;

/// <summary>A singleton that depends on internal services the module produces through its factory and instance members.</summary>
public interface IHiddenWeigher
{
	/// <summary>The internal scale the module's factory built.</summary>
	object Scale { get; }

	/// <summary>The internal silo the factory received.</summary>
	object ScaleSilo { get; }

	/// <summary>Whether the container disposed the scale.</summary>
	bool ScaleDisposed { get; }

	/// <summary>The internal recipe book the module holds as a pre-built instance.</summary>
	object RecipeBook { get; }

	/// <summary>Whether anything disposed the recipe book.</summary>
	bool RecipeBookDisposed { get; }
}

/// <summary>A singleton that depends on an internal service the module produces through an async factory.</summary>
public interface IHiddenGaugeReader
{
	/// <summary>Whether the async factory finished before the reader received the gauge.</summary>
	bool GaugeCalibrated { get; }
}

/// <summary>Exposes the pre-built internal recipe book, so a test can compare it with what the container injects.</summary>
public static class HiddenRecipeBookAccess
{
	/// <summary>The module's pre-built recipe book.</summary>
	public static object RecipeBook => CrossAssemblyHiddenServiceModule.RecipeBook;
}

internal class HiddenScale(HiddenSilo silo)
{
	public HiddenSilo Silo => silo;
}

internal sealed class DisposableHiddenScale(HiddenSilo silo) : HiddenScale(silo), IDisposable
{
	public bool Disposed { get; private set; }

	public void Dispose() => Disposed = true;
}

internal sealed class HiddenRecipeBook : IDisposable
{
	public bool Disposed { get; private set; }

	public void Dispose() => Disposed = true;
}

internal sealed class HiddenWeigher(HiddenScale scale, HiddenRecipeBook recipeBook) : IHiddenWeigher
{
	public object Scale => scale;

	public object ScaleSilo => scale.Silo;

	public bool ScaleDisposed => ((DisposableHiddenScale)scale).Disposed;

	public object RecipeBook => recipeBook;

	public bool RecipeBookDisposed => recipeBook.Disposed;
}

internal sealed class HiddenGauge
{
	public bool Calibrated { get; set; }
}

internal sealed class HiddenGaugeReader(HiddenGauge gauge) : IHiddenGaugeReader
{
	public bool GaugeCalibrated { get; } = gauge.Calibrated;
}

/// <summary>
///     A <c>[Module]</c> whose public registrations depend on services it registers under internal types. Its build
///     emits a public handle per internal service, so a container in Awaiten.Tests depends on the handles without this
///     assembly granting <c>InternalsVisibleTo</c>, and still shares, initializes, hooks and disposes each internal
///     service like one it constructs itself.
/// </summary>
[Module]
[AsyncInitializer<IHiddenStartable>(nameof(IHiddenStartable.StartAsync))]
[Singleton<HiddenSilo>]
[Singleton<HiddenBeanStore>(OnActivated = nameof(ActivateStore), OnRelease = nameof(ReleaseStore))]
[Singleton<HiddenBeanStore, IHiddenStock>]
[Singleton<HiddenGrinder, IHiddenGrinder>]
[Singleton<HiddenRoaster, IHiddenRoaster>]
[Scoped<HiddenCup>]
[Scoped<HiddenCupHolder, IHiddenCupHolder>]
[Transient<PaperFilter, IHiddenFilter>]
[Transient<MetalFilter, IHiddenFilter>]
[Transient<HiddenBarista, IHiddenBarista>]
[Singleton<HiddenOven>]
[Singleton<HiddenBakery, IHiddenBakery>]
[Singleton<HiddenKettle>(OnActivated = nameof(HeatAsync))]
[Singleton<HiddenTeaMaker, IHiddenTeaMaker>]
[Singleton<HiddenEagerProbe>(Eager = true)]
[Singleton<HiddenKeptLog>(SuppressDisposal = true)]
[Singleton<HiddenLogUser, IHiddenLogUser>]
[Singleton<HiddenShared, IHiddenFront>]
[Singleton<HiddenShared, IHiddenBack>]
[Singleton<HiddenScale>(Factory = nameof(CreateScale))]
[Singleton<HiddenRecipeBook>(Instance = nameof(RecipeBook))]
[Singleton<HiddenWeigher, IHiddenWeigher>]
[Singleton<HiddenGauge>(Factory = nameof(CreateGaugeAsync))]
[Singleton<HiddenGaugeReader, IHiddenGaugeReader>]
public static partial class CrossAssemblyHiddenServiceModule
{
	internal static readonly HiddenRecipeBook RecipeBook = new();

	internal static HiddenScale CreateScale(HiddenSilo silo) => new DisposableHiddenScale(silo);

	internal static async Task<HiddenGauge> CreateGaugeAsync()
	{
		await Task.Yield();
		return new HiddenGauge { Calibrated = true, };
	}

	internal static void ActivateStore(HiddenBeanStore store) => store.Activated = true;

	internal static void ReleaseStore(HiddenBeanStore store) => store.Released = true;

	internal static async Task HeatAsync(HiddenKettle kettle)
	{
		await Task.Yield();
		kettle.Heated = true;
	}
}

internal sealed class HiddenLock : IAsyncDisposable
{
	public bool Disposed { get; private set; }

	public ValueTask DisposeAsync()
	{
		Disposed = true;
		return default;
	}
}

internal sealed class HiddenVault(HiddenLock vaultLock) : IHiddenVault
{
	public bool LockDisposed => vaultLock.Disposed;
}

/// <summary>A <c>[Module]</c> with an internal async-only disposable, apart so the other importers dispose synchronously.</summary>
[Module]
[Singleton<HiddenLock>]
[Singleton<HiddenVault, IHiddenVault>]
public static partial class CrossAssemblyHiddenAsyncDisposalModule;
