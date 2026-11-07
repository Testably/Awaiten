namespace Awaiten.Tests.ContainerSupport;

/// <summary>A grinder that a decorator private to its module wraps.</summary>
public interface IFriendDecoratedGrinder
{
	/// <summary>The links of the decorator chain, outermost first.</summary>
	string Chain { get; }

	/// <summary>The internal store the outermost link was constructed with.</summary>
	object Store { get; }
}

internal interface IFriendDecoratedStore
{
	string Chain { get; }
}

internal sealed class FriendDecoratedStore : IFriendDecoratedStore
{
	public string Chain => "store";
}

internal sealed class FriendDecoratedGrinder(IFriendDecoratedStore store) : IFriendDecoratedGrinder
{
	public string Chain => "grinder";

	public object Store => store;
}

/// <summary>
///     A <c>[Module]</c> with decorators private to it that take an internal store: one decorates the store, the other
///     decorates a public grinder and takes the store too. This assembly grants Awaiten.Tests <c>InternalsVisibleTo</c>,
///     so a container there builds the store itself and needs factories of the decorators that take the store instead of
///     its handle, as a container in this assembly does.
/// </summary>
[Module]
[Singleton<FriendDecoratedStore, IFriendDecoratedStore>]
[Singleton<FriendDecoratedGrinder, IFriendDecoratedGrinder>]
[Decorate<SupportFriendPrivateDecoratorModule.LoggingStore, IFriendDecoratedStore>]
[Decorate<SupportFriendPrivateDecoratorModule.LoggingGrinder, IFriendDecoratedGrinder>]
public static partial class SupportFriendPrivateDecoratorModule
{
	private sealed class LoggingStore(IFriendDecoratedStore inner) : IFriendDecoratedStore
	{
		public string Chain => "logging " + inner.Chain;
	}

	private sealed class LoggingGrinder(IFriendDecoratedGrinder inner, IFriendDecoratedStore store) : IFriendDecoratedGrinder
	{
		public string Chain => "logging " + inner.Chain;

		public object Store => store;
	}
}

/// <summary>A container in the module's own assembly that imports <see cref="SupportFriendPrivateDecoratorModule" />.</summary>
[Container]
[Import(typeof(SupportFriendPrivateDecoratorModule))]
public static partial class FriendPrivateDecoratorLocalContainer;
