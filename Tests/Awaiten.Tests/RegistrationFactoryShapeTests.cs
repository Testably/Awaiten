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
		public async Task AnExtendingContainerInAnotherAssembly_BuildsThePrivateImplementation()
		{
			using SupportShapeTestContainer.Root shop = new();

			IShapeBaseReport report = shop.Resolve<IShapeBaseReport>();

			await That(report.KeyedClock).IsExactly<ShapeBaseUtcClock>();
			await That(report.Clock).IsExactly<ShapeBaseSystemClock>()
				.Because("the base's internal factory takes the keyed parameter and the injected property");
			await That(report.GetType().Name).IsEqualTo("AuditedReport")
				.Because("the base's private decorator wraps the report through its generated factory");
		}

		[Test]
		public async Task AnExtendingContainerInAnotherAssembly_CallsThePrivateMembersThroughTheirWrappers()
		{
			using SupportShapeTestContainer.Root shop = new();

			await That(shop.Resolve<IShapeBaseMenu>()).IsNotNull();
			await That(shop.Resolve<ShapeBaseKettle>().Activated).IsTrue()
				.Because("the base wraps its private factory and hook, and the wrappers reach the test assembly through InternalsVisibleTo");
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

	[Module]
	[Singleton<HiddenGreetingModule.Greeter, IGreeter>]
	[Singleton<HiddenGreetingModule.Greeter, IFormalGreeter>]
	[Singleton<Bell, IBell>]
	[Decorate<HiddenGreetingModule.LoudBell, IBell>]
	public static partial class HiddenGreetingModule
	{
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
	public static partial class Shop
	{
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

	[Container(Extends = typeof(SupportShapeBaseContainer))]
	public static partial class SupportShapeTestContainer;
}
