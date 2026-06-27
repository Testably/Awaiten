namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of the relationship types: <see cref="Func{T}" /> resolves fresh on each call
///     (respecting the target's lifetime), and <see cref="Lazy{T}" /> memoizes per owner. The
///     containers and services are nested types, so the enclosing class is <c>partial</c>.
/// </summary>
public partial class RelationshipTests
{
	[Fact]
	public async Task Func_OfTransient_ResolvesAFreshInstanceEachCall()
	{
		using RelationshipContainer container = new();

		Consumer consumer = container.Resolve<Consumer>();

		Widget first = consumer.MakeWidget();
		Widget second = consumer.MakeWidget();
		await That(ReferenceEquals(first, second)).IsFalse();
	}

	[Fact]
	public async Task Func_OfSingleton_ResolvesTheSameInstanceEachCall()
	{
		using RelationshipContainer container = new();

		Func<Engine> factory = container.Resolve<Func<Engine>>();

		await That(factory()).IsSameAs(factory());
		await That(factory()).IsSameAs(container.Resolve<Engine>());
	}

	[Fact]
	public async Task Lazy_MemoizesItsValue()
	{
		using RelationshipContainer container = new();

		Lazy<Widget> lazy = container.Resolve<Lazy<Widget>>();

		await That(lazy.Value).IsSameAs(lazy.Value);
	}

	[Fact]
	public async Task Func_BoundToAScope_ResolvesScopedInstancesFromThatScope()
	{
		using RelationshipContainer container = new();
		using IAwaitenScope scope = container.CreateScope();

		ScopedConsumer consumer = scope.Resolve<ScopedConsumer>();

		// The injected Func is bound to the scope, so it returns the scope's single scoped instance.
		await That(consumer.GetSession()).IsSameAs(scope.Resolve<Session>());
	}

	public sealed class Widget;

	public sealed class Engine;

	public sealed class Session;

	public sealed class Consumer
	{
		private readonly Func<Widget> _widgets;

		public Consumer(Func<Widget> widgets) => _widgets = widgets;

		public Widget MakeWidget() => _widgets();
	}

	public sealed class ScopedConsumer
	{
		private readonly Func<Session> _sessions;

		public ScopedConsumer(Func<Session> sessions) => _sessions = sessions;

		public Session GetSession() => _sessions();
	}

	[Container]
	[Transient<Widget>]
	[Singleton<Engine>]
	[Scoped<Session>]
	[Transient<Consumer>]
	[Scoped<ScopedConsumer>]
	public partial class RelationshipContainer;
}
