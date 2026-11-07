using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Awaiten.Tests.ContainerSupport;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of the registration shapes a module or a base container builds through a generated factory for
///     a container that cannot construct the implementation itself.
/// </summary>
public sealed partial class RegistrationFactoryShapeTests
{
	public interface IClock;

	public interface IGreeter
	{
		IClock KeyedClock { get; }

		IClock Clock { get; }
	}

	public interface IFormalGreeter : IGreeter;

	public sealed class UtcClock : IClock;

	public sealed class SystemClock : IClock;

	public interface IBell
	{
		IBell? Inner { get; }
	}

	public sealed class Bell : IBell
	{
		public IBell? Inner => null;
	}

	public interface IChime
	{
		int Count { get; }
	}

	public sealed class HighChime : IChime
	{
		public int Count => 1;
	}

	public sealed class LowChime : IChime
	{
		public int Count => 1;
	}

	public sealed class ModuleInAnotherAssembly
	{
		[Test]
		public async Task SeveralServices_ShareOneInstance()
		{
			using ShapeContainer.Root container = new();

			await That((object)container.Resolve<IShapeGreeter>()).IsSameAs(container.Resolve<IShapeFormalGreeter>())
				.Because("the factory returns the service that covers the other, so both resolve the one singleton");
		}

		[Test]
		public async Task AFromKeyParameter_ResolvesTheKeyedService()
		{
			using ShapeContainer.Root container = new();

			await That(container.Resolve<IShapeReport>().Clock).IsSameAs(container.Resolve<IShapeClock>("utc"))
				.Because("the factory's parameter carries the key of the constructor's parameter");
		}

		[Test]
		public async Task AnArgParameter_TakesTheResolveTimeArgument()
		{
			using ShapeContainer.Root container = new();

			Func<string, IShapeLabel> labels = container.Resolve<Func<string, IShapeLabel>>();

			await That(labels("espresso").Text).IsEqualTo("espresso");
		}

		[Test]
		public async Task AnInjectedProperty_IsFilled()
		{
			using ShapeContainer.Root container = new();

			await That(container.Resolve<IShapeNotifier>().Clock).IsSameAs(container.Resolve<IShapeClock>())
				.Because("the factory takes the property as a parameter and assigns it");
		}

		[Test]
		public async Task InternalFactoryAndInstanceMembers_AreCalledThroughTheirWrappers()
		{
			using ShapeContainer.Root container = new();

			await That(container.Resolve<IShapeMenu>()).IsSameAs(container.Resolve<IShapeMenu>());
			await That(container.Resolve<IShapeKettle>().Clock).IsSameAs(container.Resolve<IShapeClock>())
				.Because("the public wrapper passes the graph's clock on to the module's internal factory");
		}

		[Test]
		public async Task AnInternalDecorator_WrapsTheService()
		{
			using ShapeContainer.Root container = new();

			IShapeBell bell = container.Resolve<IShapeBell>();

			await That(bell.GetType().Name).IsEqualTo("LoudShapeBell");
			await That(bell.Inner?.GetType().Name).IsEqualTo("ShapeBell");
			await That(container.Resolve<IShapeBell>()).IsSameAs(bell)
				.Because("the decorator takes the lifetime of the singleton it wraps");
		}

		[Test]
		public async Task AnInternalComposite_FrontsTheOtherRegistrations()
		{
			using ShapeContainer.Root container = new();

			await That(container.Resolve<IShapeChime>().Count).IsEqualTo(2)
				.Because("the composite factory receives the collection of the two other chimes");
			await That(container.Resolve<IEnumerable<IShapeChime>>()).HasCount(2);
		}

		[Test]
		public async Task TheInternalHookOfAFactoryRegistration_RunsThroughItsWrapper()
		{
			using FactoryHookContainer.Root container = new();

			await That(container.Resolve<IFactoryHookedLamp>().Clock).IsSameAs(container.Resolve<IShapeClock>())
				.Because("the public wrapper passes the graph's clock on to the module's internal hook");
		}

		[Test]
		public async Task TheInternalAsyncHookOfAFactoryRegistration_IsAwaitedThroughItsWrapper()
		{
			using CancellationTokenSource cancellation = new();
			using FactoryHookContainer.Root container = new();

			IFactoryHookedRouter router = await container.ResolveAsync<IFactoryHookedRouter>(cancellation.Token);

			await That(router.Token).IsEqualTo(cancellation.Token)
				.Because("the wrapper returns the hook's Task and forwards the resolve-time token");
			await That(container.GetResolvability<IFactoryHookedRouter>().Status).IsEqualTo(AwaitenResolvabilityStatus.AsyncOnly)
				.Because("the wrapped asynchronous hook makes the router async-initialized");
		}
	}

	public sealed class ModuleHidingTheImplementation
	{
		[Test]
		public async Task TheFactory_TakesTheKeyedParameterAndTheInjectedProperty()
		{
			using HiddenContainer.Root container = new();

			IGreeter greeter = container.Resolve<IGreeter>();

			await That(greeter).IsSameAs(container.Resolve<IFormalGreeter>());
			await That(greeter.KeyedClock).IsExactly<UtcClock>();
			await That(greeter.Clock).IsExactly<SystemClock>()
				.Because("the container passes the property to the module's factory by name");
		}

		[Test]
		public async Task APrivateDecorator_WrapsTheService()
		{
			using HiddenContainer.Root container = new();

			IBell bell = container.Resolve<IBell>();

			await That(bell.GetType().Name).IsEqualTo("LoudBell");
			await That(bell.Inner).IsExactly<Bell>();
		}

		[Test]
		public async Task APrivateComposite_FrontsTheOtherRegistrations()
		{
			using HiddenContainer.Root container = new();

			await That(container.Resolve<IChime>().Count).IsEqualTo(2);
		}
	}

	public sealed class InternalModule
	{
		[Test]
		public async Task APrivateImplementation_TakesAnInternalParameterType()
		{
			using InternalModuleContainer.Root container = new();

			await That(container.Resolve<IBell>().Inner).IsNull()
				.Because("the factory of an internal module may take the internal clock its private bell needs");
		}
	}

	public sealed class BaseContainer
	{
		[Test]
		public async Task AnExtendingContainerInTheSameAssembly_BuildsThePrivateImplementation()
		{
			using TestShop.Root shop = new();

			IGreeter greeter = shop.Resolve<IGreeter>();

			await That(greeter).IsSameAs(shop.Resolve<IFormalGreeter>());
			await That(greeter.KeyedClock).IsExactly<UtcClock>();
			await That(greeter.Clock).IsExactly<SystemClock>();
		}

		[Test]
		public async Task AnExtendingContainerInTheSameAssembly_AppliesThePrivateDecorator()
		{
			using TestShop.Root shop = new();

			IBell bell = shop.Resolve<IBell>();

			await That(bell.GetType().Name).IsEqualTo("QuietBell");
			await That(bell.Inner).IsExactly<Bell>();
		}

		[Test]
		public async Task AnExtendingContainerInTheSameAssembly_AppliesThePrivateComposite()
		{
			using TestShop.Root shop = new();

			await That(shop.Resolve<IChime>().Count).IsEqualTo(2);
		}

		[Test]
		public async Task AnExtendingContainerInAnotherAssembly_BuildsThePrivateImplementation()
		{
			using SupportShapeTestContainer.Root shop = new();

			IShapeBaseReport report = shop.Resolve<IShapeBaseReport>();

			await That(report.KeyedClock).IsExactly<ShapeBaseUtcClock>();
			await That(report.Clock).IsExactly<ShapeBaseSystemClock>()
				.Because("the base's internal factory takes the keyed parameter and the injected property");
			await That(report.GetType().Name).IsEqualTo("AuditedReport")
				.Because("the base's private decorator wraps the report through its generated factory");
			await That(shop.Resolve<IShapeBaseChime>().Count).IsEqualTo(2)
				.Because("the base's private composite fronts both chimes through its generated factory");
		}

		[Test]
		public async Task AnExtendingContainerInAnotherAssembly_CallsThePrivateMembersThroughTheirWrappers()
		{
			using SupportShapeTestContainer.Root shop = new();

			await That(shop.Resolve<IShapeBaseMenu>()).IsNotNull();
			await That(shop.Resolve<ShapeBaseKettle>().Activated).IsTrue()
				.Because("the base wraps its private factory and hook, and the wrappers reach the test assembly through InternalsVisibleTo");
		}

		[Test]
		public async Task AnExtendingContainerInAnotherAssembly_AwaitsThePrivateAsyncHookThroughItsWrapper()
		{
			using CancellationTokenSource cancellation = new();
			using SupportShapeTestContainer.Root shop = new();

			IShapeBaseTerminal terminal = await shop.ResolveAsync<IShapeBaseTerminal>(cancellation.Token);

			await That(terminal.GetType().Name).IsEqualTo("GuardedTerminal")
				.Because("the base's private decorator wraps the terminal through its generated factory");
			await That(terminal.Inner).IsExactly<ShapeBaseTerminal>();
			await That(terminal.Token).IsEqualTo(cancellation.Token)
				.Because("the base's internal wrapper returns its private hook's Task and forwards the token");
			await That(shop.GetResolvability<IShapeBaseTerminal>().Status).IsEqualTo(AwaitenResolvabilityStatus.AsyncOnly)
				.Because("the wrapped asynchronous hook makes the terminal, and the decorator over it, async-initialized");
		}
	}

	[Container]
	[Import(typeof(FactoryShapeModule))]
	[Singleton<ShapeUtcClock, IShapeClock>(Key = "utc")]
	[Singleton<ShapeSystemClock, IShapeClock>]
	public static partial class ShapeContainer
	{
		public sealed class ShapeUtcClock : IShapeClock;

		public sealed class ShapeSystemClock : IShapeClock;
	}

	[Container]
	[Import(typeof(FactoryHookModule))]
	[Singleton<ShapeContainer.ShapeSystemClock, IShapeClock>]
	public static partial class FactoryHookContainer;

	[Module]
	[Singleton<HiddenGreetingModule.Greeter, IGreeter>]
	[Singleton<HiddenGreetingModule.Greeter, IFormalGreeter>]
	[Singleton<Bell, IBell>]
	[Decorate<HiddenGreetingModule.LoudBell, IBell>]
	[Transient<HighChime, IChime>]
	[Transient<LowChime, IChime>]
	[Composite<HiddenGreetingModule.AllChimes, IChime>]
	public static partial class HiddenGreetingModule
	{
		private sealed class AllChimes(IEnumerable<IChime> chimes) : IChime
		{
			public int Count => chimes.Sum(chime => chime.Count);
		}

		private sealed class LoudBell(IBell inner) : IBell
		{
			public IBell? Inner => inner;
		}

		private sealed class Greeter([FromKey("utc")] IClock keyedClock) : IFormalGreeter
		{
			public IClock KeyedClock => keyedClock;

			[Inject]
			public IClock Clock { get; set; } = null!;
		}
	}

	[Container]
	[Import(typeof(HiddenGreetingModule))]
	[Singleton<UtcClock, IClock>(Key = "utc")]
	[Singleton<SystemClock, IClock>]
	public static partial class HiddenContainer;

	[Container]
	[Singleton<UtcClock, IClock>(Key = "utc")]
	[Singleton<SystemClock, IClock>]
	[Singleton<Shop.Greeter, IGreeter>]
	[Singleton<Shop.Greeter, IFormalGreeter>]
	[Singleton<Bell, IBell>]
	[Decorate<Shop.QuietBell, IBell>]
	[Transient<HighChime, IChime>]
	[Transient<LowChime, IChime>]
	[Composite<Shop.AllChimes, IChime>]
	public static partial class Shop
	{
		private sealed class AllChimes(IEnumerable<IChime> chimes) : IChime
		{
			public int Count => chimes.Sum(chime => chime.Count);
		}

		private sealed class QuietBell(IBell inner) : IBell
		{
			public IBell? Inner => inner;
		}

		private sealed class Greeter([FromKey("utc")] IClock keyedClock) : IFormalGreeter
		{
			public IClock KeyedClock => keyedClock;

			[Inject]
			public IClock Clock { get; set; } = null!;
		}
	}

	[Container(Extends = typeof(Shop))]
	public static partial class TestShop;

	internal interface IInternalClock;

	internal sealed class InternalClock : IInternalClock;

	[Module]
	[Singleton<InternalClock, IInternalClock>]
	[Singleton<InternalBellModule.ClockedBell, IBell>]
	internal static partial class InternalBellModule
	{
		private sealed class ClockedBell(IInternalClock clock) : IBell
		{
			public IBell? Inner => clock is null ? this : null;
		}
	}

	[Container]
	[Import(typeof(InternalBellModule))]
	internal static partial class InternalModuleContainer;

	[Container(Extends = typeof(SupportShapeBaseContainer))]
	public static partial class SupportShapeTestContainer;
}
