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

/// <summary>
///     A production container in a referenced assembly whose private implementation takes a keyed parameter and an
///     injected property, which its generated factory mirrors for a test container in another assembly.
/// </summary>
[Container]
[Singleton<ShapeBaseUtcClock, IShapeBaseClock>(Key = "utc")]
[Singleton<ShapeBaseSystemClock, IShapeBaseClock>]
[Singleton<Report, IShapeBaseReport>]
public static partial class SupportShapeBaseContainer
{
	private sealed class Report([FromKey("utc")] IShapeBaseClock keyedClock) : IShapeBaseReport
	{
		public IShapeBaseClock KeyedClock => keyedClock;

		[Inject]
		public IShapeBaseClock Clock { get; set; } = null!;
	}
}
