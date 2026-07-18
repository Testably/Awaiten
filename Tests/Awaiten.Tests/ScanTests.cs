using System.Collections.Generic;
using System.Linq;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of assembly scanning: <c>[Scan]</c> registers every concrete type assignable to the
///     marker with the chosen lifetime (as itself and/or under the scanned marker interface), and an explicit
///     registration of the same type takes precedence over the scan for single resolution.
/// </summary>
public partial class ScanTests
{
	[Fact]
	public async Task Scan_RegistersEachConcreteImplementationWithTheChosenLifetime()
	{
		using ScanContainer.Root container = new();

		AlphaPlugin alpha = container.Resolve<AlphaPlugin>();
		BetaPlugin beta = container.Resolve<BetaPlugin>();

		await That(alpha).IsNotNull();
		await That(beta).IsNotNull();
		// AlphaPlugin is scanned as a singleton, so each resolve returns the same instance.
		await That(container.Resolve<AlphaPlugin>()).IsSameAs(alpha);
	}

	[Fact]
	public async Task Scan_RegistrationIsOverriddenByAnExplicitRegistration()
	{
		using ScanContainer.Root container = new();

		// The explicit transient registration of BetaPlugin wins over the scanned singleton, so each resolve
		// returns a fresh instance.
		await That(container.Resolve<BetaPlugin>()).IsNotSameAs(container.Resolve<BetaPlugin>());
	}

	[Fact]
	public async Task ScanAsMarker_MakesMatchesACollectionOfTheMarker()
	{
		using InterfaceScanContainer.Root container = new();

		IReadOnlyList<INotification> notifications = container.Resolve<NotificationHub>().Notifications;

		await That(notifications).HasCount(2);
		await That(notifications.Select(n => n.GetType()))
			.Contains(typeof(EmailNotification)).And.Contains(typeof(SmsNotification));
	}

	[Fact]
	public async Task ScanAsMarker_RegistersUnderTheInterfaceNotTheConcreteType()
	{
		using InterfaceScanContainer.Root container = new();

		// Reachable as the marker interface, but not as its own concrete type.
		await That(container.TryResolve(typeof(INotification), out _)).IsTrue();
		await That(container.TryResolve(typeof(EmailNotification), out _)).IsFalse();
	}

	[Fact]
	public async Task ScanAsSelfBitOrMarkerBit_RegistersBoth()
	{
		using SelfAndInterfaceScanContainer.Root container = new();

		// Resolvable both as its concrete type and as a member of the marker's collection.
		await That(container.Resolve<SalesReport>()).IsNotNull();
		await That(container.Resolve<IEnumerable<IReport>>().Count()).IsEqualTo(1);
	}

	public interface IPlugin;

	public sealed class AlphaPlugin : IPlugin;

	public sealed class BetaPlugin : IPlugin;

	[Container]
	[Scan(typeof(IPlugin), Lifetime = AwaitenLifetime.Singleton)]
	[Transient<BetaPlugin>]
	public static partial class ScanContainer;

	public interface INotification;

	public sealed class EmailNotification : INotification;

	public sealed class SmsNotification : INotification;

	public sealed class NotificationHub(IReadOnlyList<INotification> notifications)
	{
		public IReadOnlyList<INotification> Notifications { get; } = notifications;
	}

	[Container]
	[Scan(typeof(INotification), As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]
	[Singleton<NotificationHub>]
	public static partial class InterfaceScanContainer;

	public interface IReport;

	public sealed class SalesReport : IReport;

	[Container]
	[Scan(typeof(IReport), As = ScanAs.Self | ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]
	public static partial class SelfAndInterfaceScanContainer;

	[Fact]
	public async Task Scan_LifecycleHooks_RunForEveryMatch_WithGraphResolvedParameters()
	{
		HookProbe.Log.Clear();
		using (HookScanContainer.Root container = new())
		{
			container.Resolve<AlphaHooked>();
			container.Resolve<BetaHooked>();

			// The scan's OnActivated ran once each match was constructed, receiving its graph-resolved HookSettings
			// (a scan hook goes through the same parameter pipeline as an explicit registration's hook).
			await That(HookProbe.Log).Contains("activated:AlphaHooked:True");
			await That(HookProbe.Log).Contains("activated:BetaHooked:True");
			await That(HookProbe.Log).DoesNotContain("released:AlphaHooked")
				.Because("nothing is released while the container is alive");
		}

		// The scan's OnRelease ran for every match on disposal.
		await That(HookProbe.Log).Contains("released:AlphaHooked");
		await That(HookProbe.Log).Contains("released:BetaHooked");
	}

	public interface IHooked;

	public sealed class AlphaHooked : IHooked;

	public sealed class BetaHooked : IHooked;

	public sealed class HookSettings;

	private static class HookProbe
	{
		public static readonly List<string> Log = new();
	}

	[Container]
	[Singleton<HookSettings>]
	[Scan(typeof(IHooked), Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(Activated), OnRelease = nameof(Released))]
	public static partial class HookScanContainer
	{
		private static void Activated(IHooked instance, HookSettings settings)
			=> HookProbe.Log.Add($"activated:{instance.GetType().Name}:{settings is not null}");

		private static void Released(IHooked instance) => HookProbe.Log.Add("released:" + instance.GetType().Name);
	}

	[Fact]
	public async Task Scan_OpenGenericMarker_DispatchesAGenericHookWithTheClosedTypeArgument()
	{
		HookProbe.Log.Clear();
		using HookViewContainer.Root container = new();

		IHookView<IHookViewModel> view = container.Resolve<IHookView<IHookViewModel>>();

		await That(view).IsNotNull();
		// The scan's open marker closes as IHookView<IHookViewModel> for HookWindow, so the generic hook is dispatched
		// as WireView<IHookViewModel> - binding the matching view model, resolved from the graph, with no reflection.
		await That(HookProbe.Log).Contains("wired:HookWindow:HookViewModel")
			.Because("a generic scan hook binds its type argument from the match's closed marker form");
	}

	public interface IHookView<TViewModel>;

	public interface IHookViewModel;

	public sealed class HookViewModel : IHookViewModel;

	public sealed class HookWindow : IHookView<IHookViewModel>;

	[Container]
	[Singleton<HookViewModel, IHookViewModel>]
	[Scan(typeof(IHookView<>), As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(WireView))]
	public static partial class HookViewContainer
	{
		private static void WireView<TViewModel>(IHookView<TViewModel> view, TViewModel viewModel)
			=> HookProbe.Log.Add($"wired:{view.GetType().Name}:{viewModel!.GetType().Name}");
	}

	[Fact]
	public async Task Scan_OpenGenericMarker_RunsANonGenericHookForAMatchClosingTheMarkerMoreThanOnce()
	{
		HookProbe.Log.Clear();
		using DualPanelContainer.Root container = new();

		DualPanel panel = container.Resolve<DualPanel>();

		await That(panel).IsNotNull();
		// DualPanel closes IPanel<> at both int and string, but a non-generic hook needs no type argument, so it
		// is not ambiguous (no AWT198) and runs once for the single instance.
		await That(HookProbe.Log).Contains("tracked:DualPanel")
			.Because("a non-generic scan hook accepts every match as object, whatever closings the match implements");
	}

	public interface IPanel<TModel>;

	public sealed class DualPanel : IPanel<int>, IPanel<string>;

	[Container]
	[Scan(typeof(IPanel<>), As = ScanAs.Self | ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(Track))]
	public static partial class DualPanelContainer
	{
		private static void Track(object instance) => HookProbe.Log.Add("tracked:" + instance.GetType().Name);
	}

	[Fact]
	public async Task Scan_TwoScansHookingDifferentSlots_MergeSoBothHooksRun()
	{
		HookProbe.Log.Clear();
		using (MergeHookContainer.Root container = new())
		{
			container.Resolve<MergeWorker>();

			// MergeWorker is matched by both scans: one names OnActivated, the other OnRelease. The hooks occupy
			// different slots, so they merge instead of the second scan's being dropped (or conflicting, AWT199).
			await That(HookProbe.Log).Contains("merge-activated:MergeWorker");
		}

		await That(HookProbe.Log).Contains("merge-released:MergeWorker")
			.Because("the second scan's OnRelease merged onto the coalesced registration and ran at disposal");
	}

	public interface IMergeStart;

	public interface IMergeStop;

	public sealed class MergeWorker : IMergeStart, IMergeStop;

	[Container]
	[Scan(typeof(IMergeStart), Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(Started))]
	[Scan(typeof(IMergeStop), Lifetime = AwaitenLifetime.Singleton, OnRelease = nameof(Stopped))]
	public static partial class MergeHookContainer
	{
		private static void Started(object instance) => HookProbe.Log.Add("merge-activated:" + instance.GetType().Name);

		private static void Stopped(object instance) => HookProbe.Log.Add("merge-released:" + instance.GetType().Name);
	}

	[Fact]
	public async Task Scan_OpenGenericMarker_DispatchesAGenericReleaseHookWithTheClosedTypeArgument()
	{
		HookProbe.Log.Clear();
		using (UnwireViewContainer.Root container = new())
		{
			container.Resolve<IUnwireView<IUnwireViewModel>>();

			await That(HookProbe.Log).DoesNotContain("unwired:IUnwireViewModel")
				.Because("nothing is released while the container is alive");
		}

		// The marker closes as IUnwireView<IUnwireViewModel> for UnwireWindow, so the generic OnRelease hook is
		// dispatched as UnwireView<IUnwireViewModel> at disposal, exactly like a generic OnActivated at construction.
		await That(HookProbe.Log).Contains("unwired:IUnwireViewModel")
			.Because("a generic scan hook binds its type argument on the release slot too");
	}

	public interface IUnwireView<TViewModel>;

	public interface IUnwireViewModel;

	public sealed class UnwireWindow : IUnwireView<IUnwireViewModel>;

	[Container]
	[Scan(typeof(IUnwireView<>), As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton, OnRelease = nameof(UnwireView))]
	public static partial class UnwireViewContainer
	{
		private static void UnwireView<TViewModel>(IUnwireView<TViewModel> view)
			=> HookProbe.Log.Add("unwired:" + typeof(TViewModel).Name);
	}

	[Fact]
	public async Task Scan_OpenGenericMarker_DispatchesAGenericHookWithTwoTypeArguments()
	{
		HookProbe.Log.Clear();
		using PairBinderContainer.Root container = new();

		container.Resolve<IPairBinder<string, int>>();

		// The marker closes at two type arguments, so the hook is constructed with both: BindPair<string, int>.
		await That(HookProbe.Log).Contains("bound:String:Int32")
			.Because("a generic scan hook binds every type parameter of a multi-argument marker closing");
	}

	public interface IPairBinder<TSource, TTarget>;

	public sealed class PriceBinder : IPairBinder<string, int>;

	[Container]
	[Scan(typeof(IPairBinder<,>), As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(BindPair))]
	public static partial class PairBinderContainer
	{
		private static void BindPair<TSource, TTarget>(IPairBinder<TSource, TTarget> binder)
			=> HookProbe.Log.Add($"bound:{typeof(TSource).Name}:{typeof(TTarget).Name}");
	}

	[Fact]
	public async Task Scan_OpenGenericBaseClassMarker_DispatchesAGenericHookWithTheClosedTypeArgument()
	{
		HookProbe.Log.Clear();
		using BasePanelContainer.Root container = new();

		container.Resolve<OrdersPanel>();

		// OrdersPanel closes the base-class marker as PanelBase<OrdersPanelModel>, so the generic hook binds the
		// type argument from a base type exactly as it does from an implemented interface.
		await That(HookProbe.Log).Contains("panel:OrdersPanelModel")
			.Because("a marker closed through the inheritance chain feeds a generic hook like an interface closing");
	}

	public sealed class OrdersPanelModel;

	public abstract class PanelBase<TModel>;

	public sealed class OrdersPanel : PanelBase<OrdersPanelModel>;

	[Container]
	[Scan(typeof(PanelBase<>), Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(WirePanel))]
	public static partial class BasePanelContainer
	{
		private static void WirePanel<TModel>(PanelBase<TModel> panel)
			=> HookProbe.Log.Add("panel:" + typeof(TModel).Name);
	}

	[Fact]
	public async Task GenericScan_RegistersMatchesLikeTheTypeofForm()
	{
		using GenericScanContainer.Root container = new();

		// [Scan<IReport>] is the generic spelling of [Scan(typeof(IReport))]: resolvable both as the concrete
		// type and as a member of the marker's collection.
		await That(container.Resolve<SalesReport>()).IsNotNull();
		await That(container.Resolve<IEnumerable<IReport>>().Count()).IsEqualTo(1);
	}

	[Container]
	[Scan<IReport>(As = ScanAs.Self | ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]
	public static partial class GenericScanContainer;

	[Fact]
	public async Task ScanInAssembliesOf_RegistersMatchesFromTheReferencedAssembly()
	{
		using CrossAssemblyScanContainer.Root container = new();

		// GammaPlugin and DeltaPlugin live in the referenced Awaiten.Tests.Support assembly, not this one.
		await That(container.Resolve<GammaPlugin>()).IsNotNull();
		await That(container.Resolve<DeltaPlugin>()).IsNotNull();
		// Registered as singletons, so each resolve returns the same instance.
		await That(container.Resolve<GammaPlugin>()).IsSameAs(container.Resolve<GammaPlugin>());
	}

	[Fact]
	public async Task ScanInAssembliesOf_AsMarker_MakesMatchesACollectionOfTheMarker()
	{
		using CrossAssemblyInterfaceScanContainer.Root container = new();

		IReadOnlyList<ICrossAssemblyPlugin> plugins = container.Resolve<CrossAssemblyHub>().Plugins;

		// Exactly the two concrete, public, non-generic plugins: the support assembly's abstract PluginBase,
		// internal InternalPlugin and generic GenericPlugin<T> are all skipped by the scan.
		await That(plugins).HasCount(2);
		await That(plugins.Select(p => p.GetType()))
			.Contains(typeof(GammaPlugin)).And.Contains(typeof(DeltaPlugin));
	}

	[Fact]
	public async Task Scan_SeedsOpenGenericExpansionForScannedDependencies()
	{
		using OpenGenericSeedScanContainer.Root container = new();

		// OrderPlugin is discovered by the scan and its IRepository<Order> dependency is synthesized from the
		// open [Transient(typeof(Repository<>), typeof(IRepository<>))] registration.
		await That(container.Resolve<OrderPlugin>().Repository).IsNotNull();
	}

	public interface IRepository<T>;

	public sealed class Repository<T> : IRepository<T>;

	public sealed class Order;

	public interface IOrderPlugin;

	public sealed class OrderPlugin(IRepository<Order> repository) : IOrderPlugin
	{
		public IRepository<Order> Repository { get; } = repository;
	}

	[Container]
	[Scan(typeof(IOrderPlugin), Lifetime = AwaitenLifetime.Singleton)]
	[Transient(typeof(Repository<>), typeof(IRepository<>))]
	public static partial class OpenGenericSeedScanContainer;

	[Fact]
	public async Task ScanInAssembliesOf_IsOverriddenByAnExplicitRegistration()
	{
		using CrossAssemblyScanContainer.Root container = new();

		// The explicit transient registration of DeltaPlugin wins over the scanned singleton.
		await That(container.Resolve<DeltaPlugin>()).IsNotSameAs(container.Resolve<DeltaPlugin>());
	}

	public sealed class CrossAssemblyHub(IReadOnlyList<ICrossAssemblyPlugin> plugins)
	{
		public IReadOnlyList<ICrossAssemblyPlugin> Plugins { get; } = plugins;
	}

	[Container]
	[Scan(typeof(ICrossAssemblyPlugin), InAssembliesOf = new[] { typeof(ICrossAssemblyPlugin) }, Lifetime = AwaitenLifetime.Singleton)]
	[Transient<DeltaPlugin>]
	public static partial class CrossAssemblyScanContainer;

	[Container]
	[Scan(typeof(ICrossAssemblyPlugin), InAssembliesOf = new[] { typeof(ICrossAssemblyPlugin) }, As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]
	[Singleton<CrossAssemblyHub>]
	public static partial class CrossAssemblyInterfaceScanContainer;

	[Fact]
	public async Task ScanClosedTypesOf_RegistersEachMatchUnderItsClosedMarkerInterface()
	{
		using ClosedTypesOfScanContainer.Root container = new();

		// Each closed form of the marker is resolvable as that closed marker interface.
		await That(container.Resolve<IView<ViewModelOne>>()).IsNotNull();
		await That(container.Resolve<IView<ViewModelTwo>>()).IsNotNull();
	}

	[Fact]
	public async Task ScanClosedTypesOf_RegistersUnderTheClosedInterfaceNotTheConcreteType()
	{
		using ClosedTypesOfScanContainer.Root container = new();

		await That(container.TryResolve(typeof(IView<ViewModelOne>), out _)).IsTrue();
		await That(container.TryResolve(typeof(ViewOne), out _)).IsFalse();
	}

	[Fact]
	public async Task ScanClosedTypesOf_MakesEachClosedFormResolvableAsACollection()
	{
		using ClosedTypesOfScanContainer.Root container = new();

		// ViewOne, DecoratedViewOne and DualView close the marker at ViewModelOne; ViewTwo and DualView at ViewModelTwo.
		await That(container.Resolve<IEnumerable<IView<ViewModelOne>>>().Count()).IsEqualTo(3);
		await That(container.Resolve<IEnumerable<IView<ViewModelTwo>>>().Count()).IsEqualTo(2);
	}

	[Fact]
	public async Task ScanClosedTypesOf_AtSeveralTypeArguments_RegistersUnderEachClosedInterface()
	{
		using ClosedTypesOfScanContainer.Root container = new();

		// DualView : IView<ViewModelOne>, IView<ViewModelTwo>: resolvable as both closed forms.
		await That(container.Resolve<IEnumerable<IView<ViewModelOne>>>().Select(v => v.GetType()))
			.Contains(typeof(DualView));
		await That(container.Resolve<IEnumerable<IView<ViewModelTwo>>>().Select(v => v.GetType()))
			.Contains(typeof(DualView));
	}

	[Fact]
	public async Task ScanClosedTypesOf_AsSelf_RegistersTheConcreteType()
	{
		using ClosedTypesOfSelfScanContainer.Root container = new();

		// Self mode registers each match as its own concrete type, not under the closed interface.
		await That(container.Resolve<ViewOne>()).IsNotNull();
		await That(container.TryResolve(typeof(IView<ViewModelOne>), out _)).IsFalse();
	}

	[Fact]
	public async Task ScanClosedTypesOf_IsOverriddenByAnExplicitRegistration()
	{
		using ClosedTypesOfScanContainer.Root container = new();

		// The explicit transient registration of ViewTwo (under its closed interface) wins over the scan.
		await That(container.Resolve<IView<ViewModelTwo>>()).IsNotSameAs(container.Resolve<IView<ViewModelTwo>>());
	}

	[Fact]
	public async Task ScanClosedTypesOf_InAssembliesOf_RegistersMatchesFromTheReferencedAssembly()
	{
		using CrossAssemblyClosedTypesOfScanContainer.Root container = new();

		await That(container.Resolve<ICrossAssemblyView<CrossAssemblyViewModelOne>>()).Is<CrossAssemblyViewOne>();
		await That(container.Resolve<ICrossAssemblyView<CrossAssemblyViewModelTwo>>()).Is<CrossAssemblyViewTwo>();
	}

	public interface IView<TViewModel>;

	public sealed class ViewModelOne;

	public sealed class ViewModelTwo;

	public sealed class ViewOne : IView<ViewModelOne>;

	public sealed class ViewTwo : IView<ViewModelTwo>;

	public sealed class DecoratedViewOne : IView<ViewModelOne>;

	public sealed class DualView : IView<ViewModelOne>, IView<ViewModelTwo>;

	[Container]
	[Scan(typeof(IView<>), As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]
	[Transient<ViewTwo, IView<ViewModelTwo>>]
	public static partial class ClosedTypesOfScanContainer;

	[Container]
	[Scan(typeof(IView<>), Lifetime = AwaitenLifetime.Singleton)]
	public static partial class ClosedTypesOfSelfScanContainer;

	[Container]
	[Scan(typeof(ICrossAssemblyView<>), InAssembliesOf = new[] { typeof(ICrossAssemblyView<>) }, As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]
	public static partial class CrossAssemblyClosedTypesOfScanContainer;

	[Fact]
	public async Task ScanAsMatchingInterface_RegistersUnderTheConventionInterface()
	{
		using MatchingInterfaceScanContainer.Root container = new();

		// AlarmGadget : IGadget, IAlarmGadget registers only under its I + name interface, not the marker or itself.
		await That(container.Resolve<IAlarmGadget>()).Is<AlarmGadget>();
		await That(container.Resolve<ITimerGadget>()).Is<TimerGadget>();
		await That(container.TryResolve(typeof(AlarmGadget), out _)).IsFalse();
		await That(container.TryResolve(typeof(IGadget), out _)).IsFalse();
	}

	[Fact]
	public async Task ScanAsMarkerOrMatchingInterface_RegistersUnderBoth()
	{
		using MarkerAndMatchingScanContainer.Root container = new();

		// Marker | MatchingInterface unions the two exposures: each gadget joins the IGadget collection and stays
		// resolvable by its own interface.
		await That(container.Resolve<IAlarmGadget>()).Is<AlarmGadget>();
		await That(container.Resolve<IEnumerable<IGadget>>().Count()).IsEqualTo(2);
	}

	[Fact]
	public async Task MarkerlessScan_RegistersEachMatchUnderItsConventionInterface()
	{
		using MarkerlessScanContainer.Root container = new();

		// No marker: the scan matches by name pattern and registers each match under its I + name interface.
		await That(container.Resolve<IMklAlpha>()).Is<MklAlpha>();
		await That(container.Resolve<IMklBeta>()).Is<MklBeta>();
	}

	public interface IGadget;

	public interface IAlarmGadget;

	public sealed class AlarmGadget : IGadget, IAlarmGadget;

	public interface ITimerGadget;

	public sealed class TimerGadget : IGadget, ITimerGadget;

	[Container]
	[Scan<IGadget>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton)]
	public static partial class MatchingInterfaceScanContainer;

	[Container]
	[Scan<IGadget>(As = ScanAs.Marker | ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton)]
	public static partial class MarkerAndMatchingScanContainer;

	public interface IMklAlpha;

	public sealed class MklAlpha : IMklAlpha;

	public interface IMklBeta;

	public sealed class MklBeta : IMklBeta;

	[Container]
	[Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "Mkl*" }, Lifetime = AwaitenLifetime.Singleton)]
	public static partial class MarkerlessScanContainer;
}
