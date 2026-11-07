namespace Awaiten.Tests.ContainerSupport;

/// <summary>The marker of the plugins <see cref="SupportFriendScanModule" /> scans for.</summary>
public interface IFriendPlugin;

/// <summary>The matching interface of a scanned plugin that takes an internal dependency.</summary>
public interface IFriendRoaster
{
	/// <summary>The internal dependency the roaster was constructed with.</summary>
	object Secret { get; }
}

/// <summary>The matching interface of a scanned plugin that both scans expose under different services.</summary>
public interface IFriendGrinder;

internal sealed class FriendSecret;

internal sealed class FriendRoaster(FriendSecret secret) : IFriendPlugin, IFriendRoaster
{
	public object Secret => secret;
}

internal sealed class FriendGrinder : IFriendPlugin, IFriendGrinder;

/// <summary>
///     A <c>[Module]</c> whose scan matches types its generated factories cannot support. This assembly grants
///     Awaiten.Tests <c>InternalsVisibleTo</c>, so the module builds without factories for them and a container there
///     registers the matches itself.
/// </summary>
[Module]
[Scan<IFriendPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton)]
[Scan<IFriendPlugin>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
[Singleton<FriendSecret>]
public static partial class SupportFriendScanModule;
