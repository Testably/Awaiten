using System.Collections.Generic;
using System.Linq;
using Awaiten.Tests.ContainerSupport;

namespace Awaiten.Tests;

public sealed partial class PrivateNestedImplementationTests
{
	public interface IGadget;

	public sealed class Extends
	{
		[Test]
		public async Task Lifetimes_AreTheBaseLifetimes()
		{
			using ExtendedLifetimeShop.Root shop = new();
			using ExtendedLifetimeShop.Scope first = shop.CreateScope();
			using ExtendedLifetimeShop.Scope second = shop.CreateScope();

			await That(NameOf(shop.Resolve<IClock>())).IsEqualTo("FakeClock");
			await That(first.Resolve<IClock>()).IsSameAs(shop.Resolve<IClock>());
			await That(first.Resolve<ILedger>()).IsSameAs(first.Resolve<ILedger>());
			await That(second.Resolve<ILedger>()).IsNotSameAs(first.Resolve<ILedger>());
			await That(shop.Resolve<IReceipt>()).IsNotSameAs(shop.Resolve<IReceipt>());
			await That(shop.Resolve<Tracker>().Events).Contains("eager")
				.Because("the extending container builds the base's private eager singleton with its own Root");
		}

		[Test]
		public async Task KeysCollectionsAndContextualBindings_AreKept()
		{
			using ExtendedRoutingShop.Root shop = new();

			await That(shop.Resolve<IEnumerable<IClock>>().Select(NameOf)).IsEqualTo(["SystemClock", "FakeClock",]).InAnyOrder();
			await That(NameOf(shop.Resolve<KeyedConsumer>().Clock)).IsEqualTo("KeyedClock");
			await That(NameOf(shop.Resolve<ContextConsumer>().Clock)).IsEqualTo("ContextClock")
				.Because("the factory call keeps the key and the WhenInjectedInto binding of the inherited registration");
		}

		[Test]
		public async Task Fallback_FillsTheGap()
		{
			using ExtendedFallbackShop.Root shop = new();

			await That(NameOf(shop.Resolve<IClock>())).IsEqualTo("FallbackClock");
		}

		[Test]
		public async Task AsyncInitialization_IsReadFromThePrivateType()
		{
			await using ExtendedAsyncShop.Root shop = new();

			await That(() => shop.Resolve<IClock>()).Throws<InvalidOperationException>()
				.WithMessage("*ResolveAsync*").AsWildcard()
				.Because("the factory returns the plain service, but the private type it builds is async-initializable");
			IClock clock = await shop.ResolveAsync<IClock>();
			await That(((IWarm)clock).Initialized).IsTrue();
			await That(await shop.ResolveAsync<IClock>()).IsSameAs(clock);
		}

		[Test]
		public async Task Disposal_FollowsThePrivateType()
		{
			await using ExtendedDisposalShop.Root shop = new();
			Tracker tracker = shop.Resolve<Tracker>();
			await using (ExtendedDisposalShop.Scope scope = shop.CreateScope())
			{
				scope.Resolve<ILedger>();
				scope.Resolve<IReceipt>();
				scope.Resolve<ITimer>();
			}

			await That(tracker.Events).IsEqualTo(["async disposed", "disposed",]).InAnyOrder()
				.Because("the scope disposes what the factories built, and SuppressDisposal still leaves the timer alone");
		}

		[Test]
		public async Task Hooks_RunThroughTheBaseWrappers()
		{
			Tracker tracker;
			using (ExtendedHookShop.Root shop = new())
			{
				tracker = shop.Resolve<Tracker>();
				shop.Resolve<IClock>();
				await That(tracker.Events).IsEqualTo(["activated FakeClock",]);
			}

			await That(tracker.Events).IsEqualTo(["activated FakeClock", "released FakeClock",])
				.Because("the base's private hooks receive the private type through the generated wrappers");
		}

		[Test]
		public async Task ScanMatches_PrivateToTheBase_AreRegistered()
		{
			using ExtendedGadgetShop.Root shop = new();

			await That(shop.Resolve<IEnumerable<IGadget>>().Select(NameOf)).IsEqualTo(["Kettle", "Toaster",]).InAnyOrder()
				.Because("the test graph holds the base's private scan matches, like the production graph");
		}

		[Test]
		public async Task ATypePrivateToAClassContainingTheBase_IsBuilt()
		{
			using ExtendedEnclosedShop.Root shop = new();

			await That(NameOf(shop.Resolve<IClock>())).IsEqualTo("EnclosedClock");
		}

		[Test]
		public async Task AReplacement_ReplacesThePrivateRegistration()
		{
			Tracker tracker;
			using (ReplacingHookShop.Root shop = new())
			{
				tracker = shop.Resolve<Tracker>();
				await That(shop.Resolve<IClock>()).IsExactly<SystemClock>();
			}

			await That(tracker.Events).IsEmpty()
				.Because("the replaced private registration and its hooks are gone");
		}

		[Test]
		public async Task ADecoratorOfTheExtendingContainer_WrapsThePrivateImplementation()
		{
			using DecoratingLifetimeShop.Root shop = new();

			IClock clock = shop.Resolve<IClock>();

			await That(clock).IsExactly<LoggingClock>();
			await That(NameOf(((IInner)clock).Inner)).IsEqualTo("FakeClock");
		}

		[Test]
		public async Task ABaseInAnotherAssembly_BuildsItsPrivateImplementationsForTheExtendingContainer()
		{
			ISupportPrivateTool tool;
			await using (SupportPrivateTestContainer.Root shop = new())
			{
				tool = shop.Resolve<ISupportPrivateTool>();
				await That(tool.Activated).IsTrue()
					.Because("the base's private hook runs through its internal wrapper, reached through [InternalsVisibleTo]");
				await That(shop.Resolve<IEnumerable<ISupportPrivatePlugin>>()).HasCount(1);
			}

			await That(tool.Disposed).IsTrue()
				.Because("the disposal is read off the private type in the referenced assembly");
		}
	}

	[Container(Extends = typeof(LifetimeShop))]
	public static partial class ExtendedLifetimeShop;

	[Container(Extends = typeof(LifetimeShop))]
	[Decorate<LoggingClock, IClock>]
	public static partial class DecoratingLifetimeShop;

	[Container(Extends = typeof(RoutingShop))]
	public static partial class ExtendedRoutingShop;

	[Container]
	[Singleton<FallbackClock, IClock>(Fallback = Fallback.Silent)]
	public static partial class FallbackShop
	{
		private sealed class FallbackClock : IClock;
	}

	[Container(Extends = typeof(FallbackShop))]
	public static partial class ExtendedFallbackShop;

	[Container(Extends = typeof(AsyncShop))]
	public static partial class ExtendedAsyncShop;

	[Container(Extends = typeof(DisposalShop))]
	public static partial class ExtendedDisposalShop;

	[Container(Extends = typeof(HookShop))]
	public static partial class ExtendedHookShop;

	[Container(Extends = typeof(HookShop))]
	[Singleton<SystemClock, IClock>]
	public static partial class ReplacingHookShop;

	[Container]
	[Scan<IGadget>(As = ScanAs.Marker)]
	public static partial class GadgetShop
	{
		private sealed class Kettle : IGadget;

		private sealed class Toaster : IGadget;
	}

	[Container(Extends = typeof(GadgetShop))]
	public static partial class ExtendedGadgetShop;

	public static partial class ClockFakes
	{
		private sealed class EnclosedClock : IClock;

		[Container]
		[Singleton<EnclosedClock, IClock>]
		public static partial class EnclosedShop;
	}

	[Container(Extends = typeof(ClockFakes.EnclosedShop))]
	public static partial class ExtendedEnclosedShop;

	[Container(Extends = typeof(SupportPrivateBaseContainer))]
	public static partial class SupportPrivateTestContainer;
}
