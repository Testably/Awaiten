using System.Collections.Generic;
using System.Linq;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     A module scan match of an internal implementation and the handle another module of its assembly builds the
///     implementation through share one instance in a container of another assembly without <c>InternalsVisibleTo</c>.
/// </summary>
public partial class SharedScanHandleInstanceTests
{
	[Test]
	public async Task AScanMatchAndTheHandleOfAnInternalServiceShareOneInstance()
	{
		await using HandleScanContainer.Root container = new();
		await container.InitializeAsync();

		IHandleAppliance[] kettles = container.Resolve<IEnumerable<IHandleAppliance>>().Where(appliance => appliance is IHandleWarmable).ToArray();

		await That(kettles).HasCount(1)
			.Because("the module's registration and the other module's scan match of the kettle are one registration");
		await That(container.Resolve<IHandleBrewer>().Kettle).IsSameAs(kettles[0])
			.Because("the scan match serves the instance the kettle module's handle holds through its alias instead of building a second one");
	}

	[Test]
	public async Task AScanMatchAndTheSharedHandleOfAnImplementationShareOneInstance()
	{
		await using HandleScanContainer.Root container = new();
		await container.InitializeAsync();

		await That((object)container.Resolve<IHandleHeater>()).IsSameAs(container.Resolve<IEnumerable<IHandleAppliance>>().Single(appliance => appliance is IHandleHeater))
			.Because("the scan match serves the instance the handle shared by both registrations holds");
	}

	[Test]
	public async Task AConventionOfBothModules_RunsOnceOnTheSharedInstance()
	{
		await using HandleScanContainer.Root container = new();
		await container.InitializeAsync();

		await That(container.Resolve<IEnumerable<IHandleAppliance>>().OfType<IHandleWarmable>().Single().Warmings).IsEqualTo(1)
			.Because("the handle runs the convention once on the shared instance, and the scan match no longer initializes an instance of its own");
	}

	[Test]
	public async Task WithoutTheModulesThatBuildTheHandles_TheScanMatchesBuildTheirOwnInstances()
	{
		await using HandleScanOnlyContainer.Root container = new();
		await container.InitializeAsync();

		IHandleAppliance[] appliances = container.Resolve<IEnumerable<IHandleAppliance>>().ToArray();

		await That(appliances).HasCount(2)
			.Because("no handle is registered, so the scan's own factories build the kettle and the boiler");
		await That(appliances.OfType<IHandleWarmable>().Single().Warmings).IsEqualTo(1);
	}

	[Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
	[Import(typeof(HandleKettleModule))]
	[Import(typeof(HandleBoilerModule))]
	[Import(typeof(HandleApplianceScanModule))]
	public static partial class HandleScanContainer;

	[Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
	[Import(typeof(HandleApplianceScanModule))]
	public static partial class HandleScanOnlyContainer;
}
