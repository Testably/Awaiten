namespace Awaiten.Tests.ContainerSupport;

/// <summary>The marker of the appliances <see cref="SupportFriendRegisteredScanModule" /> scans for.</summary>
public interface IFriendAppliance;

/// <summary>The service <see cref="SupportFriendRegisteredScanModule" /> registers the scanned kettle under.</summary>
public interface IFriendKettle;

internal sealed class FriendKettle : IFriendAppliance, IFriendKettle;

/// <summary>
///     A <c>[Module]</c> that both registers an internal implementation and matches it with its own scan. This assembly
///     grants Awaiten.Tests <c>InternalsVisibleTo</c>, so the module generates neither factory and a container there
///     constructs the implementation itself.
/// </summary>
[Module]
[Scan<IFriendAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
[Singleton<FriendKettle, IFriendKettle>]
public static partial class SupportFriendRegisteredScanModule;
