namespace Awaiten.Tests.ContainerSupport;

/// <summary>A lamp the module builds through a public factory and lights in a hook that takes an internal dependency.</summary>
public interface IFriendFactoryLamp
{
	/// <summary>Whether the activation hook ran.</summary>
	bool Lit { get; }
}

internal sealed class FriendFactoryLog;

internal sealed class FriendFactoryLamp : IFriendFactoryLamp
{
	public bool Lit { get; set; }
}

/// <summary>
///     A <c>[Module]</c> whose <c>Factory</c> registration names a hook that takes an internal dependency, which a
///     generated wrapper cannot mirror. This assembly grants Awaiten.Tests <c>InternalsVisibleTo</c>, so the module
///     builds without that wrapper and a container there calls the hook itself.
/// </summary>
[Module]
[Singleton<FriendFactoryLog>]
[Singleton<IFriendFactoryLamp>(Factory = nameof(CreateLamp), OnActivated = nameof(Light))]
public static partial class SupportFriendFactoryHookModule
{
	/// <summary>Creates the lamp.</summary>
	public static IFriendFactoryLamp CreateLamp() => new FriendFactoryLamp();

	internal static void Light(IFriendFactoryLamp lamp, FriendFactoryLog log) => ((FriendFactoryLamp)lamp).Lit = log is not null;
}

internal interface IFriendInternalPlugin
{
	bool Wired { get; }
}

internal interface IFriendInternalLog;

internal sealed class FriendInternalLog : IFriendInternalLog;

internal sealed class FriendInternalPlugin : IFriendInternalPlugin
{
	public bool Wired { get; set; }
}

/// <summary>
///     An <c>internal</c> <c>[Module]</c> whose scan exposes its match under an internal interface and whose hook takes
///     an internal dependency. Only this assembly and Awaiten.Tests, which it grants <c>InternalsVisibleTo</c>, can
///     import it, so its factory and hook wrapper may name internal types.
/// </summary>
[Module]
[Singleton<FriendInternalLog, IFriendInternalLog>]
[Scan<IFriendInternalPlugin>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton, OnActivated = nameof(Wire))]
internal static partial class SupportInternalPluginModule
{
	internal static void Wire(FriendInternalPlugin plugin, IFriendInternalLog log) => plugin.Wired = log is not null;
}
