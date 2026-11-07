using Awaiten.Tests.Support;

namespace Awaiten.Tests.ContainerSupport;

internal sealed class FriendShapes : IInjectedShapes
{
	[Inject(Optional = true)]
	public IInjectedShapeClock? Clock { get; set; } = FallbackInjectedShapeClock.Instance;

	[Inject(Optional = true)]
	public IInjectedShapeClock? InitClock { get; init; } = FallbackInjectedShapeClock.Instance;

	[Inject(Optional = true)]
	public Owned<IInjectedShapeClock> OwnedValue { get; set; }

	public IInjectedShapeClock? OwnedClock => OwnedValue.Value;

	[Inject(Optional = true, Deferred = true)]
	public IInjectedShapeClock? DeferredClock { get; set; } = FallbackInjectedShapeClock.Instance;
}

internal sealed class FriendShapeKettle : IInjectedShapeKettle;

internal interface IFriendHiddenKettle;

internal sealed class FriendHiddenKettle : IFriendHiddenKettle;

/// <summary>A public implementation with a public constructor whose <c>[Inject]</c> properties only a friend assembly can set.</summary>
public sealed class FriendShapeUser : IInjectedShapeUser
{
	[Inject]
	internal IInjectedShapeKettle InternalKettle { get; set; } = null!;

	/// <inheritdoc />
	[Inject]
	public IInjectedShapeKettle? SetterKettle { get; internal set; }

	[Inject]
	internal IFriendHiddenKettle Hidden { get; set; } = null!;

	IInjectedShapeKettle? IInjectedShapeUser.InternalKettle => InternalKettle;

	/// <inheritdoc />
	public bool HasHiddenKettle => Hidden is not null;
}

/// <summary>
///     A <c>[Module]</c> whose registrations carry the <c>[Inject]</c> property shapes of
///     <see cref="CrossAssemblyInjectedShapesModule" />, in an assembly that grants Awaiten.Tests
///     <c>InternalsVisibleTo</c>, so a container there sets the properties itself.
/// </summary>
[Module]
[Transient<FriendShapes, IInjectedShapes>]
[Singleton<FriendShapeKettle, IInjectedShapeKettle>]
[Singleton<FriendHiddenKettle, IFriendHiddenKettle>]
[Transient<FriendShapeUser, IInjectedShapeUser>]
public static partial class CrossAssemblyFriendInjectedShapesModule;
