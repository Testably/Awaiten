using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Internal services that several modules of Awaiten.Tests.Support share through one generated handle: a module that
///     takes a service another module registers, and two modules that register the same internal implementation.
/// </summary>
public class ModuleSharedHiddenServiceTests
{
	[Test]
	public async Task InternalServiceOfAnotherModule_IsTheOneThatModuleRegisters()
	{
		using SharedKitchenContainer.Root container = new();

		await That(container.Resolve<ISharedChef>().Pantry.GetType().Name).IsEqualTo("SharedPantry")
			.Because("the kitchen module's factory takes the handle of the storage module, which the container registers");
	}

	[Test]
	public async Task OneInternalImplementationOfTwoModules_IsOneInstance()
	{
		ISharedBartender bartender;
		using (SharedKitchenContainer.Root container = new())
		{
			bartender = container.Resolve<ISharedBartender>();
			await That(container.Resolve<ISharedChef>().Pantry).IsSameAs(bartender.Pantry)
				.Because("both modules register the same internal singleton, which coalesces into one instance as in their own assembly");
		}

		await That(bartender.PantryDisposals).IsEqualTo(1)
			.Because("the one shared instance is disposed once");
	}

	[Test]
	public async Task OneImplementationOfTwoModulesUnderPublicServicesOnly_IsOneInstance()
	{
		using SharedBoardContainer.Root container = new();

		await That(container.Resolve<ISharedMenu>()).IsSameAs(container.Resolve<ISharedPrices>())
			.Because("both modules register the same internal implementation, which a container in their own assembly shares as one instance");
	}

	[Test]
	public async Task OneImplementationOfTwoModulesUnderDifferentInternalServices_IsOneInstance()
	{
		using SharedTillContainer.Root container = new();

		await That(container.Resolve<ISharedTill>()).IsSameAs(container.Resolve<ISharedCash>())
			.Because("both modules register the same internal implementation, each under an internal service of its own");
	}

	[Test]
	public async Task OneImplementationThatOnlyOneModuleBuildsThroughAHandle_IsOneInstance()
	{
		using SharedSafeContainer.Root container = new();

		await That(container.Resolve<ISharedSafe>()).IsSameAs(container.Resolve<ISharedVault>())
			.Because("the module that registers the implementation under a public service alone shares the instance of the handle the other module builds");
	}
}

[Container]
[Import(typeof(CrossAssemblySharedMenuModule))]
[Import(typeof(CrossAssemblySharedPriceModule))]
public static partial class SharedBoardContainer;

[Container]
[Import(typeof(CrossAssemblySharedTillModule))]
[Import(typeof(CrossAssemblySharedCashModule))]
public static partial class SharedTillContainer;

[Container]
[Import(typeof(CrossAssemblySharedSafeModule))]
[Import(typeof(CrossAssemblySharedVaultModule))]
public static partial class SharedSafeContainer;

[Container]
[Import(typeof(CrossAssemblySharedStorageModule))]
[Import(typeof(CrossAssemblySharedKitchenModule))]
[Import(typeof(CrossAssemblySharedBarModule))]
public static partial class SharedKitchenContainer;