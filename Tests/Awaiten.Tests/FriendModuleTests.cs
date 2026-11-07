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

	[Container]
	[Import(typeof(SupportFriendModule))]
	public static partial class FriendContainer;
}
