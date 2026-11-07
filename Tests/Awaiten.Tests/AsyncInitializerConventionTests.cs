using System.Collections.Generic;
using System.Threading;
using Awaiten.Tests.ContainerSupport;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of <c>[AsyncInitializer&lt;T&gt;(method)]</c> conventions: the container awaits the method of a
///     domain interface or base class once per instance, after the <c>OnActivated</c> hook and in declaration order,
///     passes it the resolve-time token and its graph dependencies, and treats a failure like a failed asynchronous hook.
///     Each container records into its own <see cref="Journal" /> singleton.
/// </summary>
public sealed partial class AsyncInitializerConventionTests
{
	public sealed class Journal
	{
		public List<string> Events { get; } = new();

		public CancellationToken? Token { get; set; }

		public bool FailNext { get; set; }
	}

	public interface IConnectable
	{
		Task ConnectAsync(Journal journal, CancellationToken cancellationToken);
	}

	public interface ICalibratable
	{
		ValueTask CalibrateAsync(Journal journal);
	}

	public interface IMachine : IConnectable;

	public abstract class Appliance
	{
		public virtual Task WarmUpAsync(Journal journal)
		{
			journal.Events.Add("appliance warmed");
			return Task.CompletedTask;
		}
	}

	public sealed class Grinder : ICalibratable, IMachine
	{
		public Task ConnectAsync(Journal journal, CancellationToken cancellationToken)
		{
			journal.Token = cancellationToken;
			journal.Events.Add("grinder connected");
			return Task.CompletedTask;
		}

		public ValueTask CalibrateAsync(Journal journal)
		{
			journal.Events.Add("grinder calibrated");
			return default;
		}
	}

	public sealed class Kettle : Appliance
	{
		public override Task WarmUpAsync(Journal journal)
		{
			journal.Events.Add("kettle warmed");
			return Task.CompletedTask;
		}
	}

	public sealed class Scale : IConnectable
	{
		async Task IConnectable.ConnectAsync(Journal journal, CancellationToken cancellationToken)
		{
			await Task.Yield();
			journal.Events.Add("scale connected");
		}
	}

	public sealed class Printer(Journal journal) : IConnectable, IDisposable
	{
		public async Task ConnectAsync(Journal log, CancellationToken cancellationToken)
		{
			await Task.Yield();
			if (log.FailNext)
			{
				log.FailNext = false;
				throw new InvalidOperationException("printer jammed");
			}

			log.Events.Add("printer connected");
		}

		public void Dispose() => journal.Events.Add("printer disposed");
	}

	public sealed class Register(Grinder grinder)
	{
		public Grinder Grinder => grinder;
	}

	[Test]
	public async Task Resolve_IsRefused_BecauseTheConventionMustBeAwaited()
	{
		using Shop.Root shop = new();

		await That(() => shop.Resolve<Kettle>()).Throws<InvalidOperationException>().WithMessage("*async initializer*ResolveAsync*").AsWildcard()
			.Because("a convention makes the kettle async-initialized, and the guidance names it among the causes");
		await That(shop.GetResolvability<Kettle>().Status).IsEqualTo(AwaitenResolvabilityStatus.AsyncOnly);
	}

	[Test]
	public async Task TheHookRunsFirst_ThenEachConventionInDeclarationOrder_EachMethodOnce()
	{
		using CancellationTokenSource cancellation = new();
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		await shop.ResolveAsync<Grinder>(cancellation.Token);

		await That(journal.Events).IsEqualTo(["grinder configured", "grinder connected", "grinder calibrated",])
			.Because("OnActivated runs before the conventions, which run in the container's declaration order, and IMachine reaches IConnectable.ConnectAsync, which runs once");
		await That(journal.Token).IsEqualTo(cancellation.Token)
			.Because("a CancellationToken parameter receives the token passed to ResolveAsync");
	}

	[Test]
	public async Task ABaseClassConvention_RunsTheOverride()
	{
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		await shop.ResolveAsync<Kettle>();

		await That(journal.Events).IsEqualTo(["kettle warmed",]);
	}

	[Test]
	public async Task AnExplicitInterfaceImplementation_Runs()
	{
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		await shop.ResolveAsync<Scale>();

		await That(journal.Events).IsEqualTo(["scale connected",]);
	}

	[Test]
	public async Task ADependentService_IsAsyncToo_AndSeesTheInitializedDependency()
	{
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		await That(() => shop.Resolve<Register>()).Throws<InvalidOperationException>()
			.Because("the taint of a convention propagates to every service that depends on the grinder");
		await shop.ResolveAsync<Register>();
		await That(journal.Events).Contains("grinder calibrated");
	}

	[Test]
	public async Task InitializeAsync_WarmsAConventionSingleton()
	{
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		await shop.InitializeAsync();

		await That(journal.Events).Contains("kettle warmed");
	}

	[Test]
	public async Task AFailingConvention_DisposesTheInstance_AndALaterResolveRetries()
	{
		using Shop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();
		journal.FailNext = true;
		using IAwaitenScope scope = shop.CreateScope();

		await That(() => scope.ResolveAsync<Printer>()).Throws<InvalidOperationException>().WithMessage("printer jammed");
		await That(journal.Events).IsEqualTo(["printer disposed",])
			.Because("the instance whose convention failed is disposed rather than leaked");

		await scope.ResolveAsync<Printer>();

		await That(journal.Events).IsEqualTo(["printer disposed", "printer connected",])
			.Because("the faulted resolve is not cached, so the next one builds and initializes a fresh instance");
	}

	[Test]
	public async Task AModuleConvention_InitializesTheModulesRegistrations_ButNotTheContainers()
	{
		using ModuleShop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		await shop.ResolveAsync<Kettle>();
		Scale scale = shop.Resolve<Scale>();

		await That(journal.Events).IsEqualTo(["kettle warmed",])
			.Because("the module's convention judges the module's kettle, and the container's own scale has no convention of its own");
		await That(scale).IsNotNull();
	}

	[Test]
	public async Task AnExtendingContainer_InheritsTheBaseConventions()
	{
		using ExtendingShop.Root shop = new();
		Journal journal = shop.Resolve<Journal>();

		await shop.ResolveAsync<Scale>();
		await shop.ResolveAsync<ITerminal>();

		await That(journal.Events).IsEqualTo(["scale connected", "hidden terminal connected",])
			.Because("the inherited convention initializes the extending container's own scale and the base's private terminal built through its factory");
	}

	[Test]
	public async Task AConventionOfAModuleInAnotherAssembly_InitializesItsInternalImplementations()
	{
		using SupportShop.Root shop = new();

		ISelfCompiledWarmup warmup = await shop.ResolveAsync<ISelfCompiledWarmup>();
		ISelfCompiledScanWarmup scanned = await shop.ResolveAsync<ISelfCompiledScanWarmup>();

		await That(warmup.IsInitialized).IsTrue()
			.Because("the consumer runs the public convention on the internal implementation it reaches through the generated factory");
		await That(scanned.IsInitialized).IsTrue()
			.Because("the module records which conventions its scan match has, since the consumer cannot see the match");
	}

	[Test]
	public async Task AConventionOfAModuleInAnotherAssembly_InitializesItsInternalDecoratorAndComposite()
	{
		using ShapeShop.Root shop = new();

		await That(() => shop.Resolve<IConventionSign>()).Throws<InvalidOperationException>()
			.Because("the module's convention makes its decorator async-initialized");
		await That((await shop.ResolveAsync<IConventionSign>()).Started).IsTrue();
		await That((await shop.ResolveAsync<IConventionDisplay>()).Started).IsTrue()
			.Because("the consumer runs the convention on the composite it builds through the module's generated factory");
	}

	[Test]
	public async Task AnInternalConventionOfAFriendModule_IsRunByTheFriend()
	{
		using CancellationTokenSource cancellation = new();
		using FriendShop.Root shop = new();

		IFriendConventionTerminal terminal = await shop.ResolveAsync<IFriendConventionTerminal>(cancellation.Token);
		IFriendConventionPrinter printer = await shop.ResolveAsync<IFriendConventionPrinter>();

		await That(terminal.Token).IsEqualTo(cancellation.Token)
			.Because("an assembly granted InternalsVisibleTo constructs the implementation itself and runs the internal convention");
		await That(printer.Started).IsTrue();
	}

	public interface ITerminal;

	[Container]
	[AsyncInitializer<IConnectable>(nameof(IConnectable.ConnectAsync))]
	[AsyncInitializer<ICalibratable>(nameof(ICalibratable.CalibrateAsync))]
	[AsyncInitializer<IMachine>(nameof(IMachine.ConnectAsync))]
	[AsyncInitializer<Appliance>(nameof(Appliance.WarmUpAsync))]
	[Singleton<Journal>]
	[Singleton<Grinder>(OnActivated = nameof(Configure))]
	[Singleton<Kettle>]
	[Singleton<Scale>]
	[Scoped<Printer>]
	[Transient<Register>]
	public static partial class Shop
	{
		private static async Task Configure(Grinder grinder, Journal journal)
		{
			await Task.Yield();
			journal.Events.Add("grinder configured");
		}
	}

	[Module]
	[AsyncInitializer<Appliance>(nameof(Appliance.WarmUpAsync))]
	[Singleton<Kettle>]
	public static partial class KettleModule;

	[Container]
	[Import(typeof(KettleModule))]
	[Singleton<Journal>]
	[Singleton<Scale>]
	public static partial class ModuleShop;

	[Container]
	[AsyncInitializer<IConnectable>(nameof(IConnectable.ConnectAsync))]
	[Singleton<Journal>]
	[Singleton<HiddenTerminal, ITerminal>]
	public static partial class BaseShop
	{
		private sealed class HiddenTerminal : ITerminal, IConnectable
		{
			public Task ConnectAsync(Journal journal, CancellationToken cancellationToken)
			{
				journal.Events.Add("hidden terminal connected");
				return Task.CompletedTask;
			}
		}
	}

	[Container(Extends = typeof(BaseShop))]
	[Singleton<Scale>]
	public static partial class ExtendingShop;

	[Container]
	[Import(typeof(SelfCompiledRegistrationModule))]
	[Import(typeof(SelfCompiledScanWarmupModule))]
	public static partial class SupportShop;

	[Container]
	[Import(typeof(SupportFriendConventionModule))]
	public static partial class FriendShop;

	[Container]
	[Import(typeof(ConventionShapeModule))]
	public static partial class ShapeShop;
}
