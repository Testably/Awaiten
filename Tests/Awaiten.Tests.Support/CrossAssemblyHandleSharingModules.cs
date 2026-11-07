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
