using System.Collections.Generic;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of the <c>typeof</c> form of container-side property injection,
///     <c>[InjectProperty(typeof(...), name)]</c>: a closed target behaves like <c>[InjectProperty&lt;T&gt;]</c>, and
///     an unbound generic definition fills the property on every closing of it that the container constructs.
/// </summary>
public partial class PropertyInjectionTypeofTests
{
	public sealed class Clock;

	public sealed class Order;

	public sealed class Customer;

	public sealed class Invoice;

	public interface IValidator<T>;

	public sealed class OrderValidator : IValidator<Order>;

	public sealed class CustomerValidator : IValidator<Customer>;

	public sealed class InvoiceValidator : IValidator<Invoice>;

	public sealed class ListValidator : IValidator<List<Order>>;

	public interface IRepository<T>;

	public sealed class Repository<T> : IRepository<T>
	{
		public Clock? Clock { get; set; }

		public IValidator<T>? Validator { get; set; }
	}

	public sealed class Report
	{
		public Clock? Clock { get; set; }
	}

	// Open-generic closings are expanded from the graph, so these consumers make the container construct them.
	public sealed class RepositoryConsumer(IRepository<Order> orders, IRepository<Customer> customers)
	{
		public IRepository<Order> Orders { get; } = orders;

		public IRepository<Customer> Customers { get; } = customers;
	}

	public sealed class OrderConsumer(IRepository<Order> orders)
	{
		public IRepository<Order> Orders { get; } = orders;
	}

	public sealed class ListConsumer(IRepository<List<Order>> lists)
	{
		public IRepository<List<Order>> Lists { get; } = lists;
	}

	public partial class ClosedTarget
	{
		[Test]
		public async Task ClosedTypeofTarget_IsFilledLikeTheGenericForm()
		{
			using ClosedContainer.Root container = new();

			Report report = container.Resolve<Report>();

			await That(report.Clock).Is<Clock>()
				.Because("a closed typeof target behaves exactly like [InjectProperty<Report>]");
		}

		[Test]
		public async Task ClosedGenericTypeofTarget_FillsOnlyThatClosing()
		{
			using ClosedGenericContainer.Root container = new();

			RepositoryConsumer consumer = container.Resolve<RepositoryConsumer>();

			await That(((Repository<Order>)consumer.Orders).Clock).Is<Clock>()
				.Because("typeof(Repository<Order>) is a closed target, so it fills that closing");
			await That(((Repository<Customer>)consumer.Customers).Clock).IsNull()
				.Because("a closed target does not reach other closings of the same definition");
		}

		[Container]
		[Singleton<Clock>]
		[Singleton<Report>]
		[InjectProperty(typeof(Report), nameof(Report.Clock))]
		public static partial class ClosedContainer;

		[Container]
		[Singleton<Clock>]
		[Singleton(typeof(Repository<>), typeof(IRepository<>))]
		[Transient<RepositoryConsumer>]
		[InjectProperty(typeof(Repository<Order>), nameof(Repository<object>.Clock))]
		public static partial class ClosedGenericContainer;
	}

	public partial class OpenTarget
	{
		[Test]
		public async Task OpenTarget_FillsEveryClosingOfAnOpenRegistration()
		{
			using OpenRegistrationContainer.Root container = new();

			RepositoryConsumer consumer = container.Resolve<RepositoryConsumer>();

			await That(((Repository<Order>)consumer.Orders).Clock).Is<Clock>();
			await That(((Repository<Customer>)consumer.Customers).Clock).Is<Clock>()
				.Because("an unbound generic target fills the property on every closing the open registration produces");
		}

		[Test]
		public async Task OpenTarget_SubstitutesThePropertyTypePerClosing()
		{
			using OpenRegistrationContainer.Root container = new();

			RepositoryConsumer consumer = container.Resolve<RepositoryConsumer>();

			await That(((Repository<Order>)consumer.Orders).Validator).Is<OrderValidator>();
			await That(((Repository<Customer>)consumer.Customers).Validator).Is<CustomerValidator>()
				.Because("IValidator<T> is resolved as IValidator<Order> on one closing and IValidator<Customer> on the other");
		}

		[Test]
		public async Task OpenTarget_FillsAnExplicitClosedRegistration()
		{
			using ExplicitClosedContainer.Root container = new();

			Repository<Invoice> invoices = container.Resolve<Repository<Invoice>>();

			await That(invoices.Clock).Is<Clock>();
			await That(invoices.Validator).Is<InvoiceValidator>()
				.Because("a closing registered explicitly through [Singleton<Repository<Invoice>>] is a closing of the definition too");
		}

		[Test]
		public async Task OpenTarget_FillsANestedGenericClosing()
		{
			using NestedClosingContainer.Root container = new();

			Repository<List<Order>> lists = (Repository<List<Order>>)container.Resolve<ListConsumer>().Lists;

			await That(lists.Clock).Is<Clock>();
			await That(lists.Validator).Is<ListValidator>()
				.Because("a closing whose type argument is itself generic still matches the definition");
		}

		[Test]
		public async Task OpenTarget_FillsEveryClosingOfADefinitionWithSeveralTypeParameters()
		{
			using SeveralParametersContainer.Root container = new();

			Map<Order, Customer> first = container.Resolve<Map<Order, Customer>>();
			Map<Customer, Invoice> second = container.Resolve<Map<Customer, Invoice>>();

			await That(first.Clock).Is<Clock>();
			await That(second.Clock).Is<Clock>()
				.Because("typeof(Map<,>) matches every closing of the two-parameter definition");
		}

		[Test]
		public async Task OpenTarget_FillsTheImplementationInsideAnOpenDecorator()
		{
			using DecoratedContainer.Root container = new();

			IRepository<Order> repository = container.Resolve<OrderConsumer>().Orders;

			await That(repository).Is<AuditedRepository<Order>>();
			await That(((Repository<Order>)((AuditedRepository<Order>)repository).Inner).Clock).Is<Clock>()
				.Because("the decorated closing is still constructed by the container, so its property is filled");
		}

		[Test]
		public async Task OpenTarget_LeavesTypesThatAreNotClosingsUntouched()
		{
			using UntouchedContainer.Root container = new();

			OtherRepository<Order> other = container.Resolve<OtherRepository<Order>>();
			DerivedRepository derived = container.Resolve<DerivedRepository>();

			await That(other.Clock).IsNull()
				.Because("a different generic definition with a property of the same name is not a closing of Repository<>");
			await That(derived.Clock).IsNull()
				.Because("a class deriving from a closing is not itself a closing, like the generic form which matches the implementation type exactly");
		}

		public sealed class Map<TKey, TValue>
		{
			public Clock? Clock { get; set; }
		}

		public sealed class AuditedRepository<T>(IRepository<T> inner) : IRepository<T>
		{
			public IRepository<T> Inner { get; } = inner;
		}

		public sealed class OtherRepository<T>
		{
			public Clock? Clock { get; set; }
		}

		public class BaseRepository<T>
		{
			public Clock? Clock { get; set; }
		}

		public sealed class DerivedRepository : BaseRepository<Order>;

		[Container]
		[Singleton<Clock>]
		[Singleton<OrderValidator, IValidator<Order>>]
		[Singleton<CustomerValidator, IValidator<Customer>>]
		[Singleton(typeof(Repository<>), typeof(IRepository<>))]
		[Transient<RepositoryConsumer>]
		[InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
		[InjectProperty(typeof(Repository<>), nameof(Repository<object>.Validator))]
		public static partial class OpenRegistrationContainer;

		[Container]
		[Singleton<Clock>]
		[Singleton<InvoiceValidator, IValidator<Invoice>>]
		[Singleton<Repository<Invoice>>]
		[InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
		[InjectProperty(typeof(Repository<>), nameof(Repository<object>.Validator))]
		public static partial class ExplicitClosedContainer;

		[Container]
		[Singleton<Clock>]
		[Singleton<ListValidator, IValidator<List<Order>>>]
		[Singleton(typeof(Repository<>), typeof(IRepository<>))]
		[Transient<ListConsumer>]
		[InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
		[InjectProperty(typeof(Repository<>), nameof(Repository<object>.Validator))]
		public static partial class NestedClosingContainer;

		[Container]
		[Singleton<Clock>]
		[Singleton<Map<Order, Customer>>]
		[Singleton<Map<Customer, Invoice>>]
		[InjectProperty(typeof(Map<,>), nameof(Map<object, object>.Clock))]
		public static partial class SeveralParametersContainer;

		[Container]
		[Singleton<Clock>]
		[Singleton(typeof(Repository<>), typeof(IRepository<>))]
		[Decorate(typeof(AuditedRepository<>), typeof(IRepository<>))]
		[Transient<OrderConsumer>]
		[InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
		[InjectProperty(typeof(Repository<>), nameof(Repository<object>.Validator), Optional = true)]
		public static partial class DecoratedContainer;

		[Container]
		[Singleton<Clock>]
		[Singleton<Repository<Order>>]
		[Singleton<OtherRepository<Order>>]
		[Singleton<DerivedRepository>]
		[InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
		public static partial class UntouchedContainer;
	}

	public partial class Options
	{
		[Test]
		public async Task OpenTarget_KeyedEntry_SelectsTheKeyedRegistrationOnEveryClosing()
		{
			using KeyedContainer.Root container = new();

			Consumer<Order> orders = container.Resolve<Consumer<Order>>();
			Consumer<Customer> customers = container.Resolve<Consumer<Customer>>();

			await That(orders.Channel).Is<SlowChannel>();
			await That(customers.Channel).Is<SlowChannel>()
				.Because("the Key on the open entry selects the keyed registration for every closing");
		}

		[Test]
		public async Task ClosedEntry_SpecialisesItsClosing_AndTheOpenEntryFillsTheOthers()
		{
			using SpecialisedContainer.Root container = new();

			Consumer<Order> orders = container.Resolve<Consumer<Order>>();
			Consumer<Customer> customers = container.Resolve<Consumer<Customer>>();

			await That(orders.Channel).Is<FastChannel>()
				.Because("the closed entry wins over the open one on its closing and selects its own Key");
			await That(customers.Channel).Is<SlowChannel>()
				.Because("the open entry still fills every other closing with its Key");
		}

		[Test]
		public async Task OpenTarget_OptionalEntry_WithNoRegistration_IsLeftAtItsDefault()
		{
			using OptionalContainer.Root container = new();

			Consumer<Order> orders = container.Resolve<Consumer<Order>>();

			await That(orders.Metrics).IsNull()
				.Because("an Optional open entry whose service is not registered leaves the property at its default on every closing");
		}

		[Test]
		public async Task OpenTarget_DeferredEntry_BreaksACycleThroughAClosing()
		{
			using DeferredContainer.Root container = new();

			Hub hub = container.Resolve<Hub>();
			Consumer<Order> orders = container.Resolve<Consumer<Order>>();

			await That(hub.Orders).IsSameAs(orders);
			await That(orders.Hub).IsSameAs(hub)
				.Because("the deferred open entry is assigned after the closing is cached, so the mutual cycle terminates");
		}

		public interface IChannel;

		public sealed class FastChannel : IChannel;

		public sealed class SlowChannel : IChannel;

		public sealed class Metrics;

		public sealed class Hub
		{
			public Consumer<Order>? Orders { get; set; }
		}

		public sealed class Consumer<T>
		{
			public IChannel? Channel { get; set; }

			public Metrics? Metrics { get; set; }

			public Hub? Hub { get; set; }
		}

		[Container]
		[Singleton<FastChannel, IChannel>(Key = "fast")]
		[Singleton<SlowChannel, IChannel>(Key = "slow")]
		[Singleton<Consumer<Order>>]
		[Singleton<Consumer<Customer>>]
		[InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Channel), Key = "slow")]
		public static partial class KeyedContainer;

		[Container]
		[Singleton<FastChannel, IChannel>(Key = "fast")]
		[Singleton<SlowChannel, IChannel>(Key = "slow")]
		[Singleton<Consumer<Order>>]
		[Singleton<Consumer<Customer>>]
		[InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Channel), Key = "slow")]
		[InjectProperty<Consumer<Order>>(nameof(Consumer<object>.Channel), Key = "fast")]
		public static partial class SpecialisedContainer;

		[Container]
		[Singleton<Consumer<Order>>]
		[InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Metrics), Optional = true)]
		public static partial class OptionalContainer;

		[Container]
		[Singleton<Hub>]
		[Singleton<Consumer<Order>>]
		[InjectProperty<Hub>(nameof(Hub.Orders), Deferred = true)]
		[InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Hub), Deferred = true)]
		public static partial class DeferredContainer;
	}
}
