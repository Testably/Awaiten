using System.Collections.Generic;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of runtime-type dispatch: a dependency typed <c>IReadOnlyDictionary&lt;Type, Func&lt;TBase&gt;&gt;</c>
///     holds one entry per unkeyed closed registration of a single-parameter generic that implements <c>TBase</c>,
///     keyed by the closed type argument, and each value resolves that closing exactly like a <c>Func</c> would.
/// </summary>
public partial class TypeDispatchTests
{
	[Test]
	public async Task Dispatch_ResolvesTheHandlerOfTheMessagesRuntimeType()
	{
		using MediatorContainer.Root container = new();

		Mediator mediator = container.Resolve<Mediator>();

		await That(mediator.Send(new OrderPlaced())).IsEqualTo("placed")
			.Because("the OrderPlaced entry resolves the scanned IDispatchHandler<OrderPlaced>");
		await That(mediator.Send(new OrderShipped())).IsEqualTo("shipped")
			.Because("the OrderShipped entry resolves the scanned IDispatchHandler<OrderShipped>");
	}

	[Test]
	public async Task Dispatch_HoldsOneEntryPerClosingKeyedByTheTypeArgument()
	{
		using MediatorContainer.Root container = new();

		IReadOnlyDictionary<Type, Func<IDispatchHandler>> handlers = container.Resolve<Mediator>().Handlers;

		await That(handlers.Keys).IsEqualTo([typeof(OrderPlaced), typeof(OrderShipped), typeof(OrderCancelled),]).InAnyOrder()
			.Because("every closed registration of IDispatchHandler<> is keyed by its type argument, and the open fallback adds none");
	}

	[Test]
	public async Task Dispatch_UnknownType_BehavesLikeAPlainDictionary()
	{
		using MediatorContainer.Root container = new();

		IReadOnlyDictionary<Type, Func<IDispatchHandler>> handlers = container.Resolve<Mediator>().Handlers;

		await That(handlers.TryGetValue(typeof(string), out _)).IsFalse()
			.Because("a type without a closing has no entry");
		await That(() => handlers[typeof(string)]).Throws<KeyNotFoundException>()
			.Because("the dispatch dictionary is a plain BCL dictionary");
	}

	[Test]
	public async Task Dispatch_EachEntryKeepsTheClosingsOwnLifetime()
	{
		using MediatorContainer.Root container = new();

		IReadOnlyDictionary<Type, Func<IDispatchHandler>> handlers = container.Resolve<Mediator>().Handlers;

		await That(handlers[typeof(OrderPlaced)]()).IsSameAs(handlers[typeof(OrderPlaced)]())
			.Because("the scanned handlers are singletons, so each call returns the same instance");
		await That(handlers[typeof(OrderCancelled)]()).IsNotSameAs(handlers[typeof(OrderCancelled)]())
			.Because("the explicitly registered OrderCancelled handler is transient, so each call builds a new one");
	}

	[Test]
	public async Task Dispatch_ExplicitRegistrationOverridesAScannedHandler()
	{
		using MediatorContainer.Root container = new();

		Mediator mediator = container.Resolve<Mediator>();

		await That(mediator.Send(new OrderCancelled())).IsEqualTo("cancelled by override")
			.Because("an entry resolves the closing's single-resolution winner, and an explicit registration beats the scan match");
	}

	[Test]
	public async Task Dispatch_DecoratedClosing_ResolvesTheDecorator()
	{
		using DecoratedMediatorContainer.Root container = new();

		Mediator mediator = container.Resolve<Mediator>();

		await That(mediator.Send(new OrderPlaced())).IsEqualTo("logged placed")
			.Because("the entry resolves the closing as a direct dependency would, through its decorator");
	}

	[Test]
	public async Task Dispatch_InjectedIntoAProperty_IsFilledLikeAConstructorParameter()
	{
		using MediatorContainer.Root container = new();

		PropertyMediator mediator = container.Resolve<PropertyMediator>();

		await That(mediator.Handlers![typeof(OrderShipped)]().Handle(new OrderShipped())).IsEqualTo("shipped")
			.Because("an [Inject] property typed as a dispatch dictionary is filled with the same entries");
	}

	[Test]
	public async Task Dispatch_ScopedHandler_ResolvesFromTheScopeThatBuiltTheConsumer()
	{
		using ScopedMediatorContainer.Root container = new();
		using IAwaitenScope scope1 = container.CreateScope();
		using IAwaitenScope scope2 = container.CreateScope();

		IReadOnlyDictionary<Type, Func<IDispatchHandler>> first = scope1.Resolve<Mediator>().Handlers;
		IReadOnlyDictionary<Type, Func<IDispatchHandler>> second = scope2.Resolve<Mediator>().Handlers;

		await That(first[typeof(OrderPlaced)]()).IsSameAs(first[typeof(OrderPlaced)]())
			.Because("a scoped handler is shared within the scope that built the mediator");
		await That(first[typeof(OrderPlaced)]()).IsNotSameAs(second[typeof(OrderPlaced)]())
			.Because("another scope resolves its own scoped handler");
	}

	public interface IDispatchHandler
	{
		string Handle(object message);
	}

	public interface IDispatchHandler<T> : IDispatchHandler;

	public sealed class OrderPlaced;

	public sealed class OrderShipped;

	public sealed class OrderCancelled;

	public sealed class OrderPlacedHandler : IDispatchHandler<OrderPlaced>
	{
		public string Handle(object message) => "placed";
	}

	public sealed class OrderShippedHandler : IDispatchHandler<OrderShipped>
	{
		public string Handle(object message) => "shipped";
	}

	public sealed class OrderCancelledHandler : IDispatchHandler<OrderCancelled>
	{
		public string Handle(object message) => "cancelled";
	}

	public sealed class OverridingCancelledHandler : IDispatchHandler<OrderCancelled>
	{
		public string Handle(object message) => "cancelled by override";
	}

	public sealed class FallbackHandler<T> : IDispatchHandler<T>
	{
		public string Handle(object message) => "fallback";
	}

	public sealed class LoggingHandler<T>(IDispatchHandler<T> inner) : IDispatchHandler<T>
	{
		public string Handle(object message) => "logged " + inner.Handle(message);
	}

	public sealed class Mediator(IReadOnlyDictionary<Type, Func<IDispatchHandler>> handlers)
	{
		public IReadOnlyDictionary<Type, Func<IDispatchHandler>> Handlers => handlers;

		public string Send(object message) => handlers[message.GetType()]().Handle(message);
	}

	public sealed class PropertyMediator
	{
		[Inject]
		public IReadOnlyDictionary<Type, Func<IDispatchHandler>>? Handlers { get; set; }
	}

	[Container]
	[Scan(typeof(IDispatchHandler<>), As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
	[Transient<OverridingCancelledHandler, IDispatchHandler<OrderCancelled>>]
	[Transient(typeof(FallbackHandler<>), typeof(IDispatchHandler<>))]
	[Singleton<Mediator>]
	[Singleton<PropertyMediator>]
	public static partial class MediatorContainer;

	[Container]
	[Singleton<OrderPlacedHandler, IDispatchHandler<OrderPlaced>>]
	[Decorate(typeof(LoggingHandler<>), typeof(IDispatchHandler<>))]
	[Singleton<Mediator>]
	public static partial class DecoratedMediatorContainer;

	[Container]
	[Scoped<OrderPlacedHandler, IDispatchHandler<OrderPlaced>>]
	[Scoped<Mediator>]
	public static partial class ScopedMediatorContainer;
}
