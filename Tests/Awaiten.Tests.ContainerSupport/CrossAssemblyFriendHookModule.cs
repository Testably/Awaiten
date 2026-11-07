namespace Awaiten.Tests.ContainerSupport;

/// <summary>A service whose activation hook takes an internal dependency.</summary>
public interface IFriendActivated
{
	/// <summary>Whether the activation hook ran.</summary>
	bool Activated { get; }
}

/// <summary>The marker of a scanned plugin whose activation hook takes an internal dependency.</summary>
public interface IFriendHookedPlugin
{
	/// <summary>Whether the activation hook ran.</summary>
	bool Activated { get; }
}

internal sealed class FriendHookLog;

internal sealed class FriendActivated : IFriendActivated
{
	public bool Activated { get; set; }
}

internal sealed class FriendHookedPlugin : IFriendHookedPlugin
{
	public bool Activated { get; set; }
}

/// <summary>
///     A <c>[Module]</c> whose hooks take an internal dependency, which a generated wrapper cannot mirror. This assembly
///     grants Awaiten.Tests <c>InternalsVisibleTo</c>, so the module builds without those wrappers and a container there
///     calls the hooks itself.
/// </summary>
[Module]
[Singleton<FriendHookLog>]
[Singleton<FriendActivated, IFriendActivated>(OnActivated = nameof(Activate))]
[Scan<IFriendHookedPlugin>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton, OnActivated = nameof(ActivatePlugin))]
public static partial class SupportFriendHookModule
{
	internal static void Activate(FriendActivated activated, FriendHookLog log) => activated.Activated = log is not null;

	internal static void ActivatePlugin(FriendHookedPlugin plugin, FriendHookLog log) => plugin.Activated = log is not null;
}
