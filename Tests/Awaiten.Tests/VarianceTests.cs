namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of generic interface variance: a consumer requesting a closed generic interface with no
///     exact registration is satisfied by a variance-compatible registration. A registered
///     <c>IHandler&lt;DomainEvent&gt;</c> (<c>in T</c>) satisfies a request for <c>IHandler&lt;OrderPlaced&gt;</c>
///     through contravariance; a registered <c>IFactory&lt;OrderPlaced&gt;</c> (<c>out T</c>) satisfies a request
///     for <c>IFactory&lt;DomainEvent&gt;</c> through covariance. An exact registration always wins, an invariant
///     interface never matches a different closure, and the same compatibility check drives single-service
///     resolution, collection union and top-level dispatch. The containers and services are nested types, so the
///     enclosing class is <c>partial</c>.
/// </summary>
public partial class VarianceTests
{
	[Fact]
	public async Task Contravariance_RegisteredBaseHandlerSatisfiesDerivedRequest()
	{
		using ContravariantContainer.Root container = new();

		// OrderConsumer requires IHandler<OrderPlaced>; only IHandler<DomainEvent> is registered, and
		// IHandler<in T> is contravariant, so the base handler satisfies the derived request.
		OrderConsumer consumer = container.Resolve<OrderConsumer>();

		await That(consumer.Handler).Is<DomainEventHandler>();
	}

	[Fact]
	public async Task Covariance_RegisteredDerivedFactorySatisfiesBaseRequest()
	{
		using CovariantContainer.Root container = new();

		// BaseConsumer requires IFactory<DomainEvent>; only IFactory<OrderPlaced> is registered, and
		// IFactory<out T> is covariant, so the derived factory satisfies the base request.
		BaseConsumer consumer = container.Resolve<BaseConsumer>();

		await That(consumer.Factory).Is<OrderPlacedFactory>();
	}

	[Fact]
	public async Task ExactRegistration_WinsOverVarianceCompatibleOne()
	{
		using ExactWinsContainer.Root container = new();

		// Both IHandler<DomainEvent> and the exact IHandler<OrderPlaced> are registered; the exact one wins.
		OrderConsumer consumer = container.Resolve<OrderConsumer>();

		await That(consumer.Handler).Is<OrderPlacedHandler>();
	}

	[Fact]
	public async Task InvariantInterface_DoesNotMatchADifferentClosure()
	{
		using InvariantContainer.Root container = new();

		// IStore<T> declares no in/out, so a registered IStore<DomainEvent> does NOT satisfy IStore<OrderPlaced>;
		// the consumer's parameter falls back to the exact registration that is present.
		StoreConsumer consumer = container.Resolve<StoreConsumer>();

		await That(consumer.Store).Is<OrderPlacedStore>();
	}

	[Fact]
	public async Task Contravariance_CollectionUnionsVarianceCompatibleRegistrations()
	{
		using ContravariantCollectionContainer.Root container = new();

		// IEnumerable<IHandler<OrderPlaced>>: the exact OrderPlacedHandler leads, then the contravariant
		// IHandler<DomainEvent> registration is unioned in.
		System.Collections.Generic.List<IHandler<OrderPlaced>> handlers =
			new(container.Resolve<OrderHandlerCollectionConsumer>().Handlers);

		await That(handlers.Count).IsEqualTo(2);
		await That(handlers[0]).Is<OrderPlacedHandler>();
		await That(handlers[1]).Is<DomainEventHandler>();
	}

	[Fact]
	public async Task Contravariance_CollectionWithNoExactMemberYieldsTheVarianceMatch()
	{
		using ContravariantCollectionOnlyContainer.Root container = new();

		// Only IHandler<DomainEvent> is registered; the collection of IHandler<OrderPlaced> still resolves it
		// through contravariance rather than an empty set.
		System.Collections.Generic.List<IHandler<OrderPlaced>> handlers =
			new(container.Resolve<OrderHandlerCollectionConsumer>().Handlers);

		await That(handlers.Count).IsEqualTo(1);
		await That(handlers[0]).Is<DomainEventHandler>();
	}

	[Fact]
	public async Task Covariance_CollectionUnionsVarianceCompatibleRegistrations()
	{
		using CovariantCollectionContainer.Root container = new();

		// IEnumerable<IFactory<DomainEvent>>: the covariant IFactory<OrderPlaced> registration satisfies it.
		System.Collections.Generic.List<IFactory<DomainEvent>> factories =
			new(container.Resolve<DomainFactoryCollectionConsumer>().Factories);

		await That(factories.Count).IsEqualTo(1);
		await That(factories[0]).Is<OrderPlacedFactory>();
	}

	[Fact]
	public async Task InvariantInterface_CollectionDoesNotPullInADifferentClosure()
	{
		using InvariantCollectionContainer.Root container = new();

		// IStore<T> is invariant, so the collection of IStore<OrderPlaced> contains only the exact registration.
		System.Collections.Generic.List<IStore<OrderPlaced>> stores =
			new(container.Resolve<StoreCollectionConsumer>().Stores);

		await That(stores.Count).IsEqualTo(1);
		await That(stores[0]).Is<OrderPlacedStore>();
	}

	[Fact]
	public async Task TopLevelDispatch_Contravariance_ResolvesVarianceCompatibleRegistration()
	{
		using ContravariantContainer.Root container = new();

		// IHandler<OrderPlaced> has no exact registration but is variance-redirected (OrderConsumer requests it),
		// so the imperative Resolve<T> now routes to the registered IHandler<DomainEvent>.
		IHandler<OrderPlaced> handler = container.Resolve<IHandler<OrderPlaced>>();

		await That(handler).Is<DomainEventHandler>();
	}

	[Fact]
	public async Task TopLevelDispatch_Covariance_ResolvesVarianceCompatibleRegistration()
	{
		using CovariantContainer.Root container = new();

		IFactory<DomainEvent> factory = container.Resolve<IFactory<DomainEvent>>();

		await That(factory).Is<OrderPlacedFactory>();
	}

	public class DomainEvent;

	public sealed class OrderPlaced : DomainEvent;

	// Contravariant: an IHandler<DomainEvent> can be used wherever an IHandler<OrderPlaced> is required.
	public interface IHandler<in T>
	{
		string Name { get; }
	}

	public sealed class DomainEventHandler : IHandler<DomainEvent>
	{
		public string Name => nameof(DomainEventHandler);
	}

	public sealed class OrderPlacedHandler : IHandler<OrderPlaced>
	{
		public string Name => nameof(OrderPlacedHandler);
	}

	public sealed class OrderConsumer
	{
		public OrderConsumer(IHandler<OrderPlaced> handler) => Handler = handler;

		public IHandler<OrderPlaced> Handler { get; }
	}

	// Covariant: an IFactory<OrderPlaced> can be used wherever an IFactory<DomainEvent> is required.
	public interface IFactory<out T>
		where T : DomainEvent
	{
		T Create();
	}

	public sealed class OrderPlacedFactory : IFactory<OrderPlaced>
	{
		public OrderPlaced Create() => new();
	}

	public sealed class BaseConsumer
	{
		public BaseConsumer(IFactory<DomainEvent> factory) => Factory = factory;

		public IFactory<DomainEvent> Factory { get; }
	}

	// Invariant: no in/out, so a different closure never matches.
	public interface IStore<T>;

	public sealed class DomainEventStore : IStore<DomainEvent>;

	public sealed class OrderPlacedStore : IStore<OrderPlaced>;

	public sealed class StoreConsumer
	{
		public StoreConsumer(IStore<OrderPlaced> store) => Store = store;

		public IStore<OrderPlaced> Store { get; }
	}

	public sealed class OrderHandlerCollectionConsumer
	{
		public OrderHandlerCollectionConsumer(System.Collections.Generic.IEnumerable<IHandler<OrderPlaced>> handlers) => Handlers = handlers;

		public System.Collections.Generic.IEnumerable<IHandler<OrderPlaced>> Handlers { get; }
	}

	public sealed class DomainFactoryCollectionConsumer
	{
		public DomainFactoryCollectionConsumer(System.Collections.Generic.IEnumerable<IFactory<DomainEvent>> factories) => Factories = factories;

		public System.Collections.Generic.IEnumerable<IFactory<DomainEvent>> Factories { get; }
	}

	public sealed class StoreCollectionConsumer
	{
		public StoreCollectionConsumer(System.Collections.Generic.IEnumerable<IStore<OrderPlaced>> stores) => Stores = stores;

		public System.Collections.Generic.IEnumerable<IStore<OrderPlaced>> Stores { get; }
	}

	[Container]
	[Transient<DomainEventHandler, IHandler<DomainEvent>>]
	[Transient<OrderConsumer>]
	public static partial class ContravariantContainer;

	[Container]
	[Transient<OrderPlacedFactory, IFactory<OrderPlaced>>]
	[Transient<BaseConsumer>]
	public static partial class CovariantContainer;

	[Container]
	[Transient<DomainEventHandler, IHandler<DomainEvent>>]
	[Transient<OrderPlacedHandler, IHandler<OrderPlaced>>]
	[Transient<OrderConsumer>]
	public static partial class ExactWinsContainer;

	[Container]
	[Transient<DomainEventStore, IStore<DomainEvent>>]
	[Transient<OrderPlacedStore, IStore<OrderPlaced>>]
	[Transient<StoreConsumer>]
	public static partial class InvariantContainer;

	[Container]
	[Transient<OrderPlacedHandler, IHandler<OrderPlaced>>]
	[Transient<DomainEventHandler, IHandler<DomainEvent>>]
	[Transient<OrderHandlerCollectionConsumer>]
	public static partial class ContravariantCollectionContainer;

	[Container]
	[Transient<DomainEventHandler, IHandler<DomainEvent>>]
	[Transient<OrderHandlerCollectionConsumer>]
	public static partial class ContravariantCollectionOnlyContainer;

	[Container]
	[Transient<OrderPlacedFactory, IFactory<OrderPlaced>>]
	[Transient<DomainFactoryCollectionConsumer>]
	public static partial class CovariantCollectionContainer;

	[Container]
	[Transient<DomainEventStore, IStore<DomainEvent>>]
	[Transient<OrderPlacedStore, IStore<OrderPlaced>>]
	[Transient<StoreCollectionConsumer>]
	public static partial class InvariantCollectionContainer;
}
