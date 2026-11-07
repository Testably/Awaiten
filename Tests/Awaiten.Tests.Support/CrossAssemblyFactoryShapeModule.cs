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
///     runtime argument and an injected property.
/// </summary>
[Module]
[Singleton<ShapeGreeter, IShapeGreeter>]
[Singleton<ShapeGreeter, IShapeFormalGreeter>]
[Singleton<ShapeReport, IShapeReport>]
[Transient<ShapeLabel, IShapeLabel>]
[Singleton<ShapeNotifier, IShapeNotifier>]
public static partial class FactoryShapeModule;
