using Awaiten.Tests.ContainerSupport;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     The <c>[Inject]</c> property shapes of a module's registrations, for a container in the module's assembly, one
///     in another assembly that builds the implementations through the module's generated factories
///     (<see cref="CrossAssemblyInjectedShapesModule" />) and one in an assembly the module grants
///     <c>InternalsVisibleTo</c> (<see cref="CrossAssemblyFriendInjectedShapesModule" />). All three fill them alike.
/// </summary>
public class ModuleInjectedPropertyShapeTests
{
	public class SameAssembly
	{
		[Test]
		public async Task ARegisteredClock_FillsEveryOptionalProperty()
		{
			using LocalShapesWithClockContainer.Root container = new();

			await ExpectClocks(container.Resolve<IInjectedShapes>());
		}

		[Test]
		public async Task AClockResolvingToNull_SetsEveryOptionalPropertyToNull()
		{
			using LocalShapesWithNullClockContainer.Root container = new();

			await ExpectNullClocks(container.Resolve<IInjectedShapes>());
		}

		[Test]
		public async Task AMissingClock_LeavesEveryOptionalPropertyAsItIs()
		{
			using LocalShapesContainer.Root container = new();

			await ExpectFallbacks(container.Resolve<IInjectedShapes>());
		}

		[Test]
		public async Task InaccessibleProperties_AreSet()
		{
			using LocalShapesContainer.Root container = new();

			await ExpectKettles(container.Resolve<IInjectedShapeUser>(), container.Resolve<IInjectedShapeKettle>());
		}
	}

	public class AnotherAssembly
	{
		[Test]
		public async Task ARegisteredClock_FillsEveryOptionalProperty()
		{
			using SupportShapesWithClockContainer.Root container = new();

			await ExpectClocks(container.Resolve<IInjectedShapes>());
		}

		[Test]
		public async Task AClockResolvingToNull_SetsEveryOptionalPropertyToNull()
		{
			using SupportShapesWithNullClockContainer.Root container = new();

			await ExpectNullClocks(container.Resolve<IInjectedShapes>());
		}

		[Test]
		public async Task AMissingClock_LeavesEveryOptionalPropertyAsItIs()
		{
			using SupportShapesContainer.Root container = new();

			await ExpectFallbacks(container.Resolve<IInjectedShapes>());
		}

		[Test]
		public async Task InaccessibleProperties_AreSet()
		{
			using SupportShapesContainer.Root container = new();

			await ExpectKettles(container.Resolve<IInjectedShapeUser>(), container.Resolve<IInjectedShapeKettle>());
		}
	}

	public class FriendAssembly
	{
		[Test]
		public async Task ARegisteredClock_FillsEveryOptionalProperty()
		{
			using FriendShapesWithClockContainer.Root container = new();

			await ExpectClocks(container.Resolve<IInjectedShapes>());
		}

		[Test]
		public async Task AClockResolvingToNull_SetsEveryOptionalPropertyToNull()
		{
			using FriendShapesWithNullClockContainer.Root container = new();

			await ExpectNullClocks(container.Resolve<IInjectedShapes>());
		}

		[Test]
		public async Task AMissingClock_LeavesEveryOptionalPropertyAsItIs()
		{
			using FriendShapesContainer.Root container = new();

			await ExpectFallbacks(container.Resolve<IInjectedShapes>());
		}

		[Test]
		public async Task InaccessibleProperties_AreSet()
		{
			using FriendShapesContainer.Root container = new();

			await ExpectKettles(container.Resolve<IInjectedShapeUser>(), container.Resolve<IInjectedShapeKettle>());
		}
	}

	private static async Task ExpectClocks(IInjectedShapes shapes)
	{
		await That(shapes.Clock).Is<InjectedShapeClock>();
		await That(shapes.InitClock).Is<InjectedShapeClock>();
		await That(shapes.OwnedClock).Is<InjectedShapeClock>();
		await That(shapes.DeferredClock).Is<InjectedShapeClock>();
	}

	private static async Task ExpectNullClocks(IInjectedShapes shapes)
	{
		await That(shapes.Clock).IsNull()
			.Because("a registered clock that resolves to null is assigned, not mistaken for a missing one");
		await That(shapes.InitClock).IsNull();
		await That(shapes.OwnedClock).IsNull();
		await That(shapes.DeferredClock).IsNull();
	}

	private static async Task ExpectFallbacks(IInjectedShapes shapes)
	{
		await That(shapes.Clock).IsSameAs(FallbackInjectedShapeClock.Instance)
			.Because("a missing optional service leaves the property at its initial value");
		await That(shapes.InitClock).IsSameAs(FallbackInjectedShapeClock.Instance);
		await That(shapes.OwnedClock).IsNull();
		await That(shapes.DeferredClock).IsSameAs(FallbackInjectedShapeClock.Instance);
	}

	private static async Task ExpectKettles(IInjectedShapeUser user, IInjectedShapeKettle kettle)
	{
		await That(user.InternalKettle).IsSameAs(kettle)
			.Because("an internal [Inject] property is set, never silently dropped");
		await That(user.SetterKettle).IsSameAs(kettle);
		await That(user.HasHiddenKettle).IsTrue();
	}
}

public sealed class InjectedShapeClock : IInjectedShapeClock;

internal sealed class LocalShapes : IInjectedShapes
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

internal sealed class LocalShapeKettle : IInjectedShapeKettle;

internal interface ILocalHiddenKettle;

internal sealed class LocalHiddenKettle : ILocalHiddenKettle;

public sealed class LocalShapeUser : IInjectedShapeUser
{
	[Inject]
	internal IInjectedShapeKettle InternalKettle { get; set; } = null!;

	[Inject]
	public IInjectedShapeKettle? SetterKettle { get; internal set; }

	[Inject]
	internal ILocalHiddenKettle Hidden { get; set; } = null!;

	IInjectedShapeKettle? IInjectedShapeUser.InternalKettle => InternalKettle;

	public bool HasHiddenKettle => Hidden is not null;
}

[Module]
[Transient<LocalShapes, IInjectedShapes>]
[Singleton<LocalShapeKettle, IInjectedShapeKettle>]
[Singleton<LocalHiddenKettle, ILocalHiddenKettle>]
[Transient<LocalShapeUser, IInjectedShapeUser>]
public static partial class LocalInjectedShapesModule;

[Container]
[Import(typeof(LocalInjectedShapesModule))]
public static partial class LocalShapesContainer;

[Container]
[Import(typeof(LocalInjectedShapesModule))]
[Transient<InjectedShapeClock, IInjectedShapeClock>]
public static partial class LocalShapesWithClockContainer;

[Container]
[Import(typeof(LocalInjectedShapesModule))]
[Transient<IInjectedShapeClock>(Factory = nameof(NoClock))]
public static partial class LocalShapesWithNullClockContainer
{
	private static IInjectedShapeClock NoClock() => null!;
}

[Container]
[Import(typeof(CrossAssemblyInjectedShapesModule))]
public static partial class SupportShapesContainer;

[Container]
[Import(typeof(CrossAssemblyInjectedShapesModule))]
[Transient<InjectedShapeClock, IInjectedShapeClock>]
public static partial class SupportShapesWithClockContainer;

[Container]
[Import(typeof(CrossAssemblyInjectedShapesModule))]
[Transient<IInjectedShapeClock>(Factory = nameof(NoClock))]
public static partial class SupportShapesWithNullClockContainer
{
	private static IInjectedShapeClock NoClock() => null!;
}

[Container]
[Import(typeof(CrossAssemblyFriendInjectedShapesModule))]
public static partial class FriendShapesContainer;

[Container]
[Import(typeof(CrossAssemblyFriendInjectedShapesModule))]
[Transient<InjectedShapeClock, IInjectedShapeClock>]
public static partial class FriendShapesWithClockContainer;

[Container]
[Import(typeof(CrossAssemblyFriendInjectedShapesModule))]
[Transient<IInjectedShapeClock>(Factory = nameof(NoClock))]
public static partial class FriendShapesWithNullClockContainer
{
	private static IInjectedShapeClock NoClock() => null!;
}
