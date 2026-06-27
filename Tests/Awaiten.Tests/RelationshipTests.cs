namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of the relationship types: <see cref="Func{T}" /> resolves fresh on each call
///     (respecting the target's lifetime), and each injected <see cref="Lazy{T}" /> memoizes its own
///     value. A relationship is bound to the owner that constructs the consumer, so a singleton's
///     <c>Func&lt;Scoped&gt;</c> resolves from the root container, not from a later child scope. The
///     containers and services are nested types, so the enclosing class is <c>partial</c>.
/// </summary>
public partial class RelationshipTests
{
	[Fact]
	public async Task Func_BoundToAScope_ResolvesScopedInstancesFromThatScope()
	{
		using RelationshipContainer container = new();
		using IAwaitenScope scope = container.CreateScope();

		ScopedConsumer consumer = scope.Resolve<ScopedConsumer>();

		await That(consumer.GetSession()).IsSameAs(scope.Resolve<Session>())
			.Because("the injected Func is bound to the scope, so it returns the scope's single scoped instance");
	}

	[Fact]
	public async Task Func_OfScopedInASingleton_ResolvesFromTheRootNotTheRequestingScope()
	{
		using RelationshipContainer container = new();
		using IAwaitenScope scope = container.CreateScope();

		SingletonSessionConsumer consumer = scope.Resolve<SingletonSessionConsumer>();

		await That(consumer.GetSession()).IsSameAs(container.Resolve<Session>())
			.Because("the singleton is constructed once on the container, so its injected Func<Session> is bound to the root scope and yields the root's scoped Session");
		await That(consumer.GetSession()).IsNotSameAs(scope.Resolve<Session>())
			.Because("the Func is never rebound to whichever scope later asks for the singleton, so it does not return the requesting child scope's instance");
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
	public async Task Func_OfTransient_ResolvesAFreshInstanceEachCall()
	{
		using RelationshipContainer container = new();

		Consumer consumer = container.Resolve<Consumer>();

		Widget first = consumer.MakeWidget();
		Widget second = consumer.MakeWidget();
		await That(first).IsNotSameAs(second);
	}

	[Fact]
	public async Task Lazy_MemoizesItsValue()
	{
		using RelationshipContainer container = new();

		Lazy<Widget> lazy = container.Resolve<Lazy<Widget>>();

		await That(lazy.Value).IsSameAs(lazy.Value);
	}

	public sealed class Widget;

	public sealed class Engine;

	public sealed class Session;

	public sealed class Consumer
	{
		private readonly Func<Widget> _widgets;

		public Consumer(Func<Widget> widgets)
		{
			_widgets = widgets;
		}

		public Widget MakeWidget() => _widgets();
	}

	public sealed class ScopedConsumer
	{
		private readonly Func<Session> _sessions;

		public ScopedConsumer(Func<Session> sessions)
		{
			_sessions = sessions;
		}

		public Session GetSession() => _sessions();
	}

	public sealed class SingletonSessionConsumer
	{
		private readonly Func<Session> _sessions;

		public SingletonSessionConsumer(Func<Session> sessions)
		{
			_sessions = sessions;
		}

		public Session GetSession() => _sessions();
	}

	[Container]
	[Transient<Widget>]
	[Singleton<Engine>]
	[Scoped<Session>]
	[Transient<Consumer>]
	[Scoped<ScopedConsumer>]
	[Singleton<SingletonSessionConsumer>]
	public partial class RelationshipContainer;
}
