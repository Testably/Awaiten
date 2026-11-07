namespace Awaiten.Tests.ContainerSupport;

/// <summary>The marker <see cref="FriendSharedFactoryScanModule" /> scans for.</summary>
public interface IFriendSharedFactoryAppliance;

/// <summary>The service <see cref="FriendSharedFactoryScanModule" /> registers its kettle under.</summary>
public interface IFriendSharedFactoryKettle;

internal sealed class FriendSharedFactoryKettle : IFriendSharedFactoryAppliance, IFriendSharedFactoryKettle;

/// <summary>
///     A <c>[Module]</c> that registers an internal implementation through an internal factory and matches it with its
///     own scan. This assembly grants Awaiten.Tests <c>InternalsVisibleTo</c>, so a container there calls the factory.
/// </summary>
[Module]
[Scan<IFriendSharedFactoryAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
[Singleton<FriendSharedFactoryKettle, IFriendSharedFactoryKettle>(Factory = nameof(Create))]
public static partial class FriendSharedFactoryScanModule
{
	internal static FriendSharedFactoryKettle Create() => new();
}

/// <summary>The marker <see cref="FriendSharedScanModule" /> scans for.</summary>
public interface IFriendSharedAppliance;

/// <summary>The service <see cref="FriendSharedRegistrationModule" /> registers the kettle under.</summary>
public interface IFriendSharedKettle;

internal sealed class FriendSharedKettle : IFriendSharedAppliance, IFriendSharedKettle;

/// <summary>A <c>[Module]</c> that registers the internal kettle <see cref="FriendSharedScanModule" /> matches.</summary>
[Module]
[Singleton<FriendSharedKettle, IFriendSharedKettle>]
public static partial class FriendSharedRegistrationModule;

/// <summary>A <c>[Module]</c> whose scan matches the internal kettle another module or a container registers.</summary>
[Module]
[Scan<IFriendSharedAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
public static partial class FriendSharedScanModule;

/// <summary>The marker <see cref="FriendSharedHeaterScanModule" /> scans for.</summary>
public interface IFriendSharedHeater;

internal sealed class FriendSharedBoiler : IFriendSharedAppliance, IFriendSharedHeater;

/// <summary>A <c>[Module]</c> whose scan matches the internal boiler <see cref="FriendSharedScanModule" /> matches too.</summary>
[Module]
[Scan<IFriendSharedHeater>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
public static partial class FriendSharedHeaterScanModule;

/// <summary>A base container that registers the internal kettle and imports the module whose scan matches it.</summary>
[Container]
[Import(typeof(FriendSharedScanModule))]
[Singleton<FriendSharedKettle, IFriendSharedKettle>]
public static partial class FriendSharedScanBaseContainer;
