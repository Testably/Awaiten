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

	[Test]
	public async Task TransientDisposableThroughAnAlias_IsWithheldOnTheRoot()
	{
		using AliasedDisposableContainer.Root container = new();

		await That((object)container is IAwaitenResolver<IAliasedCup>).IsFalse()
			.Because("the root withholds a transient disposable under strict lifetime safety, also when an alias serves it from a handle");
		await That(() => container.Resolve<IAliasedCup>()).Throws<InvalidOperationException>()
			.WithMessage("*withheld*Owned<*").AsWildcard();

		using AliasedDisposableContainer.Scope scope = container.CreateScope();
		await That(scope.Resolve<IAliasedCup>()).IsNotNull();
	}

	[Test]
	public async Task InternalServiceOnlyALaterModuleCanBuild_IsProvidedByThatModule()
	{
		using ProvidedStoreContainer.Root container = new();

		await That(container.Resolve<IProvidedChef>().Store.GetType().Name).IsEqualTo("ProvidedStore")
			.Because("the module first by name declares the handle, but the module that can build the store serves it");
	}

	[Test]
	public async Task DecoratorOfAnInternalServiceAnotherModuleRegisters_DecoratesIt()
	{
		IDecoratedTillUser user;
		using (DecoratedTillContainer.Root container = new())
		{
			user = container.Resolve<IDecoratedTillUser>();
			await That(user.Till.GetType().Name).IsEqualTo("LoggingTill")
				.Because("the decorating module builds its decorator into the handle the registering module declares");
		}

		await That(user.DecoratorDisposed).IsTrue()
			.Because("the container disposes the decorator link through the handle variant of its disposal");
	}
}

[Container]
[Import(typeof(CrossAssemblyTillModule))]
[Import(typeof(CrossAssemblyTillLoggingModule))]
public static partial class DecoratedTillContainer;

[Container]
[Import(typeof(CrossAssemblyProvidedBModule))]
[Import(typeof(CrossAssemblyProvidedCModule))]
public static partial class ProvidedStoreContainer;

[Container]
[Import(typeof(CrossAssemblyAliasedDisposableModule))]
public static partial class AliasedDisposableContainer;

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
