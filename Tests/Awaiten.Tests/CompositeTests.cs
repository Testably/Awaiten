using System.Collections.Generic;
using System.Linq;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of composites: <c>[Composite&lt;CompositeNotifier, INotifier&gt;]</c> exposes the
///     composite as the single public <c>INotifier</c>, fanning out to every OTHER registration of the service.
///     A plain <c>INotifier</c> parameter (and <c>Resolve&lt;INotifier&gt;()</c>) gets the composite, the
///     composite's own collection parameter gets the bare channels (never itself), and a separate consumer
///     requesting <c>IEnumerable&lt;INotifier&gt;</c> also gets the bare channels. The containers and services
///     are nested types, so the enclosing class is <c>partial</c>.
/// </summary>
public partial class CompositeTests
{
	[Fact]
	public async Task Resolve_ReturnsTheComposite_NotABareChannel()
	{
		using NotifierContainer.Root container = new();

		INotifier notifier = container.Resolve<INotifier>();

		await That(notifier).Is<CompositeNotifier>();
	}

	[Fact]
	public async Task Composite_FansOutToEveryOtherRegistration_NotItself()
	{
		using NotifierContainer.Root container = new();

		CompositeNotifier composite = (CompositeNotifier)container.Resolve<INotifier>();

		// The composite receives the three channels — Email, Sms, Push — and NOT itself.
		await That(string.Join(",", composite.Channels.Select(c => c.Name))).IsEqualTo("email,sms,push");
		await That(composite.Channels.Any(c => c is CompositeNotifier)).IsFalse()
			.Because("the composite is excluded from its own collection parameter");
		await That(composite.Send("hi")).IsEqualTo("email:hi|sms:hi|push:hi");
	}

	[Fact]
	public async Task SeparateConsumer_RequestingTheCollection_GetsTheBareChannels_NotTheComposite()
	{
		using NotifierContainer.Root container = new();

		INotifier[] channels = container.Resolve<INotifier[]>();

		// The composite is excluded from collection membership: a separate IEnumerable<INotifier> consumer
		// sees the bare channels only.
		await That(string.Join(",", channels.Select(c => c.Name))).IsEqualTo("email,sms,push");
		await That(channels.Any(c => c is CompositeNotifier)).IsFalse();
	}

	[Fact]
	public async Task InjectedConsumer_ReceivesTheComposite()
	{
		using ConsumerContainer.Root container = new();

		OrderService order = container.Resolve<OrderService>();

		await That(order.Notifier).Is<CompositeNotifier>();
		await That(order.Notify("ok")).IsEqualTo("email:ok|sms:ok");
	}

	[Fact]
	public async Task CompositeOverZeroMembers_FansOutToAnEmptyCollection()
	{
		using EmptyContainer.Root container = new();

		CompositeNotifier composite = (CompositeNotifier)container.Resolve<INotifier>();

		await That(composite.Channels).HasCount(0);
		await That(composite.Send("x")).IsEqualTo(string.Empty);
	}

	[Fact]
	public async Task Composite_FrontsTheDecoratedMembers_WhenTheServiceIsAlsoDecorated()
	{
		using DecoratedMembersContainer.Root container = new();

		CompositeNotifier composite = (CompositeNotifier)container.Resolve<INotifier>();

		// The composite runs after decorator chains, so it fans out to the decorated members, not the bare ones.
		await That(composite.Send("hi")).IsEqualTo("log(email:hi)|log(sms:hi)")
			.Because("a composite fronts the decorated members");
	}

	[Fact]
	public async Task Composite_DefaultsToTransient_FreshInstancePerResolve()
	{
		using NotifierContainer.Root container = new();

		INotifier first = container.Resolve<INotifier>();
		INotifier second = container.Resolve<INotifier>();

		await That(first).IsNotSameAs(second);
	}

	[Fact]
	public async Task Composite_HonorsAnExplicitSingletonLifetime()
	{
		using SingletonCompositeContainer.Root container = new();

		INotifier first = container.Resolve<INotifier>();
		INotifier second = container.Resolve<INotifier>();

		await That(first).IsSameAs(second);
	}

	[Fact]
	public async Task MultipleComposites_OverDifferentServices_EachFanOutOverItsOwnMembers()
	{
		using MultiServiceContainer.Root container = new();

		INotifier notifier = container.Resolve<INotifier>();
		IValidator validator = container.Resolve<IValidator>();

		// Each service gets its own composite, fanning out only over its own registrations.
		await That(notifier).Is<CompositeNotifier>();
		await That(validator).Is<CompositeValidator>();
		await That(notifier.Send("hi")).IsEqualTo("email:hi|sms:hi");
		await That(string.Join(",", ((CompositeValidator)validator).Rules.Select(r => r.Name))).IsEqualTo("notnull,range");
	}

	public interface INotifier
	{
		string Name { get; }

		string Send(string message);
	}

	public sealed class EmailNotifier : INotifier
	{
		public string Name => "email";

		public string Send(string message) => $"email:{message}";
	}

	public sealed class SmsNotifier : INotifier
	{
		public string Name => "sms";

		public string Send(string message) => $"sms:{message}";
	}

	public sealed class PushNotifier : INotifier
	{
		public string Name => "push";

		public string Send(string message) => $"push:{message}";
	}

	// A decorator over a single channel, so a decorated-then-composed graph is observable through Send.
	public sealed class LoggingNotifier(INotifier inner) : INotifier
	{
		public string Name => inner.Name;

		public string Send(string message) => $"log({inner.Send(message)})";
	}

	// The composite fans the message out to every other channel, joined for assertion.
	public sealed class CompositeNotifier(IEnumerable<INotifier> channels) : INotifier
	{
		public IReadOnlyList<INotifier> Channels { get; } = channels.ToList();

		public string Name => "composite";

		public string Send(string message) => string.Join("|", Channels.Select(c => c.Send(message)));
	}

	public sealed class OrderService(INotifier notifier)
	{
		public INotifier Notifier => notifier;

		public string Notify(string message) => notifier.Send(message);
	}

	[Container]
	[Transient<EmailNotifier, INotifier>]
	[Transient<SmsNotifier, INotifier>]
	[Transient<PushNotifier, INotifier>]
	[Composite<CompositeNotifier, INotifier>]
	public static partial class NotifierContainer;

	[Container]
	[Transient<EmailNotifier, INotifier>]
	[Transient<SmsNotifier, INotifier>]
	[Transient<OrderService>]
	[Composite<CompositeNotifier, INotifier>]
	public static partial class ConsumerContainer;

	[Container]
	[Composite<CompositeNotifier, INotifier>]
	public static partial class EmptyContainer;

	[Container]
	[Transient<EmailNotifier, INotifier>]
	[Transient<SmsNotifier, INotifier>]
	[Decorate<LoggingNotifier, INotifier>]
	[Composite<CompositeNotifier, INotifier>]
	public static partial class DecoratedMembersContainer;

	[Container]
	[Transient<EmailNotifier, INotifier>]
	[Transient<SmsNotifier, INotifier>]
	[Composite<CompositeNotifier, INotifier>(Lifetime = AwaitenLifetime.Singleton)]
	public static partial class SingletonCompositeContainer;

	// A second, unrelated service with its own composite, so [Composite]'s AllowMultiple is exercised across
	// distinct services in a single container.
	public interface IValidator
	{
		string Name { get; }
	}

	public sealed class NotNullValidator : IValidator
	{
		public string Name => "notnull";
	}

	public sealed class RangeValidator : IValidator
	{
		public string Name => "range";
	}

	public sealed class CompositeValidator(IEnumerable<IValidator> rules) : IValidator
	{
		public IReadOnlyList<IValidator> Rules { get; } = rules.ToList();

		public string Name => "composite";
	}

	[Container]
	[Transient<EmailNotifier, INotifier>]
	[Transient<SmsNotifier, INotifier>]
	[Transient<NotNullValidator, IValidator>]
	[Transient<RangeValidator, IValidator>]
	[Composite<CompositeNotifier, INotifier>]
	[Composite<CompositeValidator, IValidator>]
	public static partial class MultiServiceContainer;
}
