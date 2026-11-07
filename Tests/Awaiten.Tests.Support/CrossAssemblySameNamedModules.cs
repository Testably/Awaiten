namespace Awaiten.Tests.SameNamed;

/// <summary>The service <see cref="SupportClockModule" /> registers this assembly's clock under.</summary>
public interface ISupportClock;

/// <summary>A clock whose full name Awaiten.Tests.ContainerSupport declares too, for a different type.</summary>
internal sealed class Clock : ISupportClock;

/// <summary>A <c>[Module]</c> that registers this assembly's clock.</summary>
[Module]
[Singleton<Clock, ISupportClock>]
public static partial class SupportClockModule;

/// <summary>A <c>[Module]</c> whose scan matches the clock <see cref="SupportClockModule" /> registers.</summary>
[Module]
[Scan<ISupportClock>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
public static partial class SupportClockScanModule;
