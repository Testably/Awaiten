using System.Collections.Generic;
using System.Linq;
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

	[Container]
	[Import(typeof(SupportFriendModule))]
	public static partial class FriendContainer;

	[Container]
	[Import(typeof(SupportFriendScanModule))]
	public static partial class FriendScanContainer;
}
