namespace Awaiten.Tests.Support;

/// <summary>A public store that <see cref="CrossAssemblyPublicStoreModule" /> registers under an internal service only.</summary>
public sealed class PublicHandledStore : IHandledStoreAccess;

/// <summary>A service whose internal implementation takes the public store through its internal service.</summary>
public interface IHandledStoreUser
{
	/// <summary>The store the user received.</summary>
	object Store { get; }
}

internal interface IHandledStoreAccess;

internal sealed class HandledStoreUser(IHandledStoreAccess store) : IHandledStoreUser
{
	public object Store => store;
}

/// <summary>A <c>[Module]</c> that registers a public implementation under an internal service, so it builds it through a handle.</summary>
[Module]
[Singleton<PublicHandledStore, IHandledStoreAccess>]
[Singleton<HandledStoreUser, IHandledStoreUser>]
public static partial class CrossAssemblyPublicStoreModule;

/// <summary>A public service of a transient disposable that <see cref="CrossAssemblyAliasedDisposableModule" /> registers under an internal service too.</summary>
public interface IAliasedCup;

internal interface IAliasedCupSlot;

internal sealed class AliasedCup : IAliasedCup, IAliasedCupSlot, System.IDisposable
{
	public void Dispose()
	{
	}
}

/// <summary>A <c>[Module]</c> that registers a transient disposable under a public and an internal service, so a consumer reaches it through an alias.</summary>
[Module]
[Transient<AliasedCup, IAliasedCup>]
[Transient<AliasedCup, IAliasedCupSlot>]
public static partial class CrossAssemblyAliasedDisposableModule;

/// <summary>A chef whose internal implementation takes an internal store that only other modules register.</summary>
public interface IProvidedChef
{
	/// <summary>The internal store the chef received.</summary>
	object Store { get; }
}

internal interface IProvidedStore;

internal sealed class UnimportedProvidedStore : IProvidedStore;

internal sealed class ProvidedStore : IProvidedStore;

internal sealed class ProvidedChef(IProvidedStore store) : IProvidedChef
{
	public object Store => store;
}

/// <summary>A <c>[Module]</c>, first by name, that registers the internal store and declares its handle, which the container does not import.</summary>
[Module]
[Singleton<UnimportedProvidedStore, IProvidedStore>]
public static partial class CrossAssemblyProvidedAModule;

/// <summary>A <c>[Module]</c> that registers the internal store with an implementation of its own.</summary>
[Module]
[Singleton<ProvidedStore, IProvidedStore>]
public static partial class CrossAssemblyProvidedBModule;

/// <summary>A <c>[Module]</c> whose chef takes the internal store the other modules register.</summary>
[Module]
[Singleton<ProvidedChef, IProvidedChef>]
public static partial class CrossAssemblyProvidedCModule;

/// <summary>A clock that <see cref="CrossAssemblyOverriddenEagerModule" /> registers as an eager overridable default.</summary>
public interface IOverriddenClock;

/// <summary>Counts the constructions of the clock behind <see cref="IOverriddenClock" />, so a test sees whether it is built eagerly.</summary>
public static class OverriddenClockCounter
{
	private static int _constructed;

	/// <summary>How often the clock was constructed.</summary>
	public static int Constructed => System.Threading.Volatile.Read(ref _constructed);

	internal static void Increment() => System.Threading.Interlocked.Increment(ref _constructed);
}

internal interface IOverriddenTick;

internal sealed class OverriddenClock : IOverriddenClock, IOverriddenTick
{
	public OverriddenClock() => OverriddenClockCounter.Increment();
}

/// <summary>A <c>[Module]</c> whose eager registration is an overridable default beside a lazy one under an internal service.</summary>
[Module]
[Singleton<OverriddenClock, IOverriddenClock>(Eager = true, Fallback = Fallback.Silent)]
[Singleton<OverriddenClock, IOverriddenTick>]
public static partial class CrossAssemblyOverriddenEagerModule;

/// <summary>A front service of the internal grill that <see cref="CrossAssemblyGrillFrontModule" /> registers.</summary>
public interface IGrillFront;

/// <summary>A back service of the internal grill that <see cref="CrossAssemblyGrillBackModule" /> registers.</summary>
public interface IGrillBack;

internal sealed class Grill : IGrillFront, IGrillBack;

/// <summary>A <c>[Module]</c> that registers the internal grill under one public service.</summary>
[Module]
[Singleton<Grill, IGrillFront>]
public static partial class CrossAssemblyGrillFrontModule;

/// <summary>A <c>[Module]</c> that registers the same internal grill under another public service.</summary>
[Module]
[Singleton<Grill, IGrillBack>]
public static partial class CrossAssemblyGrillBackModule;

/// <summary>A clock <see cref="CrossAssemblyContextualEagerModule" /> registers as an eager overridable default for <see cref="ContextualWatch" />.</summary>
public interface IContextualClock;

/// <summary>Counts the constructions of the internal clock of <see cref="CrossAssemblyContextualEagerModule" />.</summary>
public static class ContextualClockCounter
{
	private static int _constructed;

	/// <summary>How often the clock was constructed.</summary>
	public static int Constructed => System.Threading.Volatile.Read(ref _constructed);

	internal static void Increment() => System.Threading.Interlocked.Increment(ref _constructed);
}

/// <summary>The consumer the eager clock of <see cref="CrossAssemblyContextualEagerModule" /> is injected into.</summary>
public sealed class ContextualWatch(IContextualClock clock)
{
	/// <summary>The clock the watch received.</summary>
	public IContextualClock Clock => clock;
}

internal interface IContextualTick;

internal sealed class ContextualClock : IContextualClock, IContextualTick
{
	public ContextualClock() => ContextualClockCounter.Increment();
}

/// <summary>A <c>[Module]</c> whose eager registration is an overridable default only for the watch, beside a lazy one under an internal service.</summary>
[Module]
[Singleton<ContextualClock, IContextualClock>(Eager = true, Fallback = Fallback.Silent, WhenInjectedInto = typeof(ContextualWatch))]
[Singleton<ContextualClock, IContextualTick>(Fallback = Fallback.Silent)]
[Singleton<ContextualWatch>]
public static partial class CrossAssemblyContextualEagerModule;

/// <summary>A clock <see cref="CrossAssemblyDroppedEagerModule" /> registers as an eager overridable default.</summary>
public interface IDroppedClock;

/// <summary>A timer <see cref="CrossAssemblyDroppedEagerModule" /> registers the same implementation under as an overridable default.</summary>
public interface IDroppedTimer;

/// <summary>Counts the constructions of the implementation behind <see cref="IDroppedClock" />.</summary>
public static class DroppedClockCounter
{
	private static int _constructed;

	/// <summary>How often the implementation was constructed.</summary>
	public static int Constructed => System.Threading.Volatile.Read(ref _constructed);

	internal static void Increment() => System.Threading.Interlocked.Increment(ref _constructed);
}

internal sealed class DroppedClock : IDroppedClock, IDroppedTimer
{
	public DroppedClock() => DroppedClockCounter.Increment();
}

/// <summary>A <c>[Module]</c> whose registrations of one internal implementation are all overridable defaults, one of them eager.</summary>
[Module]
[Singleton<DroppedClock, IDroppedClock>(Eager = true, Fallback = Fallback.Silent)]
[Singleton<DroppedClock, IDroppedTimer>(Fallback = Fallback.Silent)]
public static partial class CrossAssemblyDroppedEagerModule;

/// <summary>A user of an internal till that one module registers and another module decorates.</summary>
public interface IDecoratedTillUser
{
	/// <summary>The internal till the user received.</summary>
	object Till { get; }

	/// <summary>Whether the decorator around the till was disposed.</summary>
	bool DecoratorDisposed { get; }
}

internal interface IDecoratedTill;

internal sealed class DecoratedTill : IDecoratedTill;

internal sealed class LoggingTill(IDecoratedTill inner) : IDecoratedTill, System.IDisposable
{
	public IDecoratedTill Inner => inner;

	public bool Disposed { get; private set; }

	public void Dispose() => Disposed = true;
}

internal sealed class DecoratedTillUser(IDecoratedTill till) : IDecoratedTillUser
{
	public object Till => till;

	public bool DecoratorDisposed => till is LoggingTill { Disposed: true, };
}

/// <summary>A <c>[Module]</c> that registers the internal till and its user.</summary>
[Module]
[Singleton<DecoratedTill, IDecoratedTill>]
[Singleton<DecoratedTillUser, IDecoratedTillUser>]
public static partial class CrossAssemblyTillModule;

/// <summary>A <c>[Module]</c> that decorates the internal till the other module registers.</summary>
[Module]
[Decorate<LoggingTill, IDecoratedTill>]
public static partial class CrossAssemblyTillLoggingModule;
