using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of open-generic collections: a collection of a closed generic service -
///     <c>IEnumerable&lt;IHandler&lt;OrderPlaced&gt;&gt;</c> - resolves to every open-generic registration
///     expanded at that closed type argument, in declaration order, each respecting its own lifetime. A
///     different closed argument expands its own closed set. Expansion is driven at compile time by the
///     closed services an application actually needs, so each container declares a concrete <c>App</c> root
///     that depends on them. The containers and services are nested types, so the enclosing class is
///     <c>partial</c>.
/// </summary>
public partial class OpenGenericCollectionTests
{
	[Fact]
	public async Task Enumerable_YieldsEveryOpenRegistrationClosedAtTheArgument()
	{
		using HandlerContainer.Root container = new();

		Dispatcher<OrderPlaced> dispatcher = container.Resolve<Dispatcher<OrderPlaced>>();

		// AuditHandler<> and ProjectionHandler<> are both open registrations of IHandler<>, so the
		// collection of IHandler<OrderPlaced> holds both, in declaration order.
		await That(dispatcher.Handled).HasCount(2);
		await That(dispatcher.Handled[0]).IsEqualTo("Audit<OrderPlaced>");
		await That(dispatcher.Handled[1]).IsEqualTo("Projection<OrderPlaced>");
	}

	[Fact]
	public async Task Enumerable_IsPubliclyResolvable()
	{
		using HandlerContainer.Root container = new();

		string[] descriptions = container.Resolve<IEnumerable<IHandler<OrderPlaced>>>()
			.Select(h => h.Describe())
			.ToArray();

		await That(descriptions).HasCount(2);
		await That(descriptions[0]).IsEqualTo("Audit<OrderPlaced>");
		await That(descriptions[1]).IsEqualTo("Projection<OrderPlaced>");
	}

	[Fact]
	public async Task DifferentClosedArgument_YieldsItsOwnClosedSet()
	{
		using HandlerContainer.Root container = new();

		IHandler<OrderShipped>[] shipped = container.Resolve<IHandler<OrderShipped>[]>();

		await That(shipped).HasCount(2);
		await That(shipped[0].Describe()).IsEqualTo("Audit<OrderShipped>");
		await That(shipped[1].Describe()).IsEqualTo("Projection<OrderShipped>");
	}

	[Fact]
	public async Task ReadOnlyListShape_MaterializesEveryMember()
	{
		using HandlerContainer.Root container = new();

		ReadOnlyDispatcher<OrderPlaced> dispatcher = container.Resolve<ReadOnlyDispatcher<OrderPlaced>>();

		await That(dispatcher.Handlers).HasCount(2);
		await That(dispatcher.Handlers[0].Describe()).IsEqualTo("Audit<OrderPlaced>");
		await That(dispatcher.Handlers[1].Describe()).IsEqualTo("Projection<OrderPlaced>");
	}

	[Fact]
	public async Task ExplicitClosedMember_CoexistsWithOpenExpandedMembers()
	{
		using MixedContainer.Root container = new();

		// IHandler<OrderPlaced> has one explicit closed registration (LegacyHandler) and one open
		// registration (AuditHandler<>); the collection holds both.
		string[] descriptions = container.Resolve<IEnumerable<IHandler<OrderPlaced>>>()
			.Select(h => h.Describe())
			.ToArray();

		await That(descriptions).HasCount(2);
		await That(descriptions).Contains("Legacy<OrderPlaced>");
		await That(descriptions).Contains("Audit<OrderPlaced>");
	}

	[Fact]
	public async Task NoOpenRegistrationForElement_ResolvesToEmptyCollection()
	{
		using HandlerContainer.Root container = new();

		// INotifier<> has no registration (open or closed), so its collection is empty rather than an
		// AWT101 missing-dependency error.
		IReadOnlyList<INotifier<OrderPlaced>> notifiers = container.Resolve<NotifierHost>().Notifiers;

		await That(notifiers).HasCount(0);
	}

	[Fact]
	public async Task SingletonMembers_AreSharedAcrossResolutions()
	{
		using HandlerContainer.Root container = new();

		IHandler<OrderPlaced>[] first = container.Resolve<IHandler<OrderPlaced>[]>();
		IHandler<OrderPlaced>[] second = container.Resolve<IHandler<OrderPlaced>[]>();

		// AuditHandler<> is a singleton open registration, so its closed instance is shared.
		await That(first[0]).IsSameAs(second[0]);
		// ProjectionHandler<> is transient, so it is fresh each time.
		await That(ReferenceEquals(first[1], second[1])).IsFalse();
	}

	[Fact]
	public async Task AwaitedSingleService_SeedsOpenGenericExpansion()
	{
		using HandlerContainer.Root container = new();

		// A Task<IHandler<OrderPlaced>> parameter seeds expansion through its inner closed service (the
		// Task/ValueTask unwrap in RequiredServiceTypes); single dispatch picks the first open registration.
		AwaitedHandler<OrderPlaced> awaited = container.Resolve<AwaitedHandler<OrderPlaced>>();
		IHandler<OrderPlaced> handler = await awaited.Handler;

		await That(handler.Describe()).IsEqualTo("Audit<OrderPlaced>");
	}

	public sealed class OrderPlaced;

	public sealed class OrderShipped;

	public interface IHandler<T>
	{
		string Describe();
	}

	public sealed class AuditHandler<T> : IHandler<T>
	{
		public string Describe() => $"Audit<{typeof(T).Name}>";
	}

	public sealed class ProjectionHandler<T> : IHandler<T>
	{
		public string Describe() => $"Projection<{typeof(T).Name}>";
	}

	public sealed class LegacyHandler<T> : IHandler<T>
	{
		public string Describe() => $"Legacy<{typeof(T).Name}>";
	}

	// An open generic with no registration, to verify an empty open-generic collection.
	public interface INotifier<T>;

	public sealed class Dispatcher<T>
	{
		public Dispatcher(IEnumerable<IHandler<T>> handlers) => Handled = handlers.Select(h => h.Describe()).ToArray();

		public IReadOnlyList<string> Handled { get; }
	}

	public sealed class ReadOnlyDispatcher<T>
	{
		public ReadOnlyDispatcher(IReadOnlyList<IHandler<T>> handlers) => Handlers = handlers;

		public IReadOnlyList<IHandler<T>> Handlers { get; }
	}

	public sealed class AwaitedHandler<T>
	{
		public AwaitedHandler(Task<IHandler<T>> handler) => Handler = handler;

		public Task<IHandler<T>> Handler { get; }
	}

	public sealed class NotifierHost
	{
		public NotifierHost(IReadOnlyList<INotifier<OrderPlaced>> notifiers) => Notifiers = notifiers;

		public IReadOnlyList<INotifier<OrderPlaced>> Notifiers { get; }
	}

	// A concrete root seeds open generic expansion for the closed type arguments actually used.
	public sealed class App
	{
		public App(
			Dispatcher<OrderPlaced> placed,
			ReadOnlyDispatcher<OrderPlaced> readOnly,
			AwaitedHandler<OrderPlaced> awaited,
			IHandler<OrderShipped>[] shipped,
			NotifierHost notifiers)
		{
		}
	}

	[Container]
	[Singleton(typeof(AuditHandler<>), typeof(IHandler<>))]
	[Transient(typeof(ProjectionHandler<>), typeof(IHandler<>))]
	[Transient(typeof(Dispatcher<>))]
	[Transient(typeof(ReadOnlyDispatcher<>))]
	[Transient(typeof(AwaitedHandler<>))]
	[Transient<NotifierHost>]
	[Transient<App>]
	public static partial class HandlerContainer;

	[Container]
	[Transient<LegacyHandler<OrderPlaced>, IHandler<OrderPlaced>>]
	[Transient(typeof(AuditHandler<>), typeof(IHandler<>))]
	[Transient(typeof(Dispatcher<>))]
	[Transient<MixedApp>]
	public static partial class MixedContainer;

	public sealed class MixedApp
	{
		public MixedApp(IEnumerable<IHandler<OrderPlaced>> handlers)
		{
		}
	}
}
