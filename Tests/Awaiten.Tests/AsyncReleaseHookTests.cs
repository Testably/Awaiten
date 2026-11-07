using System.Collections.Generic;
using Awaiten.Tests.ContainerSupport;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of an asynchronous <c>OnRelease</c> hook: a hook returning <c>Task</c> or <c>ValueTask</c> is
///     awaited by <c>DisposeAsync</c> in reverse creation order, before the instances are disposed. A synchronous
///     <c>Dispose</c> cannot await it, so it skips the hook and throws once the rest of the teardown is done, like it does
///     for an async-only disposable. Each container records into its own <see cref="Journal" /> singleton.
/// </summary>
public sealed partial class AsyncReleaseHookTests
{
	public sealed class Journal
	{
		public List<string> Events { get; } = new();
	}

	public sealed class CupPool
	{
		public int Returned { get; set; }
	}

	public sealed class Grinder;

	public sealed class Tray;

	public sealed class Till;

	public sealed class FlakyTill;

	public sealed class Cup;

	public sealed class Kettle(Journal journal) : IDisposable
	{
		public void Dispose() => journal.Events.Add("kettle disposed");
	}

	public sealed class Mug : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public interface ITill
	{
		bool Closed { get; }
	}

	[Test]
	public async Task DisposeAsync_AwaitsEachHook_InReverseCreationOrder_BeforeDisposal()
	{
		await using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();
		Shop.Scope scope = shop.CreateScope();
		scope.Resolve<Tray>();
		scope.Resolve<Till>();
		scope.Resolve<Kettle>();

		await scope.DisposeAsync();

		await That(journal.Events).IsEqualTo(["kettle cooled", "till closed", "tray wiped", "kettle disposed",])
			.Because("asynchronous and synchronous release hooks share one queue in reverse creation order, each awaited before any instance is disposed");
	}

	[Test]
	public async Task AnAsyncReleaseHook_LeavesTheServiceSynchronouslyResolvable()
	{
		await using Shop.Root shop = new();

		await That(shop.GetResolvability<Grinder>().Status).IsEqualTo(AwaitenResolvabilityStatus.Resolvable)
			.Because("only activation can make a service async-initialized; an asynchronous release runs at teardown");
	}

	[Test]
	public async Task ASingletonHook_RunsWhenTheRootIsDisposedAsynchronously()
	{
		Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();
		shop.Resolve<Grinder>();

		await shop.DisposeAsync();

		await That(journal.Events).IsEqualTo(["grinder flushed",]);
	}

	[Test]
	public async Task ATransientHook_RunsPerInstance_WithItsCapturedDependency()
	{
		await using Shop.Root shop = new();
		CupPool pool = shop.Resolve<CupPool>();
		Shop.Scope scope = shop.CreateScope();
		scope.Resolve<Cup>();
		scope.Resolve<Cup>();

		await scope.DisposeAsync();

		await That(pool.Returned).IsEqualTo(2)
			.Because("each transient queues its own release, with the pool captured at construction");
	}

	[Test]
	public async Task WithSuppressDisposal_TheHookRunsInsteadOfDisposal()
	{
		await using Shop.Root shop = new();
		Shop.Scope scope = shop.CreateScope();
		Mug mug = scope.Resolve<Mug>();

		await scope.DisposeAsync();

		await That(shop.Resolve<Journal>().Events).IsEqualTo(["mug returned",]);
		await That(mug.Disposed).IsFalse()
			.Because("SuppressDisposal hands the teardown to the release hook alone");
	}

	[Test]
	public async Task AFailingHook_DoesNotStopTheTeardown()
	{
		await using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();
		Shop.Scope scope = shop.CreateScope();
		scope.Resolve<Tray>();
		scope.Resolve<FlakyTill>();
		scope.Resolve<Kettle>();

		await That(async () => await scope.DisposeAsync()).Throws<InvalidOperationException>().WithMessage("till jammed")
			.Because("a single teardown failure is rethrown unchanged");
		await That(journal.Events).IsEqualTo(["kettle cooled", "tray wiped", "kettle disposed",])
			.Because("the remaining release hooks and every disposal still run after the faulted hook");
	}

	[Test]
	public async Task ASynchronousDispose_SkipsTheAsyncHook_AndThrowsAfterTheRestOfTheTeardown()
	{
		await using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();
		IAwaitenScope scope = shop.CreateScope();
		scope.Resolve<Tray>();
		scope.Resolve<Till>();
		scope.Resolve<Kettle>();

		AggregateException thrown = await That(() => scope.Dispose()).Throws<AggregateException>();

		await That(thrown.InnerExceptions).HasCount(2)
			.Because("a synchronous Dispose cannot await either asynchronous hook, so it reports each one, like each async-only disposable");
		await That(thrown.InnerExceptions[0]).IsExactly<InvalidOperationException>();
		await That(thrown.InnerExceptions[0].Message).Contains("*OnRelease*asynchronous*DisposeAsync*").AsWildcard();
		await That(journal.Events).IsEqualTo(["tray wiped", "kettle disposed",])
			.Because("the synchronous release hooks and the disposals still run, only the asynchronous hooks are skipped");
	}

	[Test]
	public async Task AnOwnedHandle_AwaitsTheHookOnDisposeAsync_AndRefusesASynchronousDispose()
	{
		await using Shop.Root shop = new();
		CupPool pool = shop.Resolve<CupPool>();

		Owned<Cup> first = shop.Resolve<Owned<Cup>>();
		await first.DisposeAsync();
		Owned<Cup> second = shop.Resolve<Owned<Cup>>();

		await That(pool.Returned).IsEqualTo(1)
			.Because("disposing the handle asynchronously awaits the release of the cup it owns");
		await That(() => second.Dispose()).Throws<InvalidOperationException>().WithMessage("*DisposeAsync*").AsWildcard();
		await That(pool.Returned).IsEqualTo(1);
	}

	[Test]
	public async Task AModuleHookInAnotherAssembly_IsAwaitedThroughItsWrapper()
	{
		await using ModuleShop.Root shop = new();
		ModuleShop.Scope scope = shop.CreateScope();
		IAsyncReleasedTill till = scope.Resolve<IAsyncReleasedTill>();
		IAsyncReleasedGrinder grinder = scope.Resolve<IAsyncReleasedGrinder>();

		await scope.DisposeAsync();

		await That(till.Closed).IsTrue()
			.Because("the module's public wrapper returns the internal hook's Task for the consumer to await");
		await That(grinder.Cleaned).IsTrue()
			.Because("the scan hook's wrapper returns its ValueTask too");
	}

	[Test]
	public async Task AFriendModuleHook_IsAwaitedThroughADirectCall()
	{
		await using FriendShop.Root shop = new();
		FriendShop.Scope scope = shop.CreateScope();
		IFriendAsyncReleasedTill till = scope.Resolve<IFriendAsyncReleasedTill>();

		await scope.DisposeAsync();

		await That(till.Closed).IsTrue()
			.Because("an assembly granted InternalsVisibleTo calls the internal hook directly and awaits its task");
	}

	[Test]
	public async Task ABaseHookOfAPrivateImplementation_IsAwaitedByTheExtendingContainer()
	{
		await using ExtendingShop.Root shop = new();
		ExtendingShop.Scope scope = shop.CreateScope();
		ITill till = scope.Resolve<ITill>();

		await scope.DisposeAsync();

		await That(till.Closed).IsTrue();
	}

	[Test]
	public async Task ABaseHookInAnotherAssembly_IsAwaitedByTheExtendingContainer()
	{
		await using ExtendingSupportShop.Root shop = new();
		ExtendingSupportShop.Scope scope = shop.CreateScope();
		ISupportAsyncReleasedTill till = scope.Resolve<ISupportAsyncReleasedTill>();
		SupportAsyncReleasedKettle kettle = scope.Resolve<SupportAsyncReleasedKettle>();

		await scope.DisposeAsync();

		await That(till.Closed).IsTrue()
			.Because("the base's internal wrapper of a private implementation's hook returns its Task");
		await That(kettle.Cooled).IsTrue()
			.Because("the base's internal wrapper of a private hook member returns its ValueTask");
	}

	[Container]
	[Singleton<Journal>]
	[Singleton<CupPool>]
	[Singleton<Grinder>(OnRelease = nameof(Flush))]
	[Scoped<Tray>(OnRelease = nameof(Wipe))]
	[Scoped<Till>(OnRelease = nameof(Close))]
	[Scoped<FlakyTill>(OnRelease = nameof(Jam))]
	[Scoped<Kettle>(OnRelease = nameof(Cool))]
	[Transient<Cup>(OnRelease = nameof(ReturnCup))]
	[Scoped<Mug>(OnRelease = nameof(ReturnMug), SuppressDisposal = true)]
	public static partial class Shop
	{
		private static async Task Flush(Grinder grinder, Journal journal)
		{
			await Task.Yield();
			journal.Events.Add("grinder flushed");
		}

		private static void Wipe(Tray tray, Journal journal) => journal.Events.Add("tray wiped");

		private static async ValueTask Close(Till till, Journal journal)
		{
			await Task.Yield();
			journal.Events.Add("till closed");
		}

		private static async Task Jam(FlakyTill till)
		{
			await Task.Yield();
			throw new InvalidOperationException("till jammed");
		}

		private static async Task Cool(Kettle kettle, Journal journal)
		{
			await Task.Yield();
			journal.Events.Add("kettle cooled");
		}

		private static async Task ReturnCup(Cup cup, CupPool pool)
		{
			await Task.Yield();
			pool.Returned++;
		}

		private static async Task ReturnMug(Mug mug, Journal journal)
		{
			await Task.Yield();
			journal.Events.Add("mug returned");
		}
	}

	[Container]
	[Import(typeof(AsyncReleaseModule))]
	public static partial class ModuleShop;

	[Container]
	[Import(typeof(SupportFriendAsyncReleaseModule))]
	public static partial class FriendShop;

	[Container]
	[Scoped<HiddenTill, ITill>(OnRelease = nameof(Close))]
	public static partial class BaseShop
	{
		private static async Task Close(HiddenTill till)
		{
			await Task.Yield();
			till.Closed = true;
		}

		private sealed class HiddenTill : ITill
		{
			public bool Closed { get; set; }
		}
	}

	[Container(Extends = typeof(BaseShop))]
	public static partial class ExtendingShop;

	[Container(Extends = typeof(SupportAsyncReleaseBaseContainer))]
	public static partial class ExtendingSupportShop;
}
