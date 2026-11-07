using System.Collections.Generic;
using System.Linq;
using Awaiten.Tests.SameNamed;

namespace Awaiten.Tests;

/// <summary>
///     Two assemblies that declare an implementation of the same full name declare two different types, so a
///     container that imports a registration of each keeps one instance of each.
/// </summary>
public partial class SameNamedImplementationTests
{
	[Test]
	public async Task RegistrationsOfSameNamedTypesFromTwoAssemblies_StayTwoInstances()
	{
		using TwoClocksContainer.Root container = new();

		await That((object)container.Resolve<ISupportClock>()).IsNotSameAs(container.Resolve<IContainerSupportClock>())
			.Because("the clocks are different types that only share their full name");
	}

	[Test]
	public async Task AScanMatchOfOneOfTheSameNamedTypes_SharesTheInstanceOfItsOwnType()
	{
		using TwoClocksContainer.Root container = new();

		await That((object)container.Resolve<IEnumerable<ISupportClock>>().Single()).IsSameAs(container.Resolve<ISupportClock>())
			.Because("the scan match names the clock of its own assembly, which the registration of that assembly already builds");
		await That((object)container.Resolve<IEnumerable<ISupportClock>>().Single()).IsNotSameAs(container.Resolve<IContainerSupportClock>());
	}

	[Container]
	[Import(typeof(SupportClockModule))]
	[Import(typeof(SupportClockScanModule))]
	[Import(typeof(ContainerSupportClockModule))]
	public static partial class TwoClocksContainer;
}
