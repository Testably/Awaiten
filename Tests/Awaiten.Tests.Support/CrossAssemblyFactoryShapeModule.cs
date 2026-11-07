using System.Collections.Generic;
using System.Linq;

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

/// <summary>A bell whose internal implementation the module decorates with an internal decorator.</summary>
public interface IShapeBell
{
	/// <summary>The bell the decorator wraps, or <see langword="null" /> for the undecorated bell.</summary>
	IShapeBell? Inner { get; }
}

internal sealed class ShapeBell : IShapeBell
{
	public IShapeBell? Inner => null;
}

internal sealed class LoudShapeBell(IShapeBell inner, IShapeClock clock) : IShapeBell
{
	public IShapeBell? Inner => inner;

	public IShapeClock Clock => clock;
}

/// <summary>A chime whose internal implementations an internal composite fronts.</summary>
public interface IShapeChime
{
	/// <summary>How many chimes ring.</summary>
	int Count { get; }
}

internal sealed class ShapeHighChime : IShapeChime
{
	public int Count => 1;
}

internal sealed class ShapeLowChime : IShapeChime
{
	public int Count => 1;
}

internal sealed class AllShapeChimes(IEnumerable<IShapeChime> chimes) : IShapeChime
{
	public int Count => chimes.Sum(chime => chime.Count);
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
///     runtime argument, an injected property, internal factory and instance members, an internal decorator and an
///     internal composite.
/// </summary>
[Module]
[Singleton<ShapeGreeter, IShapeGreeter>]
[Singleton<ShapeGreeter, IShapeFormalGreeter>]
[Singleton<ShapeReport, IShapeReport>]
[Transient<ShapeLabel, IShapeLabel>]
[Singleton<ShapeNotifier, IShapeNotifier>]
[Singleton<IShapeMenu>(Instance = nameof(Menu))]
[Transient<IShapeKettle>(Factory = nameof(CreateKettle))]
[Singleton<ShapeBell, IShapeBell>]
[Decorate<LoudShapeBell, IShapeBell>]
[Transient<ShapeHighChime, IShapeChime>]
[Transient<ShapeLowChime, IShapeChime>]
[Composite<AllShapeChimes, IShapeChime>]
public static partial class FactoryShapeModule
{
	internal static readonly IShapeMenu Menu = new ShapeMenu();

	internal static IShapeKettle CreateKettle(IShapeClock clock) => new ShapeKettle(clock);
}
