using System;
using System.Threading.Tasks;

namespace Awaiten.Tests.ContainerSupport;

/// <summary>What happened to an internal service its module produces through a private member.</summary>
public interface IFriendPrivateMemberState
{
	/// <summary>Whether the module's activation hook ran on the service.</summary>
	bool Activated { get; }

	/// <summary>Whether the module's async initializer convention ran on the service.</summary>
	bool Started { get; }

	/// <summary>Whether the module's release hook ran on the service.</summary>
	bool Released { get; }

	/// <summary>Whether the container disposed the service.</summary>
	bool Disposed { get; }

	/// <summary>The kettle a cup was produced with, or <see langword="null" />.</summary>
	IFriendPrivateMemberState? Kettle { get; }
}

/// <summary>A barista that takes the internal services its module produces through private members.</summary>
public interface IFriendPrivateMemberBarista
{
	/// <summary>The singleton kettle, from a private async factory with hooks.</summary>
	IFriendPrivateMemberState Kettle { get; }

	/// <summary>The scoped cup, from a private factory that takes the kettle.</summary>
	IFriendPrivateMemberState Cup { get; }

	/// <summary>A transient scale, from a private factory.</summary>
	IFriendPrivateMemberState Scale { get; }

	/// <summary>A second transient scale.</summary>
	IFriendPrivateMemberState OtherScale { get; }

	/// <summary>The shelf, a private instance.</summary>
	IFriendPrivateMemberState Shelf { get; }
}

internal interface IFriendPrivateMemberStartable
{
	Task StartAsync();
}

internal interface IFriendPrivateMemberKettle : IFriendPrivateMemberState, IFriendPrivateMemberStartable;

internal interface IFriendPrivateMemberCup : IFriendPrivateMemberState;

internal interface IFriendPrivateMemberScale : IFriendPrivateMemberState;

internal interface IFriendPrivateMemberShelf : IFriendPrivateMemberState;

internal class FriendPrivateMemberPart : IFriendPrivateMemberCup, IFriendPrivateMemberScale, IFriendPrivateMemberShelf, IDisposable
{
	public bool Activated { get; set; }

	public bool Started { get; protected set; }

	public bool Released { get; set; }

	public bool Disposed { get; private set; }

	public IFriendPrivateMemberState? Kettle { get; set; }

	public void Dispose() => Disposed = true;
}

internal sealed class FriendPrivateMemberKettlePart : FriendPrivateMemberPart, IFriendPrivateMemberKettle
{
	public Task StartAsync()
	{
		Started = true;
		return Task.CompletedTask;
	}
}

internal sealed class FriendPrivateMemberBarista(IFriendPrivateMemberKettle kettle, IFriendPrivateMemberCup cup, IFriendPrivateMemberScale scale, IFriendPrivateMemberScale otherScale, IFriendPrivateMemberShelf shelf) : IFriendPrivateMemberBarista
{
	public IFriendPrivateMemberState Kettle => kettle;

	public IFriendPrivateMemberState Cup => cup;

	public IFriendPrivateMemberState Scale => scale;

	public IFriendPrivateMemberState OtherScale => otherScale;

	public IFriendPrivateMemberState Shelf => shelf;
}

/// <summary>
///     A <c>[Module]</c> that produces internal services through private <c>Factory</c> and <c>Instance</c> members, with
///     hooks, an async initializer convention and disposal. This assembly grants Awaiten.Tests <c>InternalsVisibleTo</c>, so a container
///     there, like one in this assembly, builds them itself through internal wrappers of the private members.
/// </summary>
[Module]
[AsyncInitializer<IFriendPrivateMemberStartable>(nameof(IFriendPrivateMemberStartable.StartAsync))]
[Singleton<IFriendPrivateMemberKettle>(Factory = nameof(MakeKettleAsync), OnActivated = nameof(Activate), OnRelease = nameof(Release))]
[Scoped<IFriendPrivateMemberCup>(Factory = nameof(MakeCup))]
[Transient<IFriendPrivateMemberScale>(Factory = nameof(MakeScale))]
[Singleton<IFriendPrivateMemberShelf>(Instance = nameof(Shelf))]
[Scoped<FriendPrivateMemberBarista, IFriendPrivateMemberBarista>]
public static partial class SupportFriendPrivateMemberModule
{
	private static readonly IFriendPrivateMemberShelf Shelf = new FriendPrivateMemberPart();

	internal static void Activate(IFriendPrivateMemberKettle kettle) => ((FriendPrivateMemberPart)kettle).Activated = true;

	internal static void Release(IFriendPrivateMemberKettle kettle) => ((FriendPrivateMemberPart)kettle).Released = true;

	private static async Task<IFriendPrivateMemberKettle> MakeKettleAsync()
	{
		await Task.Yield();
		return new FriendPrivateMemberKettlePart();
	}

	private static IFriendPrivateMemberCup MakeCup(IFriendPrivateMemberKettle kettle) => new FriendPrivateMemberPart { Kettle = kettle, };

	private static IFriendPrivateMemberScale MakeScale() => new FriendPrivateMemberPart();
}

/// <summary>A container in the module's own assembly that imports <see cref="SupportFriendPrivateMemberModule" />.</summary>
[Container]
[Import(typeof(SupportFriendPrivateMemberModule))]
public static partial class FriendPrivateMemberLocalContainer;
