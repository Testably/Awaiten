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

	public sealed class Widget : IDisposable
	{
		public void Dispose()
		{
		}
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
}
