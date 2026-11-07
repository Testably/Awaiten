namespace Awaiten.Tests.Support;

/// <summary>A clock a consumer may register for the optional properties of <see cref="IInjectedShapes" />.</summary>
public interface IInjectedShapeClock;

/// <summary>The clock every optional property of <see cref="IInjectedShapes" /> starts with.</summary>
public sealed class FallbackInjectedShapeClock : IInjectedShapeClock
{
	/// <summary>The single fallback instance.</summary>
	public static readonly FallbackInjectedShapeClock Instance = new();
}

/// <summary>The optional <c>[Inject]</c> property shapes of one implementation, as the container left them.</summary>
public interface IInjectedShapes
{
	/// <summary>An optional property with a <c>set</c> accessor.</summary>
	IInjectedShapeClock? Clock { get; }

	/// <summary>An optional init-only property.</summary>
	IInjectedShapeClock? InitClock { get; }

	/// <summary>The value of an optional property of the value type <see cref="Owned{T}" />.</summary>
	IInjectedShapeClock? OwnedClock { get; }

	/// <summary>A property that is both optional and deferred.</summary>
	IInjectedShapeClock? DeferredClock { get; }
}

/// <summary>A kettle the module registers for the inaccessible <c>[Inject]</c> properties of <see cref="IInjectedShapeUser" />.</summary>
public interface IInjectedShapeKettle;

/// <summary>The <c>[Inject]</c> properties of a public implementation that another assembly cannot set.</summary>
public interface IInjectedShapeUser
{
	/// <summary>An internal property.</summary>
	IInjectedShapeKettle? InternalKettle { get; }

	/// <summary>A public property with an internal setter.</summary>
	IInjectedShapeKettle? SetterKettle { get; }

	/// <summary>Whether the internal property of an internal service type was set.</summary>
	bool HasHiddenKettle { get; }
}

internal sealed class SupportShapes : IInjectedShapes
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

internal sealed class SupportShapeKettle : IInjectedShapeKettle;

internal interface ISupportHiddenKettle;

internal sealed class SupportHiddenKettle : ISupportHiddenKettle;

/// <summary>A public implementation with a public constructor whose <c>[Inject]</c> properties another assembly cannot set.</summary>
public sealed class SupportShapeUser : IInjectedShapeUser
{
	[Inject]
	internal IInjectedShapeKettle InternalKettle { get; set; } = null!;

	/// <inheritdoc />
	[Inject]
	public IInjectedShapeKettle? SetterKettle { get; internal set; }

	[Inject]
	internal ISupportHiddenKettle Hidden { get; set; } = null!;

	IInjectedShapeKettle? IInjectedShapeUser.InternalKettle => InternalKettle;

	/// <inheritdoc />
	public bool HasHiddenKettle => Hidden is not null;
}

/// <summary>
///     A <c>[Module]</c> whose registrations carry the <c>[Inject]</c> property shapes a generated factory fills for a
///     container in another assembly without <c>InternalsVisibleTo</c>.
/// </summary>
[Module]
[Transient<SupportShapes, IInjectedShapes>]
[Singleton<SupportShapeKettle, IInjectedShapeKettle>]
[Singleton<SupportHiddenKettle, ISupportHiddenKettle>]
[Transient<SupportShapeUser, IInjectedShapeUser>]
public static partial class CrossAssemblyInjectedShapesModule;
