using System.Collections.Generic;
using System.Linq;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of open generic expansion requested by injected members and lifecycle hook parameters: a
///     closing that only an open registration provides is constructed for them exactly as for a constructor parameter.
/// </summary>
public partial class OpenGenericInjectedMemberTests
{
	public sealed class Order;

	public interface IRepository<T>;

	public sealed class Repository<T> : IRepository<T>
	{
		public IValidator<T>? Validator { get; set; }
	}

	public sealed class FastRepository<T> : IRepository<T>;

	public sealed class SlowRepository<T> : IRepository<T>;

	public interface IValidator<T>;

	public sealed class Validator<T> : IValidator<T>;

	public sealed class OrderService
	{
		public IRepository<Order>? Orders { get; set; }
	}

	public sealed class InjectedOrderService
	{
		[Inject]
		public IRepository<Order>? Orders { get; set; }
	}

	public sealed class OptionalOrderService
	{
		[Inject(Optional = true)]
		public IRepository<Order>? Orders { get; set; }
	}

	public sealed class KeyedOrderService
	{
		[Inject]
		[FromKey("fast")]
		public IRepository<Order>? Fast { get; set; }

		public IRepository<Order>? Slow { get; set; }
	}

	public sealed class CollectionOrderService
	{
		[Inject]
		public IReadOnlyList<IRepository<Order>> Orders { get; set; } = [];
	}

	public sealed class HookedOrderService
	{
		public IRepository<Order>? Orders { get; set; }
	}

	public sealed class RepositoryConsumer(IRepository<Order> orders)
	{
		public IRepository<Order> Orders { get; } = orders;
	}

	[Test]
	public async Task InjectPropertyEntry_IsFilledFromTheOpenRegistration()
	{
		using InjectPropertyContainer.Root container = new();

		OrderService service = container.Resolve<OrderService>();

		await That(service.Orders).Is<Repository<Order>>()
			.Because("the [InjectProperty] entry requests IRepository<Order>, which the open registration provides");
	}

	[Test]
	public async Task InjectAttributeProperty_IsFilledFromTheOpenRegistration()
	{
		using InjectAttributeContainer.Root container = new();

		InjectedOrderService service = container.Resolve<InjectedOrderService>();

		await That(service.Orders).Is<Repository<Order>>()
			.Because("an [Inject] property closes the open registration like a constructor parameter");
	}

	[Test]
	public async Task OpenTypeofEntry_IsFilledFromTheOpenRegistrationPerClosing()
	{
		using OpenTypeofContainer.Root container = new();

		Repository<Order> orders = (Repository<Order>)container.Resolve<RepositoryConsumer>().Orders;

		await That(orders.Validator).Is<Validator<Order>>()
			.Because("the open entry applies to Repository<Order>, whose property closes the open IValidator<> registration");
	}

	[Test]
	public async Task KeyedProperty_IsFilledFromTheOpenRegistrationUnderThatKey()
	{
		using KeyedContainer.Root container = new();

		KeyedOrderService service = container.Resolve<KeyedOrderService>();

		await That(service.Fast).Is<FastRepository<Order>>()
			.Because("the [FromKey(\"fast\")] property resolves the open registration keyed 'fast', closed at Order");
		await That(service.Slow).Is<SlowRepository<Order>>()
			.Because("the entry keyed 'slow' resolves the open registration keyed 'slow', closed at Order");
	}

	[Test]
	public async Task OptionalProperty_IsFilledFromTheOpenRegistration()
	{
		using OptionalContainer.Root container = new();

		OptionalOrderService service = container.Resolve<OptionalOrderService>();

		await That(service.Orders).Is<Repository<Order>>()
			.Because("an Optional property is filled when an open registration can provide the closing, like a constructor parameter");
	}

	[Test]
	public async Task CollectionProperty_IsFilledWithEveryOpenRegistration()
	{
		using CollectionContainer.Root container = new();

		CollectionOrderService service = container.Resolve<CollectionOrderService>();

		await That(service.Orders.Select(repository => repository.GetType()))
			.IsEqualTo([typeof(FastRepository<Order>), typeof(SlowRepository<Order>),])
			.Because("the collection element IRepository<Order> closes both open registrations, in declaration order");
	}

	[Test]
	public async Task HookParameter_IsResolvedFromTheOpenRegistration()
	{
		using HookContainer.Root container = new();

		HookedOrderService service = container.Resolve<HookedOrderService>();

		await That(service.Orders).Is<Repository<Order>>()
			.Because("the OnActivated hook's IRepository<Order> parameter closes the open registration");
	}

	[Container]
	[Singleton(typeof(Repository<>), typeof(IRepository<>))]
	[Singleton<OrderService>]
	[InjectProperty<OrderService>(nameof(OrderService.Orders))]
	public static partial class InjectPropertyContainer;

	[Container]
	[Singleton(typeof(Repository<>), typeof(IRepository<>))]
	[Singleton<InjectedOrderService>]
	public static partial class InjectAttributeContainer;

	[Container]
	[Singleton(typeof(Repository<>), typeof(IRepository<>))]
	[Singleton(typeof(Validator<>), typeof(IValidator<>))]
	[Singleton<RepositoryConsumer>]
	[InjectProperty(typeof(Repository<>), nameof(Repository<object>.Validator))]
	public static partial class OpenTypeofContainer;

	[Container]
	[Singleton(typeof(FastRepository<>), typeof(IRepository<>), Key = "fast")]
	[Singleton(typeof(SlowRepository<>), typeof(IRepository<>), Key = "slow")]
	[Singleton<KeyedOrderService>]
	[InjectProperty<KeyedOrderService>(nameof(KeyedOrderService.Slow), Key = "slow")]
	public static partial class KeyedContainer;

	[Container]
	[Singleton(typeof(Repository<>), typeof(IRepository<>))]
	[Singleton<OptionalOrderService>]
	public static partial class OptionalContainer;

	[Container]
	[Singleton(typeof(FastRepository<>), typeof(IRepository<>))]
	[Singleton(typeof(SlowRepository<>), typeof(IRepository<>))]
	[Singleton<CollectionOrderService>]
	public static partial class CollectionContainer;

	[Container]
	[Singleton(typeof(Repository<>), typeof(IRepository<>))]
	[Singleton<HookedOrderService>(OnActivated = nameof(Activated))]
	public static partial class HookContainer
	{
		private static void Activated(HookedOrderService service, IRepository<Order> orders) => service.Orders = orders;
	}
}
