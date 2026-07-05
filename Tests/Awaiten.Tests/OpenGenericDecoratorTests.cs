using System.Collections.Generic;
using System.Linq;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of open generic decorators: <c>[Decorate(typeof(Logging&lt;&gt;), typeof(IHandler&lt;&gt;))]</c>
///     synthesizes a closed decorator per closing of the service present in the graph, so resolving
///     <c>IHandler&lt;Order&gt;</c> yields <c>Logging&lt;Order&gt;(Handler&lt;Order&gt;)</c>. Open and explicit closed
///     decorators of one closing interleave by the same (Order, declaration) rules, and a collection view yields the
///     decorated chain.
/// </summary>
public partial class OpenGenericDecoratorTests
{
	[Fact]
	public async Task OpenDecorator_WrapsEachClosingOfTheService()
	{
		using OpenDecoratorContainer.Root container = new();

		IHandler<Order> order = container.Resolve<IHandler<Order>>();
		IHandler<Customer> customer = container.Resolve<IHandler<Customer>>();

		await That(order).Is<Logging<Order>>();
		await That(order.Describe()).IsEqualTo("Log(Handler<Order>)");
		await That(customer.Describe()).IsEqualTo("Log(Handler<Customer>)");
	}

	[Fact]
	public async Task OpenDecorator_InjectedIntoAConsumer_ReceivesTheDecorator()
	{
		using OpenDecoratorContainer.Root container = new();

		OrderConsumer consumer = container.Resolve<OrderConsumer>();

		await That(consumer.Handler.Describe()).IsEqualTo("Log(Handler<Order>)");
	}

	[Fact]
	public async Task OpenAndClosedDecorators_InterleaveByOrderThenDeclaration()
	{
		using InterleavedContainer.Root container = new();

		// Declaration order: open Logging<> (0), then closed Audit for IHandler<Order> (1).
		// The last declared is outermost: Audit(Log(Handler<Order>)).
		await That(container.Resolve<IHandler<Order>>().Describe()).IsEqualTo("Audit(Log(Handler<Order>))");

		// IHandler<Customer> has no closed Audit decorator, only the open Logging<>.
		await That(container.Resolve<IHandler<Customer>>().Describe()).IsEqualTo("Log(Handler<Customer>)");
	}

	[Fact]
	public async Task DecoratedOpenGeneric_AsCollection_YieldsTheDecoratedChain()
	{
		using OpenDecoratorContainer.Root container = new();

		IHandler<Order>[] handlers = container.Resolve<IHandler<Order>[]>();

		await That(handlers).HasCount(1);
		await That(handlers[0].Describe()).IsEqualTo("Log(Handler<Order>)");
	}

	public sealed class Order;

	public sealed class Customer;

	public interface IHandler<T>
	{
		string Describe();
	}

	public sealed class Handler<T> : IHandler<T>
	{
		public string Describe() => $"Handler<{typeof(T).Name}>";
	}

	public sealed class Logging<T> : IHandler<T>
	{
		private readonly IHandler<T> _inner;

		public Logging(IHandler<T> inner) => _inner = inner;

		public string Describe() => $"Log({_inner.Describe()})";
	}

	// A closed decorator for exactly IHandler<Order>, to prove open/closed interleaving on one closing.
	public sealed class Audit : IHandler<Order>
	{
		private readonly IHandler<Order> _inner;

		public Audit(IHandler<Order> inner) => _inner = inner;

		public string Describe() => $"Audit({_inner.Describe()})";
	}

	public sealed class OrderConsumer
	{
		public OrderConsumer(IHandler<Order> handler) => Handler = handler;

		public IHandler<Order> Handler { get; }
	}

	// A concrete root seeds open generic expansion for the closings the application uses.
	public sealed class App
	{
		public App(IHandler<Order> order, IHandler<Customer> customer, IEnumerable<IHandler<Order>> orders)
		{
			_ = orders.ToList();
		}
	}

	[Container]
	[Transient(typeof(Handler<>), typeof(IHandler<>))]
	[Transient<OrderConsumer>]
	[Transient<App>]
	[Decorate(typeof(Logging<>), typeof(IHandler<>))]
	public static partial class OpenDecoratorContainer;

	[Container]
	[Transient(typeof(Handler<>), typeof(IHandler<>))]
	[Transient<App>]
	[Decorate(typeof(Logging<>), typeof(IHandler<>))]
	[Decorate<Audit, IHandler<Order>>]
	public static partial class InterleavedContainer;
}
