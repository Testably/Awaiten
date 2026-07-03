using System.Collections.Generic;
using System.Linq;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of assembly scanning: <c>[Scan]</c> registers every concrete type assignable to the
///     marker with the chosen lifetime - as itself and/or under its implemented interfaces - and an explicit
///     registration of the same type takes precedence over the scan for single resolution. The container and
///     services are nested types, so the enclosing class is <c>partial</c>.
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
	public async Task ScanAsImplementedInterfaces_MakesMatchesACollectionOfTheMarker()
	{
		using InterfaceScanContainer.Root container = new();

		IReadOnlyList<INotification> notifications = container.Resolve<NotificationHub>().Notifications;

		await That(notifications).HasCount(2);
		await That(notifications.Select(n => n.GetType()))
			.Contains(typeof(EmailNotification)).And.Contains(typeof(SmsNotification));
	}

	[Fact]
	public async Task ScanAsImplementedInterfaces_RegistersUnderTheInterfaceNotTheConcreteType()
	{
		using InterfaceScanContainer.Root container = new();

		// Reachable as the marker interface, but not as its own concrete type.
		await That(container.TryResolve(typeof(INotification), out _)).IsTrue();
		await That(container.TryResolve(typeof(EmailNotification), out _)).IsFalse();
	}

	[Fact]
	public async Task ScanAsSelfAndImplementedInterfaces_RegistersBoth()
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
	[Scan(typeof(INotification), As = ScanAs.ImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
	[Singleton<NotificationHub>]
	public static partial class InterfaceScanContainer;

	public interface IReport;

	public sealed class SalesReport : IReport;

	[Container]
	[Scan(typeof(IReport), As = ScanAs.SelfAndImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
	public static partial class SelfAndInterfaceScanContainer;

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
	public async Task ScanInAssembliesOf_AsImplementedInterfaces_MakesMatchesACollectionOfTheMarker()
	{
		using CrossAssemblyInterfaceScanContainer.Root container = new();

		IReadOnlyList<ICrossAssemblyPlugin> plugins = container.Resolve<CrossAssemblyHub>().Plugins;

		await That(plugins).HasCount(2);
		await That(plugins.Select(p => p.GetType()))
			.Contains(typeof(GammaPlugin)).And.Contains(typeof(DeltaPlugin));
	}

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
	[Scan(typeof(ICrossAssemblyPlugin), InAssembliesOf = new[] { typeof(ICrossAssemblyPlugin) }, As = ScanAs.ImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
	[Singleton<CrossAssemblyHub>]
	public static partial class CrossAssemblyInterfaceScanContainer;
}
