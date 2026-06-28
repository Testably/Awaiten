namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of <see cref="Owned{T}" />: resolving a service as an owned handle (directly, or
///     through a <c>Func&lt;Owned&lt;T&gt;&gt;</c> / <c>Func&lt;TArg…, Owned&lt;T&gt;&gt;</c> factory) builds
///     it in a dedicated throwaway scope and transfers disposal to the caller. Disposing the handle releases
///     only what was built for that one resolution, while shared singletons live on the container. The
///     containers and services are nested types, so the enclosing class is <c>partial</c>.
/// </summary>
public partial class OwnedTests
{
	[Fact]
	public async Task FuncOwned_BuildsAFreshDisposablePerCall_DisposedWithTheHandleNotTheContainer()
	{
		using OwnedContainer.Root container = new();

		Workshop workshop = container.Resolve<Workshop>();

		Widget first;
		using (Owned<Widget> owned = workshop.Rent())
		{
			first = owned.Value;
			await That(first.Disposed).IsFalse()
				.Because("the handle is still alive");
		}

		await That(first.Disposed).IsTrue()
			.Because("disposing the Owned handle disposes the throwaway scope that built the widget");

		Widget second = workshop.Rent().Value;
		await That(second).IsNotSameAs(first)
			.Because("each call to the factory builds a fresh instance");
	}

	[Fact]
	public async Task Owned_IsNotTrackedByTheContainerRoot()
	{
		Widget leaked;
		using (OwnedContainer.Root container = new())
		{
			Workshop workshop = container.Resolve<Workshop>();

			// Build through the singleton-held factory but deliberately never dispose the handle.
			leaked = workshop.Rent().Value;
		}

		await That(leaked.Disposed).IsFalse()
			.Because("an Owned widget the caller never disposed is not tracked by the container root, so disposing the container does not dispose it - the handle, not the root, owns it");
	}

	[Fact]
	public async Task OwnedSingleton_SharesTheRootInstanceAndDoesNotDisposeIt()
	{
		using OwnedContainer.Root container = new();

		Engine direct = container.Resolve<Engine>();
		Owned<Engine> owned = container.Resolve<Owned<Engine>>();

		await That(owned.Value).IsSameAs(direct)
			.Because("a singleton resolved as Owned still comes from the shared root");

		owned.Dispose();

		await That(direct.Disposed).IsFalse()
			.Because("disposing an Owned handle over a singleton does not dispose the shared singleton");
	}

	[Fact]
	public async Task FuncOwnedWithRuntimeArgument_FlowsTheArgumentAndIsDisposedWithTheHandle()
	{
		using OwnedContainer.Root container = new();

		Func<string, Owned<Label>> labels = container.Resolve<Func<string, Owned<Label>>>();

		Label made;
		using (Owned<Label> owned = labels("urgent"))
		{
			made = owned.Value;
			await That(made.Text).IsEqualTo("urgent")
				.Because("the runtime argument flows through the Func<…, Owned<T>> into the [Arg] parameter");
			await That(made.Disposed).IsFalse();
		}

		await That(made.Disposed).IsTrue()
			.Because("a disposable parameterized service built through Func<…, Owned<T>> is released with its handle");
	}

	[Fact]
	public async Task BareOwned_InjectedDirectly_IsDisposedWhenTheOwnerDisposesIt()
	{
		using OwnedContainer.Root container = new();

		Holder holder = container.Resolve<Holder>();
		Widget held = holder.Owned.Value;

		await That(held.Disposed).IsFalse();

		holder.Owned.Dispose();

		await That(held.Disposed).IsTrue()
			.Because("a bare Owned<T> dependency transfers disposal to whoever holds the handle");
	}

	[Fact]
	public async Task FuncOwned_OverANonDisposableThatBuildsADisposable_DrainsTheTransitiveDisposableWithTheHandle()
	{
		using OwnedContainer.Root container = new();

		Func<Owned<Gizmo>> gizmos = container.Resolve<Func<Owned<Gizmo>>>();

		Bolt bolt;
		using (Owned<Gizmo> owned = gizmos())
		{
			bolt = owned.Value.Bolt;
			await That(bolt.Disposed).IsFalse()
				.Because("the handle is still alive");
		}

		await That(bolt.Disposed).IsTrue()
			.Because("Gizmo is not disposable, but its transient Bolt is built into the throwaway scope and released when the handle is disposed - the transitive disposable does not accumulate on the root");
	}

	public sealed class Engine : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public sealed class Bolt : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	// Non-disposable, but each construction pulls a fresh disposable Bolt - the transitive-accumulation case.
	public sealed class Gizmo
	{
		public Gizmo(Bolt bolt) => Bolt = bolt;

		public Bolt Bolt { get; }
	}

	public sealed class Widget : IDisposable
	{
		public Widget(Engine engine) => Engine = engine;

		public Engine Engine { get; }

		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public sealed class Label : IDisposable
	{
		public Label([Arg] string text) => Text = text;

		public string Text { get; }

		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	// A singleton holding a Func<Owned<Widget>>: the leak-free way for a long-lived service to build and
	// release short-lived disposables on demand.
	public sealed class Workshop
	{
		private readonly Func<Owned<Widget>> _widgets;

		public Workshop(Func<Owned<Widget>> widgets) => _widgets = widgets;

		public Owned<Widget> Rent() => _widgets();
	}

	// A direct Owned<Widget> dependency: the holder owns the handle and decides when to release it.
	public sealed class Holder
	{
		public Holder(Owned<Widget> owned) => Owned = owned;

		public Owned<Widget> Owned { get; }
	}

	[Container]
	[Singleton<Engine>]
	[Transient<Widget>]
	[Transient<Label>]
	[Singleton<Workshop>]
	[Transient<Holder>]
	[Transient<Bolt>]
	[Transient<Gizmo>]
	public static partial class OwnedContainer;
}
