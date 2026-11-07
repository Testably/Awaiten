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
}

[Container]
[Import(typeof(CrossAssemblySharedStorageModule))]
[Import(typeof(CrossAssemblySharedKitchenModule))]
[Import(typeof(CrossAssemblySharedBarModule))]
public static partial class SharedKitchenContainer;