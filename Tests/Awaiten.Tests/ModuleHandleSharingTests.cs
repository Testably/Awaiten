using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Registrations of a container in another assembly that share the instance a module of Awaiten.Tests.Support builds
///     through a generated handle, as a container in the module's own assembly coalesces them into one instance.
/// </summary>
public class ModuleHandleSharingTests
{
	[Test]
	public async Task PublicImplementationTheContainerRegistersToo_IsOneInstance()
	{
		using PublicStoreContainer.Root container = new();

		await That(container.Resolve<PublicHandledStore>()).IsSameAs(container.Resolve<IHandledStoreUser>().Store)
			.Because("the container's registration of the public implementation serves the instance the module's handle holds");
	}

	[Test]
	public async Task PublicImplementationAModuleOfTheContainerRegistersToo_IsOneInstance()
	{
		using PublicStoreModuleContainer.Root container = new();

		await That(container.Resolve<PublicHandledStore>()).IsSameAs(container.Resolve<IHandledStoreUser>().Store)
			.Because("a module of this assembly that registers the public implementation shares the handle's instance too");
	}
}

[Container]
[Import(typeof(CrossAssemblyPublicStoreModule))]
[Singleton<PublicHandledStore>]
public static partial class PublicStoreContainer;

/// <summary>A <c>[Module]</c> of this assembly that registers the public store a module of another assembly builds through a handle.</summary>
[Module]
[Singleton<PublicHandledStore>]
public static class LocalPublicStoreModule;

[Container]
[Import(typeof(LocalPublicStoreModule))]
[Import(typeof(CrossAssemblyPublicStoreModule))]
public static partial class PublicStoreModuleContainer;
