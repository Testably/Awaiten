namespace Awaiten.Tests.Support;

/// <summary>A clock a consumer may register for the optional property of the internal timer.</summary>
public interface IInjectedClock;

/// <summary>An alarm that depends on the timer, which takes the alarm back through a deferred property.</summary>
public interface IInjectedAlarm
{
	/// <summary>The timer the alarm was constructed with.</summary>
	IInjectedTimer Timer { get; }
}

/// <summary>A singleton whose internal implementation has an optional and a deferred <c>[Inject]</c> property.</summary>
public interface IInjectedTimer
{
	/// <summary>The clock the container injected, or the fallback the property starts with.</summary>
	IInjectedClock Clock { get; }

	/// <summary>Whether the clock is still the fallback the property starts with.</summary>
	bool ClockIsFallback { get; }

	/// <summary>The alarm the container assigned after it built the timer.</summary>
	IInjectedAlarm Alarm { get; }
}

internal sealed class FallbackClock : IInjectedClock
{
	public static readonly FallbackClock Instance = new();
}

internal sealed class InjectedTimer : IInjectedTimer
{
	[Inject(Optional = true)]
	public IInjectedClock Clock { get; set; } = FallbackClock.Instance;

	public bool ClockIsFallback => ReferenceEquals(Clock, FallbackClock.Instance);

	[Inject(Deferred = true)]
	public IInjectedAlarm Alarm { get; set; } = null!;
}

internal sealed class InjectedAlarm(IInjectedTimer timer) : IInjectedAlarm
{
	public IInjectedTimer Timer => timer;
}

/// <summary>
///     A <c>[Module]</c> whose internal timer has an optional and a deferred <c>[Inject]</c> property, which its
///     generated factory mirrors: an optional parameter and a setter wrapper.
/// </summary>
[Module]
[Singleton<InjectedTimer, IInjectedTimer>]
[Transient<InjectedAlarm, IInjectedAlarm>]
public static partial class CrossAssemblyInjectionModule;