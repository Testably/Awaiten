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

internal sealed class ProvidedSilo;

internal sealed class UnbuildableProvidedStore : IProvidedStore
{
	public UnbuildableProvidedStore(ProvidedSilo silo)
	{
	}
}

internal sealed class ProvidedStore : IProvidedStore;

internal sealed class ProvidedChef(IProvidedStore store) : IProvidedChef
{
	public object Store => store;
}

/// <summary>A <c>[Module]</c>, first by name, that registers the internal store with an implementation it cannot build.</summary>
[Module]
[Singleton<UnbuildableProvidedStore, IProvidedStore>]
public static partial class CrossAssemblyProvidedAModule;

/// <summary>A <c>[Module]</c> that registers the internal store with an implementation it can build.</summary>
[Module]
[Singleton<ProvidedStore, IProvidedStore>]
public static partial class CrossAssemblyProvidedBModule;

/// <summary>A <c>[Module]</c> whose chef takes the internal store the other modules register.</summary>
[Module]
[Singleton<ProvidedChef, IProvidedChef>]
public static partial class CrossAssemblyProvidedCModule;

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
