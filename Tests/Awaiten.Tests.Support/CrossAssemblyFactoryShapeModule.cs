namespace Awaiten.Tests.Support;

/// <summary>A clock an importing container registers for the internal implementations of <see cref="FactoryShapeModule" />.</summary>
public interface IShapeClock;

/// <summary>A greeter registered under itself and under <see cref="IShapeFormalGreeter" />.</summary>
public interface IShapeGreeter;

/// <summary>The second service of the greeter, which covers the first.</summary>
public interface IShapeFormalGreeter : IShapeGreeter;

/// <summary>A report whose internal implementation takes a keyed clock.</summary>
public interface IShapeReport
{
	/// <summary>The clock registered under the key "utc".</summary>
	IShapeClock Clock { get; }
}

/// <summary>A label whose internal implementation takes its text at resolve time.</summary>
public interface IShapeLabel
{
	/// <summary>The text passed to the resolution.</summary>
	string Text { get; }
}

/// <summary>A notifier whose internal implementation has an injected property.</summary>
public interface IShapeNotifier
{
	/// <summary>The clock the container injected.</summary>
	IShapeClock Clock { get; }
}

/// <summary>A menu the module hands out from an internal instance member.</summary>
public interface IShapeMenu;

/// <summary>A kettle the module builds through an internal factory member.</summary>
public interface IShapeKettle
{
	/// <summary>The clock the factory received.</summary>
	IShapeClock Clock { get; }
}

internal sealed class ShapeMenu : IShapeMenu;

internal sealed class ShapeKettle(IShapeClock clock) : IShapeKettle
{
	public IShapeClock Clock => clock;
}

internal sealed class ShapeGreeter : IShapeFormalGreeter;

internal sealed class ShapeReport([FromKey("utc")] IShapeClock clock) : IShapeReport
{
	public IShapeClock Clock => clock;
}

internal sealed class ShapeLabel([Arg] string text) : IShapeLabel
{
	public string Text => text;
}

internal sealed class ShapeNotifier : IShapeNotifier
{
	[Inject]
	public IShapeClock Clock { get; set; } = null!;
}

/// <summary>
///     A <c>[Module]</c> whose internal implementations its generated factories build for a container in another
///     assembly without <c>InternalsVisibleTo</c>: one shared under two services, a keyed constructor parameter, a
///     runtime argument, an injected property, and internal factory and instance members.
/// </summary>
[Module]
[Singleton<ShapeGreeter, IShapeGreeter>]
[Singleton<ShapeGreeter, IShapeFormalGreeter>]
[Singleton<ShapeReport, IShapeReport>]
[Transient<ShapeLabel, IShapeLabel>]
[Singleton<ShapeNotifier, IShapeNotifier>]
[Singleton<IShapeMenu>(Instance = nameof(Menu))]
[Transient<IShapeKettle>(Factory = nameof(CreateKettle))]
public static partial class FactoryShapeModule
{
	internal static readonly IShapeMenu Menu = new ShapeMenu();

	internal static IShapeKettle CreateKettle(IShapeClock clock) => new ShapeKettle(clock);
}
