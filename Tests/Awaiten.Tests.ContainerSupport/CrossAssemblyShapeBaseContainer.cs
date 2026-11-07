namespace Awaiten.Tests.ContainerSupport;

/// <summary>A clock the base registers under a key and without one.</summary>
public interface IShapeBaseClock;

/// <summary>A service the base builds from a private implementation with a keyed parameter and an injected property.</summary>
public interface IShapeBaseReport
{
	/// <summary>The clock registered under the key "utc".</summary>
	IShapeBaseClock KeyedClock { get; }

	/// <summary>The unkeyed clock the container injected.</summary>
	IShapeBaseClock Clock { get; }
}

/// <summary>The clock registered under the key "utc".</summary>
public sealed class ShapeBaseUtcClock : IShapeBaseClock;

/// <summary>The unkeyed clock.</summary>
public sealed class ShapeBaseSystemClock : IShapeBaseClock;

/// <summary>A chime the base fronts with a private composite.</summary>
public interface IShapeBaseChime
{
	/// <summary>How many chimes ring.</summary>
	int Count { get; }
}

/// <summary>One chime.</summary>
public sealed class ShapeBaseHighChime : IShapeBaseChime
{
	/// <inheritdoc />
	public int Count => 1;
}

/// <summary>Another chime.</summary>
public sealed class ShapeBaseLowChime : IShapeBaseChime
{
	/// <inheritdoc />
	public int Count => 1;
}

/// <summary>A menu the base hands out from a private instance member.</summary>
public interface IShapeBaseMenu;

/// <summary>A kettle the base builds through a private factory and activates through a private hook.</summary>
public sealed class ShapeBaseKettle
{
	/// <summary>Set by the base's private activation hook.</summary>
	public bool Activated { get; set; }
}

/// <summary>
///     A production container in a referenced assembly whose private implementation takes a keyed parameter and an
///     injected property, which its generated factory mirrors for a test container in another assembly, and whose
///     private factory, instance member and hook it wraps for that container.
/// </summary>
[Container]
[Singleton<ShapeBaseUtcClock, IShapeBaseClock>(Key = "utc")]
[Singleton<ShapeBaseSystemClock, IShapeBaseClock>]
[Singleton<Report, IShapeBaseReport>]
[Singleton<IShapeBaseMenu>(Instance = nameof(TheMenu))]
[Transient<ShapeBaseKettle>(Factory = nameof(CreateKettle), OnActivated = nameof(Activate))]
[Decorate<AuditedReport, IShapeBaseReport>]
[Transient<ShapeBaseHighChime, IShapeBaseChime>]
[Transient<ShapeBaseLowChime, IShapeBaseChime>]
[Composite<AllChimes, IShapeBaseChime>]
public static partial class SupportShapeBaseContainer
{
	private sealed class AllChimes(System.Collections.Generic.IEnumerable<IShapeBaseChime> chimes) : IShapeBaseChime
	{
		public int Count => System.Linq.Enumerable.Sum(chimes, chime => chime.Count);
	}

	private sealed class AuditedReport(IShapeBaseReport inner) : IShapeBaseReport
	{
		public IShapeBaseClock KeyedClock => inner.KeyedClock;

		public IShapeBaseClock Clock => inner.Clock;
	}

	private static readonly IShapeBaseMenu TheMenu = new Menu();

	private static ShapeBaseKettle CreateKettle() => new();

	private static void Activate(ShapeBaseKettle kettle) => kettle.Activated = true;

	private sealed class Menu : IShapeBaseMenu;

	private sealed class Report([FromKey("utc")] IShapeBaseClock keyedClock) : IShapeBaseReport
	{
		public IShapeBaseClock KeyedClock => keyedClock;

		[Inject]
		public IShapeBaseClock Clock { get; set; } = null!;
	}
}
