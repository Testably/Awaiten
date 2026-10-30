using System.Collections.Generic;
using System.Linq;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of keyed decorators and composites: <c>[Decorate]</c> with <c>Keys</c> or <c>AllKeys</c>
///     wraps the selected keyed registrations, each key in its own chain ordered by <c>Order</c>, so a keyed
///     resolve, <c>[FromKey]</c> injection and the keyed dictionary all get the decorated instance. A
///     <c>[Composite]</c> with a <c>Key</c> fronts that key and fans out over the members under it.
/// </summary>
public partial class KeyedDecoratorCompositeTests
{
	[Test]
	public async Task Keys_DecoratesOnlyTheSelectedKeys()
	{
		using SelectedKeysContainer.Root container = new();

		await That(container.Resolve<ITerminal>().Describe()).IsEqualTo("Log(card)")
			.Because("a null entry selects the unkeyed registration");
		await That(container.Resolve<ITerminal>("backup").Describe()).IsEqualTo("Log(cash)")
			.Because("the selected key is decorated");
		await That(container.Resolve<ITerminal>("spare").Describe()).IsEqualTo("spare")
			.Because("an unselected key stays undecorated");
	}

	[Test]
	public async Task Keys_DecoratesFromKeyInjectionAndTheKeyedDictionary()
	{
		using SelectedKeysContainer.Root container = new();

		await That(container.Resolve<BackupConsumer>().Terminal.Describe()).IsEqualTo("Log(cash)")
			.Because("[FromKey] injection receives the decorated keyed registration");
		IReadOnlyDictionary<string, ITerminal> terminals = container.Resolve<IReadOnlyDictionary<string, ITerminal>>();
		await That(terminals["backup"].Describe()).IsEqualTo("Log(cash)")
			.Because("the keyed dictionary cannot bypass the decorator either");
		await That(terminals["spare"].Describe()).IsEqualTo("spare");
	}

	[Test]
	public async Task AllKeys_DecoratesEveryRegistration()
	{
		using AllKeysContainer.Root container = new();

		await That(container.Resolve<ITerminal>().Describe()).IsEqualTo("Retry(card)");
		await That(container.Resolve<ITerminal>("backup").Describe()).IsEqualTo("Retry(cash)");
		await That(container.Resolve<ITerminal>("spare").Describe()).IsEqualTo("Retry(spare)");
		await That(container.Resolve<ITerminal[]>().Select(t => t.Describe())).IsEqualTo(["Retry(card)",])
			.Because("the unkeyed collection holds the decorated unkeyed registration");
	}

	[Test]
	public async Task Order_AppliesWithinEachKeysChain()
	{
		using PerKeyOrderContainer.Root container = new();

		await That(container.Resolve<ITerminal>().Describe()).IsEqualTo("Log(card)")
			.Because("the unkeyed chain holds only the decorator that selects it");
		await That(container.Resolve<ITerminal>("backup").Describe()).IsEqualTo("Log(Retry(cash))")
			.Because("in the backup chain the lower Order sits innermost");
	}

	[Test]
	public async Task KeyedDecorator_InheritsTheKeyedRegistrationsLifetime()
	{
		using SelectedKeysContainer.Root container = new();

		await That(container.Resolve<ITerminal>("backup")).IsSameAs(container.Resolve<ITerminal>("backup"))
			.Because("the decorator of a singleton keyed registration is a singleton too");
	}

	[Test]
	public async Task OpenDecorator_WithKeys_DecoratesTheClosingsUnderTheSelectedKey()
	{
		using OpenKeyedDecoratorContainer.Root container = new();

		await That(container.Resolve<IHandler<Order>>().Describe()).IsEqualTo("handler")
			.Because("the unkeyed closing is not selected");
		await That(container.Resolve<IHandler<Order>>("audit").Describe()).IsEqualTo("Log(audit)");
	}

	[Test]
	public async Task KeyedComposite_FrontsItsKey_AndFansOutOverThatKeysMembers()
	{
		using KeyedCompositeContainer.Root container = new();

		ITerminal urgent = container.Resolve<ITerminal>("urgent");

		await That(urgent).Is<CompositeTerminal>()
			.Because("a keyed resolve returns the composite fronting that key");
		await That(urgent.Describe()).IsEqualTo("All(Log(pager))")
			.Because("the composite fans out over the (decorated) members under its key only");
		await That(container.Resolve<ITerminal>().Describe()).IsEqualTo("card")
			.Because("the unkeyed registration is not fronted by a keyed composite");
		await That(container.Resolve<UrgentConsumer>().Terminal).Is<CompositeTerminal>()
			.Because("[FromKey] injection receives the keyed composite");
		await That(container.Resolve<IReadOnlyDictionary<string, ITerminal>>()["urgent"].Describe()).IsEqualTo("Log(pager)")
			.Because("like an unkeyed composite in a collection, the keyed composite stays out of the keyed dictionary");
	}

	[Test]
	public async Task OneCompositeType_CanFrontTheUnkeyedRegistrationsAndAKey()
	{
		using UnkeyedAndKeyedCompositeContainer.Root container = new();

		await That(container.Resolve<ITerminal>().Describe()).IsEqualTo("All(card)")
			.Because("the unkeyed composite fans out over the unkeyed registrations");
		await That(container.Resolve<ITerminal>("urgent").Describe()).IsEqualTo("All(pager)")
			.Because("the keyed composite of the same type is a separate instance over its key");
	}

	[Test]
	public async Task OpenComposite_WithKey_FrontsTheClosingsUnderThatKey()
	{
		using OpenKeyedCompositeContainer.Root container = new();

		await That(container.Resolve<IHandler<Order>>("audit").Describe()).IsEqualTo("All(audit)");
		await That(container.Resolve<IHandler<Order>>().Describe()).IsEqualTo("handler");
	}

	public interface ITerminal
	{
		string Describe();
	}

	public sealed class CardTerminal : ITerminal
	{
		public string Describe() => "card";
	}

	public sealed class CashTerminal : ITerminal
	{
		public string Describe() => "cash";
	}

	public sealed class SpareTerminal : ITerminal
	{
		public string Describe() => "spare";
	}

	public sealed class PagerTerminal : ITerminal
	{
		public string Describe() => "pager";
	}

	public sealed class LoggingTerminal(ITerminal inner) : ITerminal
	{
		public string Describe() => $"Log({inner.Describe()})";
	}

	public sealed class RetryingTerminal(ITerminal inner) : ITerminal
	{
		public string Describe() => $"Retry({inner.Describe()})";
	}

	public sealed class CompositeTerminal(IEnumerable<ITerminal> members) : ITerminal
	{
		public string Describe() => $"All({string.Join(",", members.Select(m => m.Describe()))})";
	}

	public sealed class BackupConsumer([FromKey("backup")] ITerminal terminal)
	{
		public ITerminal Terminal => terminal;
	}

	public sealed class UrgentConsumer([FromKey("urgent")] ITerminal terminal)
	{
		public ITerminal Terminal => terminal;
	}

	public sealed class Order;

	public interface IHandler<T>
	{
		string Describe();
	}

	public sealed class Handler<T> : IHandler<T>
	{
		public string Describe() => "handler";
	}

	public sealed class AuditHandler<T> : IHandler<T>
	{
		public string Describe() => "audit";
	}

	public sealed class LoggingHandler<T>(IHandler<T> inner) : IHandler<T>
	{
		public string Describe() => $"Log({inner.Describe()})";
	}

	public sealed class CompositeHandler<T>(IEnumerable<IHandler<T>> members) : IHandler<T>
	{
		public string Describe() => $"All({string.Join(",", members.Select(m => m.Describe()))})";
	}

	[Container]
	[Singleton<CardTerminal, ITerminal>]
	[Singleton<CashTerminal, ITerminal>(Key = "backup")]
	[Singleton<SpareTerminal, ITerminal>(Key = "spare")]
	[Transient<BackupConsumer>]
	[Decorate<LoggingTerminal, ITerminal>(Keys = new object?[] { null, "backup" })]
	public static partial class SelectedKeysContainer;

	[Container]
	[Transient<CardTerminal, ITerminal>]
	[Transient<CashTerminal, ITerminal>(Key = "backup")]
	[Transient<SpareTerminal, ITerminal>(Key = "spare")]
	[Decorate<RetryingTerminal, ITerminal>(AllKeys = true)]
	public static partial class AllKeysContainer;

	[Container]
	[Transient<CardTerminal, ITerminal>]
	[Transient<CashTerminal, ITerminal>(Key = "backup")]
	[Decorate<LoggingTerminal, ITerminal>(AllKeys = true, Order = 1)]
	[Decorate<RetryingTerminal, ITerminal>(Keys = new object?[] { "backup" })]
	public static partial class PerKeyOrderContainer;

	[Container]
	[Transient<Handler<Order>, IHandler<Order>>]
	[Transient<AuditHandler<Order>, IHandler<Order>>(Key = "audit")]
	[Decorate(typeof(LoggingHandler<>), typeof(IHandler<>), Keys = new object?[] { "audit" })]
	public static partial class OpenKeyedDecoratorContainer;

	[Container]
	[Transient<CardTerminal, ITerminal>]
	[Transient<PagerTerminal, ITerminal>(Key = "urgent")]
	[Transient<UrgentConsumer>]
	[Decorate<LoggingTerminal, ITerminal>(Keys = new object?[] { "urgent" })]
	[Composite<CompositeTerminal, ITerminal>(Key = "urgent")]
	public static partial class KeyedCompositeContainer;

	[Container]
	[Transient<Handler<Order>, IHandler<Order>>]
	[Transient<AuditHandler<Order>, IHandler<Order>>(Key = "audit")]
	[Composite(typeof(CompositeHandler<>), typeof(IHandler<>), Key = "audit")]
	public static partial class OpenKeyedCompositeContainer;

	[Container]
	[Transient<CardTerminal, ITerminal>]
	[Transient<PagerTerminal, ITerminal>(Key = "urgent")]
	[Composite<CompositeTerminal, ITerminal>]
	[Composite<CompositeTerminal, ITerminal>(Key = "urgent")]
	public static partial class UnkeyedAndKeyedCompositeContainer;
}
