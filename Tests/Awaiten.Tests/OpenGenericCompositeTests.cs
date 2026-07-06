using System.Collections.Generic;
using System.Linq;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of open generic composites: <c>[Composite(typeof(CompositeHandler&lt;&gt;), typeof(IHandler&lt;&gt;))]</c>
///     synthesizes a closed composite per closing of the service present in the graph, so resolving
///     <c>IHandler&lt;Order&gt;</c> yields the <c>CompositeHandler&lt;Order&gt;</c> fanning out to the other
///     <c>IHandler&lt;Order&gt;</c> registrations (never itself).
/// </summary>
public partial class OpenGenericCompositeTests
{
	[Fact]
	public async Task OpenComposite_FrontsEachClosing_FanningToTheOtherMembers()
	{
		using OpenCompositeContainer.Root container = new();

		IHandler<Order> order = container.Resolve<IHandler<Order>>();
		IHandler<Customer> customer = container.Resolve<IHandler<Customer>>();

		await That(order).Is<CompositeHandler<Order>>();
		await That(order.Describe()).IsEqualTo("Composite[A<Order>,B<Order>]");
		await That(customer.Describe()).IsEqualTo("Composite[A<Customer>,B<Customer>]");
	}

	[Fact]
	public async Task OpenComposite_IsExcludedFromItsOwnFanOut()
	{
		using OpenCompositeContainer.Root container = new();

		CompositeHandler<Order> composite = (CompositeHandler<Order>)container.Resolve<IHandler<Order>>();

		await That(composite.Inner.Any(h => h is CompositeHandler<Order>)).IsFalse()
			.Because("a composite never fans out to itself");
		await That(composite.Inner).HasCount(2);
	}

	public sealed class Order;

	public sealed class Customer;

	public interface IHandler<T>
	{
		string Describe();
	}

	public sealed class HandlerA<T> : IHandler<T>
	{
		public string Describe() => $"A<{typeof(T).Name}>";
	}

	public sealed class HandlerB<T> : IHandler<T>
	{
		public string Describe() => $"B<{typeof(T).Name}>";
	}

	public sealed class CompositeHandler<T> : IHandler<T>
	{
		public CompositeHandler(IEnumerable<IHandler<T>> inner) => Inner = inner.ToList();

		public IReadOnlyList<IHandler<T>> Inner { get; }

		public string Describe() => $"Composite[{string.Join(",", Inner.Select(h => h.Describe()))}]";
	}

	// A concrete root seeds open generic expansion for the closings the application uses.
	public sealed class App
	{
		public App(IHandler<Order> order, IHandler<Customer> customer)
		{
		}
	}

	[Container]
	[Transient(typeof(HandlerA<>), typeof(IHandler<>))]
	[Transient(typeof(HandlerB<>), typeof(IHandler<>))]
	[Transient<App>]
	[Composite(typeof(CompositeHandler<>), typeof(IHandler<>))]
	public static partial class OpenCompositeContainer;
}
