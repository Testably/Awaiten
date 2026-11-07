namespace Awaiten.Tests.ContainerSupport;

/// <summary>A grinder whose implementation is private to its module and takes an internal store.</summary>
public interface IFriendPrivateGrinder
{
	/// <summary>The internal store the grinder was constructed with.</summary>
	object Store { get; }
}

internal interface IFriendPrivateStore;

internal sealed class FriendPrivateStore : IFriendPrivateStore;

/// <summary>
///     A <c>[Module]</c> whose private grinder takes an internal store the module registers. This assembly grants
///     Awaiten.Tests <c>InternalsVisibleTo</c>, so a container there builds the store itself and needs a factory of the
///     grinder that takes the store instead of its handle, as a container in this assembly does.
/// </summary>
[Module]
[Singleton<FriendPrivateStore, IFriendPrivateStore>]
[Singleton<SupportFriendPrivateModule.Grinder, IFriendPrivateGrinder>]
public static partial class SupportFriendPrivateModule
{
	private sealed class Grinder(IFriendPrivateStore store) : IFriendPrivateGrinder
	{
		public object Store => store;
	}
}

/// <summary>A container in the module's own assembly that imports <see cref="SupportFriendPrivateModule" />.</summary>
[Container]
[Import(typeof(SupportFriendPrivateModule))]
public static partial class FriendPrivateLocalContainer;
