using System.Collections.Generic;
using System.Linq;
using Awaiten.Tests.ContainerSupport;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     A module scan match and another registration or scan match of the same implementation share one instance, in a
///     container of the module's own assembly and in one of another assembly, with or without
///     <c>InternalsVisibleTo</c>.
/// </summary>
public partial class SharedScanInstanceTests
{
	[Test]
	public async Task InTheModulesAssembly_AFactoryRegistrationAndTheModulesScanMatchShareOneInstance()
	{
		using LocalFactoryContainer.Root container = new();

		await That((object)container.Resolve<ILocalFactoryKettle>()).IsSameAs(container.Resolve<IEnumerable<ILocalFactoryAppliance>>().Single())
			.Because("the scan match coalesces with the registration on the implementation, which the factory builds");
	}

	[Test]
	public async Task InTheModulesAssembly_AnotherModulesRegistrationAndTheScanMatchShareOneInstance()
	{
		using LocalTwoModulesContainer.Root container = new();

		await That((object)container.Resolve<ILocalKettle>()).IsSameAs(container.Resolve<IEnumerable<ILocalAppliance>>().OfType<LocalKettle>().Single());
	}

	[Test]
	public async Task InTheModulesAssembly_TheContainersRegistrationAndTheScanMatchShareOneInstance()
	{
		using LocalContainerRegistrationContainer.Root container = new();

		await That((object)container.Resolve<ILocalKettle>()).IsSameAs(container.Resolve<IEnumerable<ILocalAppliance>>().OfType<LocalKettle>().Single());
	}

	[Test]
	public async Task InTheModulesAssembly_TheScanMatchesOfTwoModulesShareOneInstance()
	{
		using LocalTwoScansContainer.Root container = new();

		await That((object)container.Resolve<IEnumerable<ILocalHeater>>().Single()).IsSameAs(container.Resolve<IEnumerable<ILocalAppliance>>().OfType<LocalBoiler>().Single());
	}

	[Test]
	public async Task WithoutInternalsVisibleTo_AFactoryRegistrationAndTheModulesScanMatchShareOneInstance()
	{
		using SupportFactoryContainer.Root container = new();

		await That((object)container.Resolve<ISharedFactoryKettle>()).IsSameAs(container.Resolve<IEnumerable<ISharedFactoryAppliance>>().Single())
			.Because("the scan match names its implementation, so the container shares the factory's instance instead of calling the scan's factory");
	}

	[Test]
	public async Task WithoutInternalsVisibleTo_AnotherModulesRegistrationAndTheScanMatchShareOneInstance()
	{
		using SupportTwoModulesContainer.Root container = new();

		await That((object)container.Resolve<ISharedKettle>()).IsSameAs(container.Resolve<IEnumerable<ISharedAppliance>>().OfType<SharedKettle>().Single());
	}

	[Test]
	public async Task WithoutInternalsVisibleTo_TheContainersRegistrationAndTheScanMatchShareOneInstance()
	{
		using SupportContainerRegistrationContainer.Root container = new();

		await That((object)container.Resolve<ISharedKettle>()).IsSameAs(container.Resolve<IEnumerable<ISharedAppliance>>().OfType<SharedKettle>().Single());
	}

	[Test]
	public async Task WithoutInternalsVisibleTo_TheScanMatchesOfTwoModulesShareOneInstance()
	{
		using SupportTwoScansContainer.Root container = new();

		await That((object)container.Resolve<IEnumerable<ISharedHeater>>().Single()).IsSameAs(container.Resolve<IEnumerable<ISharedAppliance>>().OfType<SharedBoiler>().Single())
			.Because("neither factory's return type converts to the other's service, so the container constructs the public implementation once for both");
	}

	[Test]
	public async Task WithInternalsVisibleTo_AFactoryRegistrationAndTheModulesScanMatchShareOneInstance()
	{
		using FriendFactoryContainer.Root container = new();

		await That((object)container.Resolve<IFriendSharedFactoryKettle>()).IsSameAs(container.Resolve<IEnumerable<IFriendSharedFactoryAppliance>>().Single());
	}

	[Test]
	public async Task WithInternalsVisibleTo_AnotherModulesRegistrationAndTheScanMatchShareOneInstance()
	{
		using FriendTwoModulesContainer.Root container = new();

		await That((object)container.Resolve<IFriendSharedKettle>()).IsSameAs(container.Resolve<IEnumerable<IFriendSharedAppliance>>().OfType<IFriendSharedKettle>().Single());
	}

	[Test]
	public async Task WithInternalsVisibleTo_TheContainersRegistrationAndTheScanMatchShareOneInstance()
	{
		using FriendContainerRegistrationContainer.Root container = new();

		await That((object)container.Resolve<IFriendSharedKettle>()).IsSameAs(container.Resolve<IEnumerable<IFriendSharedAppliance>>().OfType<IFriendSharedKettle>().Single());
	}

	[Test]
	public async Task WithInternalsVisibleTo_TheScanMatchesOfTwoModulesShareOneInstance()
	{
		using FriendTwoScansContainer.Root container = new();

		await That((object)container.Resolve<IEnumerable<IFriendSharedHeater>>().Single()).IsSameAs(container.Resolve<IEnumerable<IFriendSharedAppliance>>().OfType<IFriendSharedHeater>().Single())
			.Because("the container constructs the internal implementation once for both scan matches");
	}

	[Test]
	public async Task WithInternalsVisibleTo_AnInheritedRegistrationAndAnInheritedScanMatchShareOneInstance()
	{
		using FriendExtendingContainer.Root container = new();

		await That((object)container.Resolve<IFriendSharedKettle>()).IsSameAs(container.Resolve<IEnumerable<IFriendSharedAppliance>>().OfType<IFriendSharedKettle>().Single())
			.Because("the base's scan match coalesces with its registration in the extending container, as in the base itself");
	}

	[Container]
	[Import(typeof(LocalFactoryScanModule))]
	public static partial class LocalFactoryContainer;

	[Container]
	[Import(typeof(LocalRegistrationModule))]
	[Import(typeof(LocalScanModule))]
	public static partial class LocalTwoModulesContainer;

	[Container]
	[Import(typeof(LocalScanModule))]
	[Singleton<LocalKettle, ILocalKettle>]
	public static partial class LocalContainerRegistrationContainer;

	[Container]
	[Import(typeof(LocalScanModule))]
	[Import(typeof(LocalHeaterScanModule))]
	public static partial class LocalTwoScansContainer;

	[Container]
	[Import(typeof(SharedFactoryScanModule))]
	public static partial class SupportFactoryContainer;

	[Container]
	[Import(typeof(SharedRegistrationModule))]
	[Import(typeof(SharedScanModule))]
	public static partial class SupportTwoModulesContainer;

	[Container]
	[Import(typeof(SharedScanModule))]
	[Singleton<SharedKettle, ISharedKettle>]
	public static partial class SupportContainerRegistrationContainer;

	[Container]
	[Import(typeof(SharedScanModule))]
	[Import(typeof(SharedHeaterScanModule))]
	public static partial class SupportTwoScansContainer;

	[Container]
	[Import(typeof(FriendSharedFactoryScanModule))]
	public static partial class FriendFactoryContainer;

	[Container]
	[Import(typeof(FriendSharedRegistrationModule))]
	[Import(typeof(FriendSharedScanModule))]
	public static partial class FriendTwoModulesContainer;

	[Container]
	[Import(typeof(FriendSharedScanModule))]
	[Singleton<FriendSharedKettle, IFriendSharedKettle>]
	public static partial class FriendContainerRegistrationContainer;

	[Container]
	[Import(typeof(FriendSharedScanModule))]
	[Import(typeof(FriendSharedHeaterScanModule))]
	public static partial class FriendTwoScansContainer;

	[Container(Extends = typeof(FriendSharedScanBaseContainer))]
	public static partial class FriendExtendingContainer;
}

/// <summary>The marker <see cref="LocalFactoryScanModule" /> scans for.</summary>
public interface ILocalFactoryAppliance;

/// <summary>The service <see cref="LocalFactoryScanModule" /> registers its kettle under.</summary>
public interface ILocalFactoryKettle;

internal sealed class LocalFactoryKettle : ILocalFactoryAppliance, ILocalFactoryKettle;

[Module]
[Scan<ILocalFactoryAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
[Singleton<LocalFactoryKettle, ILocalFactoryKettle>(Factory = nameof(Create))]
internal static partial class LocalFactoryScanModule
{
	internal static LocalFactoryKettle Create() => new();
}

/// <summary>The marker <see cref="LocalScanModule" /> scans for.</summary>
public interface ILocalAppliance;

/// <summary>The service <see cref="LocalRegistrationModule" /> registers the kettle under.</summary>
public interface ILocalKettle;

internal sealed class LocalKettle : ILocalAppliance, ILocalKettle;

[Module]
[Singleton<LocalKettle, ILocalKettle>]
internal static partial class LocalRegistrationModule;

[Module]
[Scan<ILocalAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
internal static partial class LocalScanModule;

/// <summary>The marker <see cref="LocalHeaterScanModule" /> scans for.</summary>
public interface ILocalHeater;

internal sealed class LocalBoiler : ILocalAppliance, ILocalHeater;

[Module]
[Scan<ILocalHeater>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
internal static partial class LocalHeaterScanModule;
