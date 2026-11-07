namespace Awaiten.Tests.SameNamed;

/// <summary>The service <see cref="ContainerSupportClockModule" /> registers this assembly's clock under.</summary>
public interface IContainerSupportClock;

/// <summary>
///     A clock whose full name Awaiten.Tests.Support declares too, for a different type. This assembly grants
///     Awaiten.Tests <c>InternalsVisibleTo</c>, so a container there constructs it.
/// </summary>
internal sealed class Clock : IContainerSupportClock;

/// <summary>A <c>[Module]</c> that registers this assembly's clock.</summary>
[Module]
[Singleton<Clock, IContainerSupportClock>]
public static partial class ContainerSupportClockModule;
