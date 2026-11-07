using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Awaiten.Tests.ContainerSupport;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of a <c>[Module]</c> in Awaiten.Tests.ContainerSupport, which grants this assembly
///     <c>InternalsVisibleTo</c>, whose registrations its generated factories cannot support. The module builds without
///     those factories, and the container here constructs the internal implementations itself.
/// </summary>
public partial class FriendModuleTests
{
	[Test]
	public async Task ConstructsAnImplementationWithAnInternalDependency()
	{
		using FriendContainer.Root container = new();

		IFriendGreeter greeter = container.Resolve<IFriendGreeter>();

		await That(greeter.Formatter.GetType().Name).IsEqualTo("FriendFormatter")
			.Because("the container constructs the internal implementation and its internal dependency directly");
	}

	[Test]
	public async Task SharesAnImplementationAcrossItsServices()
	{
		using FriendContainer.Root container = new();

		await That((object)container.Resolve<IFriendFront>()).IsSameAs(container.Resolve<IFriendBack>())
			.Because("the container coalesces both registrations on the implementation it constructs");
	}

	[Test]
	public async Task HonorsAFromKeyParameter()
	{
		using FriendContainer.Root container = new();

		await That(container.Resolve<IFriendReport>().Clock).IsSameAs(container.Resolve<IFriendClock>("utc"))
			.Because("the container reads the [FromKey] off the constructor it calls");
	}

	[Test]
	public async Task RegistersAScanMatchWithAnInternalDependency()
	{
		using FriendScanContainer.Root container = new();

		IFriendRoaster roaster = container.Resolve<IFriendRoaster>();

		await That(roaster.Secret.GetType().Name).IsEqualTo("FriendSecret")
			.Because("the container constructs the scanned internal implementation and its internal dependency directly");
	}

	[Test]
	public async Task SharesAScanMatchAcrossTheServicesBothScansExposeItAs()
	{
		using FriendScanContainer.Root container = new();

		IFriendPlugin[] plugins = container.Resolve<IEnumerable<IFriendPlugin>>().ToArray();

		await That(plugins).HasCount(2)
			.Because("each match joins the marker's collection once");
		await That((object)container.Resolve<IFriendGrinder>()).IsSameAs(plugins.Single(plugin => plugin is IFriendGrinder))
			.Because("the container coalesces the match's registrations on the implementation it constructs, as a scan would");
		await That((object)container.Resolve<IFriendRoaster>()).IsSameAs(plugins.Single(plugin => plugin is IFriendRoaster));
	}

	[Test]
	public async Task ConstructsTheImplementationOfAModuleThatIsNotPartial()
	{
		using FriendShapeContainer.Root container = new();

		await That(container.Resolve<IFriendWelcome>().GetType().Name).IsEqualTo("FriendWelcome")
			.Because("the container constructs the internal implementation of a module that cannot carry a factory");
	}

	[Test]
	public async Task ConstructsTheImplementationOfAGenericModule()
	{
		using FriendGenericContainer.Root container = new();

		await That(container.Resolve<IFriendWelcome>().GetType().Name).IsEqualTo("FriendWelcome")
			.Because("the container constructs the internal implementation of a generic module itself");
	}

	[Test]
	public async Task CallsAHookWithAnInternalDependency()
	{
		using FriendHookContainer.Root container = new();

		await That(container.Resolve<IFriendActivated>().Activated).IsTrue()
			.Because("the container calls the module's internal hook directly instead of a wrapper the module could not generate");
		await That(container.Resolve<IFriendHookedPlugin>().Activated).IsTrue()
			.Because("the container registers the scan match itself and calls its internal hook directly");
	}

	[Test]
	public async Task AwaitsAnAsyncHookWithAnInternalDependency()
	{
		using CancellationTokenSource cancellation = new();
		using FriendAsyncHookContainer.Root container = new();

		IFriendAsyncTerminal terminal = await container.ResolveAsync<IFriendAsyncTerminal>(cancellation.Token);
		IFriendAsyncPrinter printer = await container.ResolveAsync<IFriendAsyncPrinter>(cancellation.Token);

		await That(terminal.Token).IsEqualTo(cancellation.Token)
			.Because("the container awaits the module's internal Task hook directly and passes it the resolve-time token");
		await That(printer.Token).IsEqualTo(cancellation.Token)
			.Because("the container registers the scan match itself, awaits its internal ValueTask hook and passes it the token");
	}

	[Test]
	public async Task CallsTheHookOfAFactoryRegistrationWithAnInternalDependency()
	{
		using FriendFactoryHookContainer.Root container = new();

		await That(container.Resolve<IFriendFactoryLamp>().Lit).IsTrue()
			.Because("the container calls the module's internal hook directly instead of a wrapper the module could not generate");
	}

	[Test]
	public async Task ImportsAnInternalModuleWithInternalExposuresAndHookParameters()
	{
		using InternalPluginContainer.Root container = new();

		IFriendInternalPlugin plugin = await container.ResolveAsync<IFriendInternalPlugin>();

		await That(plugin.Wired).IsTrue()
			.Because("the internal module's factory exposes the match under its internal interface and its wrapper takes the internal log");
		await That(plugin.Started).IsTrue()
			.Because("the container runs the internal module's internal convention, whose parameter takes the internal log, on the match the factory builds");
	}

	[Container]
	[Import(typeof(SupportFriendModule))]
	public static partial class FriendContainer;

	[Container]
	[Import(typeof(SupportFriendFactoryHookModule))]
	public static partial class FriendFactoryHookContainer;

	[Container]
	[Import(typeof(SupportInternalPluginModule))]
	internal static partial class InternalPluginContainer;

	[Container]
	[Import(typeof(SupportFriendAsyncHookModule))]
	public static partial class FriendAsyncHookContainer;
	[Container]
	[Import(typeof(SupportFriendHookModule))]
	public static partial class FriendHookContainer;

	[Container]
	[Import(typeof(SupportFriendNotPartialModule))]
	public static partial class FriendShapeContainer;

	[Container]
	[Import(typeof(SupportFriendGenericModule<int>))]
	public static partial class FriendGenericContainer;

	[Container]
	[Import(typeof(SupportFriendScanModule))]
	public static partial class FriendScanContainer;
}
