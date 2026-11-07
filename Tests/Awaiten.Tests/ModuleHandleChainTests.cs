using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of internal services that pass through three modules of Awaiten.Tests.Support: the user takes
///     the kettle of a second module, which takes the bean of a third. Each module builds its service through the
///     handles of the others, so a container here that imports all three resolves the chain.
/// </summary>
public partial class ModuleHandleChainTests
{
	[Test]
	public async Task ServiceOfAThirdModule_ReachesTheUserThroughTheHandles()
	{
		using ChainContainer.Root container = new();

		await That(container.Resolve<IChainUser>().Bean.GetType().Name).IsEqualTo("ChainBean")
			.Because("the kettle module builds the kettle from the bean the third module shares through its handle");
	}

	[Container]
	[Import(typeof(CrossAssemblyChainKettleModule))]
	[Import(typeof(CrossAssemblyChainUserModule))]
	[Import(typeof(CrossAssemblyChainBeanModule))]
	public static partial class ChainContainer;
}
