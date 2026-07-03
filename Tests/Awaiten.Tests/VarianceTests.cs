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

	[Fact]
	public async Task ImperativeResolve_Contravariance_ResolvesVarianceCompatibleRegistration()
	{
		using ImperativeContravariantContainer.Root container = new();

		// No consumer parameter ever requests IHandler<OrderPlaced>, so no compile-time dispatch alias exists;
		// the runtime variance fallback matches the registered IHandler<DomainEvent> (in T) on the first request.
		IHandler<OrderPlaced> handler = container.Resolve<IHandler<OrderPlaced>>();

		await That(handler).Is<DomainEventHandler>();
	}

	[Fact]
	public async Task ImperativeResolve_Covariance_ResolvesVarianceCompatibleRegistration()
	{
		using ImperativeCovariantContainer.Root container = new();

		IFactory<DomainEvent> factory = container.Resolve<IFactory<DomainEvent>>();

		await That(factory).Is<OrderPlacedFactory>();
	}

	[Fact]
	public async Task ImperativeResolve_NearestCandidateWins()
	{
		using ImperativeNearestContainer.Root container = new();

		// Both IHandler<object> and IHandler<DomainEvent> satisfy the request; the runtime fallback picks the
		// nearest closure, mirroring the compile-time rule, not the first-registered IHandler<object>.
		IHandler<OrderPlaced> handler = container.Resolve<IHandler<OrderPlaced>>();

		await That(handler).Is<DomainEventHandler>();
	}

	[Fact]
	public async Task ImperativeResolve_RepeatedRequestsFollowTheMemoizedRoute()
	{
		using ImperativeNearestContainer.Root container = new();

		// The second request takes the memoized route (requested type -> matched service) instead of
		// re-scanning the candidates; both land on the same registration.
		IHandler<OrderPlaced> first = container.Resolve<IHandler<OrderPlaced>>();
		IHandler<OrderPlaced> second = container.Resolve<IHandler<OrderPlaced>>();

		await That(first).Is<DomainEventHandler>();
		await That(second).Is<DomainEventHandler>();
	}

	[Fact]
	public async Task ImperativeResolve_ScopedRegistration_ResolvesOnTheRequestingScope()
	{
		using ImperativeScopedContainer.Root container = new();
		using IAwaitenScope scope1 = container.CreateScope();
		using IAwaitenScope scope2 = container.CreateScope();

		// The fallback reuses the matched registration's resolver on the requesting scope: the variance-routed
		// request and the exact request share the scope's single instance, and another scope gets its own.
		IHandler<OrderPlaced> routed = scope1.Resolve<IHandler<OrderPlaced>>();

		await That(routed).IsSameAs(scope1.Resolve<IHandler<DomainEvent>>());
		await That(routed).IsSameAs(scope1.Resolve<IHandler<OrderPlaced>>());
		await That(routed).IsNotSameAs(scope2.Resolve<IHandler<OrderPlaced>>());
	}

	[Fact]
	public async Task ImperativeResolve_ExactRegistrationWinsOverVariance()
	{
		using ExactWinsContainer.Root container = new();

		// The exact IHandler<OrderPlaced> bucket hits before any variance fallback runs.
		IHandler<OrderPlaced> handler = container.Resolve<IHandler<OrderPlaced>>();

		await That(handler).Is<OrderPlacedHandler>();
	}

	[Fact]
	public async Task ImperativeResolve_KeyedRegistrationIsNeverACandidate()
	{
		using ImperativeKeyedContainer.Root container = new();

		// The only variance-compatible registration is keyed, and keyed registrations are reached solely
		// through their key - so the imperative request still throws the standard resolution failure.
		await That(() => container.Resolve<IHandler<OrderPlaced>>()).Throws<InvalidOperationException>();
	}

	[Fact]
	public async Task ImperativeResolve_ValueTypeClosureThrows()
	{
		using ImperativeNearestContainer.Root container = new();

		// int converts to object only by boxing, never by a reference conversion, so IHandler<object> does not
		// satisfy IHandler<int> - exactly as at compile time.
		await That(() => container.Resolve<IHandler<int>>()).Throws<InvalidOperationException>();
	}

	[Fact]
	public async Task ImperativeResolve_UnboundGenericThrows()
	{
		using ImperativeNearestContainer.Root container = new();

		// An unbound generic is not a constructed type, so the fallback never considers it.
		await That(() => container.Resolve(typeof(IHandler<>))).Throws<InvalidOperationException>();
	}

	[Fact]
	public async Task ImperativeResolve_InvariantInterfaceThrows()
	{
		using InvariantContainer.Root container = new();

		// IStore<T> declares no in/out, so no registration is a variance candidate and an unregistered closure
		// still throws.
		await That(() => container.Resolve<IStore<SpecialDomainEvent>>()).Throws<InvalidOperationException>();
	}

	[Fact]
	public async Task Contravariance_NearestCandidateWins()
	{
		using NearestContravariantContainer.Root container = new();

		// Both IHandler<object> and IHandler<DomainEvent> satisfy IHandler<OrderPlaced>; the nearest closure wins
		// (the most-derived argument under contravariance), not the first-registered IHandler<object>.
		OrderConsumer consumer = container.Resolve<OrderConsumer>();

		await That(consumer.Handler).Is<DomainEventHandler>();
	}

	[Fact]
	public async Task Covariance_NearestCandidateWins()
	{
		using NearestCovariantContainer.Root container = new();

		// Both IFactory<SpecialOrderPlaced> and IFactory<SpecialDomainEvent> satisfy IFactory<DomainEvent>; the
		// nearest closure wins (the most-general argument under covariance), not the first-registered deeper one.
		BaseConsumer consumer = container.Resolve<BaseConsumer>();

		await That(consumer.Factory).Is<SpecialDomainEventFactory>();
	}

	[Fact]
	public async Task Contravariance_NestedVariantArgumentMatches()
	{
		using NestedVarianceContainer.Root container = new();

		// The variance conversion composes: IEnumerable<OrderPlaced> converts to IEnumerable<DomainEvent> (out T),
		// so IHandler<IEnumerable<DomainEvent>> satisfies the requested IHandler<IEnumerable<OrderPlaced>> (in T).
		NestedOrderConsumer consumer = container.Resolve<NestedOrderConsumer>();

		await That(consumer.Handler).Is<EnumerableEventHandler>();
	}

	[Fact]
	public async Task Covariance_InterfaceArgumentSatisfiesObjectRequest()
	{
		using ObjectRequestContainer.Root container = new();

		// An interface argument converts to object by an implicit reference conversion, so the covariant
		// IProvider<IHandler<DomainEvent>> satisfies the requested IProvider<object>.
		ObjectProviderConsumer consumer = container.Resolve<ObjectProviderConsumer>();

		await That(consumer.Provider).Is<HandlerProvider>();
	}

	[Fact]
	public async Task Contravariance_FuncRelationshipResolvesVarianceCompatibleRegistration()
	{
		using FuncRelationshipContainer.Root container = new();

		// A Func<T> relationship defers the same redirected resolver: Func is covariant in its result, so the
		// emitted Func<IHandler<DomainEvent>> converts to the declared Func<IHandler<OrderPlaced>>.
		FuncOrderConsumer consumer = container.Resolve<FuncOrderConsumer>();

		await That(consumer.HandlerFactory()).Is<DomainEventHandler>();
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

	public sealed class ObjectHandler : IHandler<object>
	{
		public string Name => nameof(ObjectHandler);
	}

	// Handles any collection of domain events; its closure's nested argument is itself variant (IEnumerable's
	// out T), so it satisfies a requested IHandler<IEnumerable<OrderPlaced>> through the composed conversion.
	public sealed class EnumerableEventHandler : IHandler<System.Collections.Generic.IEnumerable<DomainEvent>>
	{
		public string Name => nameof(EnumerableEventHandler);
	}

	public sealed class NestedOrderConsumer
	{
		public NestedOrderConsumer(IHandler<System.Collections.Generic.IEnumerable<OrderPlaced>> handler) => Handler = handler;

		public IHandler<System.Collections.Generic.IEnumerable<OrderPlaced>> Handler { get; }
	}

	public sealed class FuncOrderConsumer
	{
		public FuncOrderConsumer(Func<IHandler<OrderPlaced>> handlerFactory) => HandlerFactory = handlerFactory;

		public Func<IHandler<OrderPlaced>> HandlerFactory { get; }
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

	public class SpecialDomainEvent : DomainEvent;

	public sealed class SpecialOrderPlaced : SpecialDomainEvent;

	public sealed class SpecialDomainEventFactory : IFactory<SpecialDomainEvent>
	{
		public SpecialDomainEvent Create() => new();
	}

	public sealed class SpecialOrderPlacedFactory : IFactory<SpecialOrderPlaced>
	{
		public SpecialOrderPlaced Create() => new();
	}

	// Covariant without a constraint, so an interface argument can be requested as plain object.
	public interface IProvider<out T>;

	public sealed class HandlerProvider : IProvider<IHandler<DomainEvent>>;

	public sealed class ObjectProviderConsumer
	{
		public ObjectProviderConsumer(IProvider<object> provider) => Provider = provider;

		public IProvider<object> Provider { get; }
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

	// The farther IHandler<object> is registered first, so registration order alone would pick it.
	[Container]
	[Transient<ObjectHandler, IHandler<object>>]
	[Transient<DomainEventHandler, IHandler<DomainEvent>>]
	[Transient<OrderConsumer>]
	public static partial class NearestContravariantContainer;

	// The farther IFactory<SpecialOrderPlaced> is registered first, so registration order alone would pick it.
	[Container]
	[Transient<SpecialOrderPlacedFactory, IFactory<SpecialOrderPlaced>>]
	[Transient<SpecialDomainEventFactory, IFactory<SpecialDomainEvent>>]
	[Transient<BaseConsumer>]
	public static partial class NearestCovariantContainer;

	[Container]
	[Transient<EnumerableEventHandler, IHandler<System.Collections.Generic.IEnumerable<DomainEvent>>>]
	[Transient<NestedOrderConsumer>]
	public static partial class NestedVarianceContainer;

	[Container]
	[Transient<HandlerProvider, IProvider<IHandler<DomainEvent>>>]
	[Transient<ObjectProviderConsumer>]
	public static partial class ObjectRequestContainer;

	[Container]
	[Transient<DomainEventHandler, IHandler<DomainEvent>>]
	[Transient<FuncOrderConsumer>]
	public static partial class FuncRelationshipContainer;

	// No consumer requests a differently-closed IHandler<T>, so only the runtime fallback can variance-route.
	[Container]
	[Transient<DomainEventHandler, IHandler<DomainEvent>>]
	public static partial class ImperativeContravariantContainer;

	[Container]
	[Transient<OrderPlacedFactory, IFactory<OrderPlaced>>]
	public static partial class ImperativeCovariantContainer;

	// The farther IHandler<object> is registered first, so registration order alone would pick it.
	[Container]
	[Transient<ObjectHandler, IHandler<object>>]
	[Transient<DomainEventHandler, IHandler<DomainEvent>>]
	public static partial class ImperativeNearestContainer;

	[Container]
	[Scoped<DomainEventHandler, IHandler<DomainEvent>>]
	public static partial class ImperativeScopedContainer;

	[Container]
	[Transient<DomainEventHandler, IHandler<DomainEvent>>(Key = "k")]
	public static partial class ImperativeKeyedContainer;
}
