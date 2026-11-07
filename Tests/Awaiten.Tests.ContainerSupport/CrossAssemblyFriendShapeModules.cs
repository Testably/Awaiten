namespace Awaiten.Tests.ContainerSupport;

/// <summary>A service registered by modules that cannot carry generated factories.</summary>
public interface IFriendWelcome;

internal sealed class FriendWelcome : IFriendWelcome;

/// <summary>
///     A <c>[Module]</c> that is not partial, so it cannot carry generated factories. This assembly grants Awaiten.Tests
///     <c>InternalsVisibleTo</c>, so the module builds and a container there constructs the implementation itself.
/// </summary>
[Module]
[Singleton<FriendWelcome, IFriendWelcome>]
public static class SupportFriendNotPartialModule;

/// <summary>
///     A generic <c>[Module]</c>, which cannot carry generated factories. This assembly grants Awaiten.Tests
///     <c>InternalsVisibleTo</c>, so the module builds and a container there constructs the implementation itself.
/// </summary>
/// <typeparam name="T">Unused; only makes the module generic.</typeparam>
[Module]
[Singleton<FriendWelcome, IFriendWelcome>]
public static partial class SupportFriendGenericModule<T>;
