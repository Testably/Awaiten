using System;
using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>What happened to an internal service its module produces through a private member.</summary>
public interface IPrivateMemberState
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
	IPrivateMemberState? Kettle { get; }
}

/// <summary>A barista that takes the internal services its module produces through private members.</summary>
public interface IPrivateMemberBarista
{
	/// <summary>The singleton kettle, from a private async factory with hooks.</summary>
	IPrivateMemberState Kettle { get; }

	/// <summary>The scoped cup, from a private factory that takes the kettle.</summary>
	IPrivateMemberState Cup { get; }

	/// <summary>A transient scale, from a private factory.</summary>
	IPrivateMemberState Scale { get; }

	/// <summary>A second transient scale.</summary>
	IPrivateMemberState OtherScale { get; }

	/// <summary>The shelf, a private instance.</summary>
	IPrivateMemberState Shelf { get; }
}

internal interface IPrivateMemberStartable
{
	Task StartAsync();
}

internal interface IPrivateMemberKettle : IPrivateMemberState, IPrivateMemberStartable;

internal interface IPrivateMemberCup : IPrivateMemberState;

internal interface IPrivateMemberScale : IPrivateMemberState;

internal interface IPrivateMemberShelf : IPrivateMemberState;

internal class PrivateMemberPart : IPrivateMemberCup, IPrivateMemberScale, IPrivateMemberShelf, IDisposable
{
	public bool Activated { get; set; }

	public bool Started { get; protected set; }

	public bool Released { get; set; }

	public bool Disposed { get; private set; }

	public IPrivateMemberState? Kettle { get; set; }

	public void Dispose() => Disposed = true;
}

internal sealed class PrivateMemberKettlePart : PrivateMemberPart, IPrivateMemberKettle
{
	public Task StartAsync()
	{
		Started = true;
		return Task.CompletedTask;
	}
}

internal sealed class PrivateMemberBarista(IPrivateMemberKettle kettle, IPrivateMemberCup cup, IPrivateMemberScale scale, IPrivateMemberScale otherScale, IPrivateMemberShelf shelf) : IPrivateMemberBarista
{
	public IPrivateMemberState Kettle => kettle;

	public IPrivateMemberState Cup => cup;

	public IPrivateMemberState Scale => scale;

	public IPrivateMemberState OtherScale => otherScale;

	public IPrivateMemberState Shelf => shelf;
}

/// <summary>
///     A <c>[Module]</c> that produces internal services through private <c>Factory</c> and <c>Instance</c> members, with
///     hooks, an async initializer convention and disposal. A container in another assembly takes them through their
///     handles.
/// </summary>
[Module]
[AsyncInitializer<IPrivateMemberStartable>(nameof(IPrivateMemberStartable.StartAsync))]
[Singleton<IPrivateMemberKettle>(Factory = nameof(MakeKettleAsync), OnActivated = nameof(Activate), OnRelease = nameof(Release))]
[Scoped<IPrivateMemberCup>(Factory = nameof(MakeCup))]
[Transient<IPrivateMemberScale>(Factory = nameof(MakeScale))]
[Singleton<IPrivateMemberShelf>(Instance = nameof(Shelf))]
[Scoped<PrivateMemberBarista, IPrivateMemberBarista>]
public static partial class CrossAssemblyPrivateMemberModule
{
	private static readonly IPrivateMemberShelf Shelf = new PrivateMemberPart();

	internal static void Activate(IPrivateMemberKettle kettle) => ((PrivateMemberPart)kettle).Activated = true;

	internal static void Release(IPrivateMemberKettle kettle) => ((PrivateMemberPart)kettle).Released = true;

	private static async Task<IPrivateMemberKettle> MakeKettleAsync()
	{
		await Task.Yield();
		return new PrivateMemberKettlePart();
	}

	private static IPrivateMemberCup MakeCup(IPrivateMemberKettle kettle) => new PrivateMemberPart { Kettle = kettle, };

	private static IPrivateMemberScale MakeScale() => new PrivateMemberPart();
}
