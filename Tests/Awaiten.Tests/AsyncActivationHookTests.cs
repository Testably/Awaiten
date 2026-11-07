using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Awaiten.Tests.ContainerSupport;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of an asynchronous <c>OnActivated</c> hook: a hook returning <c>Task</c> or <c>ValueTask</c>
///     makes its service async-initialized like <c>IAsyncInitializable</c> does, without the service referencing
///     Awaiten. The container awaits the hook once per instance, passes it the resolve-time token, and treats a failure
///     like a failed <c>InitializeAsync</c>. Each container records into its own <see cref="Journal" /> singleton.
/// </summary>
public sealed partial class AsyncActivationHookTests
{
	public sealed class Journal
	{
		public List<string> Events { get; } = new();

		public CancellationToken? Token { get; set; }

		public bool FailNext { get; set; }
	}

	public interface ITerminal
	{
		bool Connected { get; }
	}

	public sealed class PaymentTerminal : ITerminal
	{
		public bool Connected { get; set; }
	}

	public sealed class Register(ITerminal terminal)
	{
		public ITerminal Terminal => terminal;
	}

	public sealed class Oven(Journal journal) : IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			journal.Events.Add("initialized");
			return Task.CompletedTask;
		}
	}

	public sealed class Till;

	public sealed class Receipt;

	public sealed class FlakyPrinter(Journal journal) : IDisposable
	{
		public void Dispose() => journal.Events.Add("disposed");
	}

	public interface IRepository<T>
	{
		bool Opened { get; }
	}

	public sealed class Repository<T> : IRepository<T>
	{
		public bool Opened { get; set; }
	}

	public sealed class Ledger(IRepository<int> repository)
	{
		public IRepository<int> Repository => repository;
	}

	public interface IDevice
	{
		bool Ready { get; }
	}

	public sealed class Scale : IDevice
	{
		public bool Ready { get; set; }
	}

	public sealed class Grinder : IDevice
	{
		public bool Ready { get; set; }
	}

	public sealed class Counter(IAsyncEnumerable<IDevice> devices)
	{
		public IAsyncEnumerable<IDevice> Devices => devices;
	}

	[Test]
	public async Task Resolve_IsRefused_BecauseTheHookMustBeAwaited()
	{
		using Shop.Root shop = new();

		await That(() => shop.Resolve<ITerminal>()).Throws<InvalidOperationException>().WithMessage("*ResolveAsync*").AsWildcard()
			.Because("the asynchronous hook makes the terminal async-initialized, so a synchronous resolve would hand out an unconnected terminal");
		await That(shop.GetResolvability(typeof(ITerminal)).Status).IsEqualTo(AwaitenResolvabilityStatus.AsyncOnly);
	}

	[Test]
	public async Task ResolveAsync_AwaitsTheHookOnce_AndPassesTheToken()
	{
		using CancellationTokenSource cancellation = new();
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		ITerminal terminal = await shop.ResolveAsync<ITerminal>(cancellation.Token);

		await That(terminal.Connected).IsTrue();
		await That(journal.Token).IsEqualTo(cancellation.Token)
			.Because("the hook receives the token passed to ResolveAsync");
		await That(await shop.ResolveAsync<ITerminal>()).IsSameAs(terminal);
		await That(journal.Events).IsEqualTo(["connected",])
			.Because("a singleton is activated once, even when resolved again");
	}

	[Test]
	public async Task InitializeAsync_WarmsTheHookedSingleton_WithItsToken()
	{
		using CancellationTokenSource cancellation = new();
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		await shop.InitializeAsync(cancellation.Token);

		await That(journal.Events).Contains("connected");
		await That(journal.Token).IsEqualTo(cancellation.Token);
	}

	[Test]
	public async Task ADependentService_IsAsyncToo_AndSeesTheActivatedDependency()
	{
		using Shop.Root shop = new();

		await That(() => shop.Resolve<Register>()).Throws<InvalidOperationException>().WithMessage("*ResolveAsync*").AsWildcard()
			.Because("the taint of the hook propagates to every service that depends on the terminal");
		Register register = await shop.ResolveAsync<Register>();
		await That(register.Terminal.Connected).IsTrue();
	}

	[Test]
	public async Task WithIAsyncInitializable_TheHookRunsFirst()
	{
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		await shop.ResolveAsync<Oven>();

		await That(journal.Events).IsEqualTo(["preheated", "initialized",])
			.Because("OnActivated keeps its place before InitializeAsync whether it is synchronous or asynchronous");
	}

	[Test]
	public async Task AScopedService_IsActivatedByCreateScopeAsync_WithItsToken()
	{
		using CancellationTokenSource cancellation = new();
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		await using IAwaitenScope scope = await shop.CreateScopeAsync(cancellation.Token);

		await That(journal.Events).Contains("till opened")
			.Because("CreateScopeAsync warms the scope's async scoped services, including those with an asynchronous hook");
		await That(journal.Token).IsEqualTo(cancellation.Token);
		await That(await scope.ResolveAsync<Till>()).IsSameAs(await scope.ResolveAsync<Till>());
		await That(journal.Events.Count(entry => entry == "till opened")).IsEqualTo(1);
	}

	[Test]
	public async Task ATransientService_IsActivatedPerInstance()
	{
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		Receipt first = await shop.ResolveAsync<Receipt>();
		Receipt second = await shop.ResolveAsync<Receipt>();

		await That(first).IsNotSameAs(second);
		await That(journal.Events.Count(entry => entry == "receipt printed")).IsEqualTo(2);
	}

	[Test]
	public async Task AFailingHook_DisposesTheInstance_AndALaterResolveRetries()
	{
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();
		journal.FailNext = true;
		using IAwaitenScope scope = shop.CreateScope();

		await That(() => scope.ResolveAsync<FlakyPrinter>()).Throws<InvalidOperationException>().WithMessage("printer jammed")
			.Because("the hook's own exception surfaces unchanged, as for a failing InitializeAsync");
		await That(journal.Events).IsEqualTo(["disposed",])
			.Because("the instance whose activation failed is disposed rather than leaked");

		await scope.ResolveAsync<FlakyPrinter>();

		await That(journal.Events).IsEqualTo(["disposed", "printer ready",])
			.Because("the faulted resolve is not cached, so the next one builds and activates a fresh instance");
	}

	[Test]
	public async Task KeyedFactoryOpenGenericAndScanRegistrations_AwaitTheirHooks()
	{
		using Shop.Root shop = new();

		ITerminal keyed = await shop.ResolveAsync<ITerminal>("backup");
		IRepository<int> repository = (await shop.ResolveAsync<Ledger>()).Repository;
		IAsyncEnumerable<IDevice> devices = (await shop.ResolveAsync<Counter>()).Devices;
		List<IDevice> ready = new();
		await foreach (IDevice device in devices)
		{
			ready.Add(device);
		}

		await That(keyed.Connected).IsTrue();
		await That(repository.Opened).IsTrue()
			.Because("a generic hook is closed over the registration's type arguments and awaited like any other");
		await That(ready.Select(device => device.Ready)).IsEqualTo([true, true,])
			.Because("a scan hook applies to every match");
	}

	[Test]
	public async Task SyncAfterInit_ResolvesTheWarmedInstanceSynchronously()
	{
		using SyncAfterInitShop.Root shop = new();
		await shop.InitializeAsync();

		await That(shop.Resolve<ITerminal>().Connected).IsTrue();
	}

	[Test]
	public async Task AModuleHookInAnotherAssembly_IsAwaitedThroughItsWrapper()
	{
		using CancellationTokenSource cancellation = new();
		using ModuleShop.Root shop = new();

		IAsyncHookedTerminal terminal = await shop.ResolveAsync<IAsyncHookedTerminal>(cancellation.Token);
		IAsyncHookedPrinter printer = await shop.ResolveAsync<IAsyncHookedPrinter>();

		await That(terminal.Token).IsEqualTo(cancellation.Token)
			.Because("the module's public wrapper returns the internal hook's Task and forwards the token");
		await That(printer.Warmed).IsTrue();
		await That(() => shop.Resolve<IAsyncHookedPrinter>()).Throws<InvalidOperationException>()
			.Because("the consumer reads the asynchronous activation off the wrapper's return type");
	}

	[Test]
	public async Task ABaseHookOfAPrivateImplementation_IsAwaitedByTheExtendingContainer()
	{
		using CancellationTokenSource cancellation = new();
		using ExtendingShop.Root shop = new();

		ITerminal terminal = await shop.ResolveAsync<ITerminal>(cancellation.Token);

		await That(terminal.Connected).IsTrue();
		await That(shop.Resolve<Journal>().Token).IsEqualTo(cancellation.Token);
	}

	[Test]
	public async Task ABaseHookInAnotherAssembly_IsAwaitedByTheExtendingContainer()
	{
		using CancellationTokenSource cancellation = new();
		using ExtendingSupportShop.Root shop = new();

		ISupportAsyncTerminal terminal = await shop.ResolveAsync<ISupportAsyncTerminal>(cancellation.Token);

		await That(terminal.Token).IsEqualTo(cancellation.Token)
			.Because("the base's internal wrapper returns its private hook's Task and forwards the token");
	}

	[Container]
	[Singleton<Journal>]
	[Singleton<PaymentTerminal, ITerminal>(OnActivated = nameof(Connect))]
	[Singleton<PaymentTerminal, ITerminal>(Key = "backup", OnActivated = nameof(Connect))]
	[Singleton<Register>]
	[Singleton<Oven>(OnActivated = nameof(Preheat))]
	[Scoped<Till>(OnActivated = nameof(Open))]
	[Transient<Receipt>(OnActivated = nameof(Print))]
	[Scoped<FlakyPrinter>(Factory = nameof(CreatePrinter), OnActivated = nameof(Feed))]
	[Singleton(typeof(Repository<>), typeof(IRepository<>), OnActivated = nameof(OpenRepository))]
	[Transient<Ledger>]
	[Transient<Counter>]
	[Scan<IDevice>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton, OnActivated = nameof(Tare))]
	public static partial class Shop
	{
		private static async Task Connect(PaymentTerminal terminal, Journal journal, CancellationToken cancellationToken)
		{
			await Task.Yield();
			journal.Token = cancellationToken;
			journal.Events.Add("connected");
			terminal.Connected = true;
		}

		private static Task Preheat(Oven oven, Journal journal)
		{
			journal.Events.Add("preheated");
			return Task.CompletedTask;
		}

		private static ValueTask Open(Till till, Journal journal, CancellationToken cancellationToken)
		{
			journal.Token = cancellationToken;
			journal.Events.Add("till opened");
			return default;
		}

		private static Task Print(Receipt receipt, Journal journal)
		{
			journal.Events.Add("receipt printed");
			return Task.CompletedTask;
		}

		private static FlakyPrinter CreatePrinter(Journal journal) => new(journal);

		private static async Task Feed(FlakyPrinter printer, Journal journal)
		{
			await Task.Yield();
			if (journal.FailNext)
			{
				journal.FailNext = false;
				throw new InvalidOperationException("printer jammed");
			}

			journal.Events.Add("printer ready");
		}

		private static Task OpenRepository<T>(Repository<T> repository)
		{
			repository.Opened = true;
			return Task.CompletedTask;
		}

		private static async Task Tare(IDevice device)
		{
			await Task.Yield();
			switch (device)
			{
				case Scale scale:
					scale.Ready = true;
					break;
				case Grinder grinder:
					grinder.Ready = true;
					break;
			}
		}
	}

	[Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
	[Singleton<PaymentTerminal, ITerminal>(OnActivated = nameof(Connect))]
	public static partial class SyncAfterInitShop
	{
		private static Task Connect(PaymentTerminal terminal)
		{
			terminal.Connected = true;
			return Task.CompletedTask;
		}
	}

	[Container]
	[Import(typeof(AsyncHookModule))]
	public static partial class ModuleShop;

	[Container]
	[Singleton<Journal>]
	[Singleton<HiddenTerminal, ITerminal>(OnActivated = nameof(Connect))]
	public static partial class BaseShop
	{
		private static async Task Connect(HiddenTerminal terminal, Journal journal, CancellationToken cancellationToken)
		{
			await Task.Yield();
			journal.Token = cancellationToken;
			terminal.Connected = true;
		}

		private sealed class HiddenTerminal : ITerminal
		{
			public bool Connected { get; set; }
		}
	}

	[Container(Extends = typeof(BaseShop))]
	public static partial class ExtendingShop;

	[Container(Extends = typeof(SupportAsyncHookBaseContainer))]
	public static partial class ExtendingSupportShop;
}
