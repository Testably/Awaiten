namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of <see cref="LifetimeSafety" />. Under the default <see cref="LifetimeSafety.Strict" />
///     a disposable transient is withheld from by-type resolution (it throws guidance pointing at
///     <see cref="Owned{T}" />), while <see cref="Owned{T}" /> and <c>Func&lt;Owned&lt;T&gt;&gt;</c> stay
///     resolvable; under <see cref="LifetimeSafety.Loose" /> it resolves like any other service. The
///     containers and services are nested types, so the enclosing class is <c>partial</c>.
/// </summary>
public partial class StrictModeTests
{
	[Fact]
	public async Task Strict_ResolvingADisposableTransientByType_ThrowsGuidance()
	{
		using StrictContainer.Root container = new();

		await That(() => container.Resolve<Widget>()).Throws<InvalidOperationException>()
			.Because("a disposable transient is withheld from by-type resolution under strict lifetime safety");
	}

	[Fact]
	public async Task Strict_TryResolvingAWithheldDisposableTransient_ReturnsFalseRatherThanThrowing()
	{
		using StrictContainer.Root container = new();

		bool resolved = container.TryResolve<Widget>(out Widget? widget);

		await That(resolved).IsFalse()
			.Because("TryResolve is a non-throwing probe; a withheld disposable transient is simply not resolvable by type, so it reports false rather than throwing the by-type guidance");
		await That(widget).IsNull();
	}

	[Fact]
	public async Task Strict_TryResolvingTheWithheldPlainFunc_ReturnsFalseRatherThanThrowing()
	{
		using StrictContainer.Root container = new();

		bool resolved = container.TryResolve<Func<Widget>>(out Func<Widget>? factory);

		await That(resolved).IsFalse()
			.Because("the plain Func<Widget> is withheld under strict safety; TryResolve reports that as false, while Resolve throws the guidance");
		await That(factory is null).IsTrue();
	}

	[Fact]
	public async Task Strict_OwnedAndFuncOwned_RemainResolvable()
	{
		using StrictContainer.Root container = new();

		using Owned<Widget> owned = container.Resolve<Owned<Widget>>();
		await That(owned.Value).IsNotNull()
			.Because("Owned<T> is the sanctioned way to reach a withheld service");

		Func<Owned<Widget>> factory = container.Resolve<Func<Owned<Widget>>>();
		using Owned<Widget> built = factory();
		await That(built.Value).IsNotNull()
			.Because("Func<Owned<T>> stays resolvable under strict lifetime safety");
		await That(built.Value).IsNotSameAs(owned.Value)
			.Because("each owned handle builds a fresh instance");
	}

	[Fact]
	public async Task Strict_ResolvingAWithheldDisposableTransientFromAScope_Works()
	{
		using StrictContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();

		Widget widget = scope.Resolve<Widget>();

		await That(widget).IsNotNull()
			.Because("strict withholds the disposable transient on the container root, but a child scope can still resolve it by type - its lifetime is bounded by the scope, so there is no root accumulation");
	}

	[Fact]
	public async Task Strict_ADisposableTransientResolvedFromAScope_IsDisposedWithTheScope()
	{
		using StrictContainer.Root container = new();

		Widget widget;
		using (IAwaitenScope scope = container.CreateScope())
		{
			widget = scope.Resolve<Widget>();
			await That(widget.Disposed).IsFalse()
				.Because("the scope is still alive");
		}

		await That(widget.Disposed).IsTrue()
			.Because("the scope tracks the transient it built and disposes it when the scope is disposed - the leak the root would suffer is bounded here");
	}

	[Fact]
	public async Task Strict_ResolvingTheWithheldPlainFuncFromAScope_BuildsFreshInstancesBoundToTheScope()
	{
		using StrictContainer.Root container = new();

		Widget first, second;
		using (IAwaitenScope scope = container.CreateScope())
		{
			Func<Widget> factory = scope.Resolve<Func<Widget>>();
			first = factory();
			second = factory();

			await That(second).IsNotSameAs(first)
				.Because("the plain Func is resolvable from a scope under strict safety and builds a fresh instance per call");
			await That(first.Disposed).IsFalse();
		}

		await That(first.Disposed).IsTrue()
			.Because("every widget the scope-bound Func built is tracked on that scope and disposed with it");
		await That(second.Disposed).IsTrue();
	}

	[Fact]
	public async Task Strict_TheScopeBoundFunc_StillThrowsWhenResolvedFromTheRoot()
	{
		using StrictContainer.Root container = new();

		await That(() => container.Resolve<Func<Widget>>()).Throws<InvalidOperationException>()
			.Because("the plain Func remains withheld on the root - only the scope path is opened up, so the leak stays impossible");
	}

	[Fact]
	public async Task Strict_InjectingADisposableTransientDirectly_IsAllowed()
	{
		using StrictContainer.Root container = new();

		// Consumer takes a bare Widget in its constructor - bounded to one instance per Consumer, so it is not
		// withheld even under strict safety.
		Consumer consumer = container.Resolve<Consumer>();

		await That(consumer.Widget).IsNotNull()
			.Because("constructor injection of a disposable transient is bounded and stays allowed");
	}

	[Fact]
	public async Task Loose_ResolvingADisposableTransientByType_Works()
	{
		using LooseContainer.Root container = new();

		Widget widget = container.Resolve<Widget>();

		await That(widget).IsNotNull()
			.Because("Loose lifetime safety keeps a disposable transient resolvable by type, like MS.DI");
	}

	[Fact]
	public async Task Strict_SingletonHoldingLazyOfADisposableTransient_ForcingBuildsIt()
	{
		using LazyContainer.Root container = new();

		Vault vault = container.Resolve<Vault>();

		// Lazy<T> is memoized (bounded to one), so it is not withheld even under strict safety; forcing it must
		// build the widget rather than throw the by-type withholding guidance.
		Widget widget = vault.Widget.Value;

		await That(widget).IsNotNull()
			.Because("a Lazy<DisposableTransient> held by a singleton stays usable - it builds at most one instance");
	}

	// A singleton holding a Lazy over a disposable transient: bounded to a single memoized instance, disposed
	// with the container, so it is allowed under strict safety (unlike a re-invokable Func, which is AWT118).
	public sealed class Vault
	{
		public Vault(Lazy<Widget> widget) => Widget = widget;

		public Lazy<Widget> Widget { get; }
	}

	public sealed class Widget : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public sealed class Consumer
	{
		public Consumer(Widget widget) => Widget = widget;

		public Widget Widget { get; }
	}

	[Container]
	[Transient<Widget>]
	[Transient<Consumer>]
	public static partial class StrictContainer;

	[Container(LifetimeSafety = LifetimeSafety.Loose)]
	[Transient<Widget>]
	public static partial class LooseContainer;

	[Container]
	[Transient<Widget>]
	[Singleton<Vault>]
	public static partial class LazyContainer;
}
