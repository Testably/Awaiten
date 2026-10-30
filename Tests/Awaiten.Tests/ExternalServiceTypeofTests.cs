using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of the <c>typeof</c> form of <c>[ImportService]</c>: an unbound generic definition routes every
///     closing a dependency asks for to the <see cref="IExternalResolver" />, and a closed type behaves exactly like the
///     generic <c>[ImportService&lt;T&gt;]</c>.
/// </summary>
public sealed partial class ExternalServiceTypeofTests
{
	public interface ILog<T>
	{
		object? Key { get; }
	}

	public interface IPair<TFirst, TSecond>;

	public sealed class Log<T>(object? key) : ILog<T>, ISupportLog<T>
	{
		public object? Key { get; } = key;
	}

	public sealed class Pair<TFirst, TSecond> : IPair<TFirst, TSecond>;

	public sealed class OrderService(ILog<OrderService> log)
	{
		public ILog<OrderService> Log { get; } = log;
	}

	public sealed class PaymentService(ILog<PaymentService> log)
	{
		public ILog<PaymentService> Log { get; } = log;
	}

	public sealed class NestedConsumer(ILog<List<OrderService>> log, IPair<string, ILog<int>> pair)
	{
		public ILog<List<OrderService>> Log { get; } = log;
		public IPair<string, ILog<int>> Pair { get; } = pair;
	}

	public sealed class KeyedConsumer([FromKey("audit")] ILog<KeyedConsumer> log)
	{
		public ILog<KeyedConsumer> Log { get; } = log;
	}

	public sealed class PropertyConsumer
	{
		[Inject]
		public ILog<PropertyConsumer> Log { get; set; } = null!;
	}

	public sealed class FactoryProduct(ILog<FactoryProduct> log)
	{
		public ILog<FactoryProduct> Log { get; } = log;
	}

	public sealed class LazyConsumer(Lazy<ILog<LazyConsumer>> log)
	{
		public Lazy<ILog<LazyConsumer>> Log { get; } = log;
	}

	public interface IRepository<T>
	{
		ILog<Repository<T>> Log { get; }
	}

	public sealed class Repository<T>(ILog<Repository<T>> log) : IRepository<T>
	{
		public ILog<Repository<T>> Log { get; } = log;
	}

	public sealed class ClockConsumer(ExternalServiceTests.IClock clock, [FromKey("utc")] ExternalServiceTests.IClock utcClock)
	{
		public ExternalServiceTests.IClock Clock { get; } = clock;
		public ExternalServiceTests.IClock UtcClock { get; } = utcClock;
	}

	[Container]
	[ImportService(typeof(ILog<>))]
	[ImportService(typeof(IPair<,>))]
	[Transient<OrderService>]
	[Transient<PaymentService>]
	[Transient<NestedConsumer>]
	[Transient<KeyedConsumer>]
	[Transient<PropertyConsumer>]
	[Transient<FactoryProduct>(Factory = nameof(MakeProduct))]
	public static partial class OpenContainer
	{
		private static FactoryProduct MakeProduct(ILog<FactoryProduct> log) => new(log);
	}

	[Container]
	[ImportService(typeof(ILog<>))]
	[Transient<OrderService>]
	[Transient<LazyConsumer>]
	[Singleton<Log<LazyConsumer>, ILog<LazyConsumer>>(Factory = nameof(MakeLog))]
	public static partial class RelationshipContainer
	{
		private static Log<LazyConsumer> MakeLog() => new("graph");
	}

	[Container]
	[ImportService(typeof(ILog<>))]
	[Transient(typeof(Repository<>), typeof(IRepository<>))]
	[Transient<RepositoryConsumer>]
	public static partial class OpenRegistrationContainer;

	public sealed class RepositoryConsumer(IRepository<OrderService> orders, IRepository<PaymentService> payments)
	{
		public IRepository<OrderService> Orders { get; } = orders;
		public IRepository<PaymentService> Payments { get; } = payments;
	}

	[Container]
	[ImportService(typeof(ExternalServiceTests.IClock))]
	[Transient<ClockConsumer>]
	public static partial class ClosedTypeofContainer;

	[Container]
	[ImportService<ExternalServiceTests.IClock>]
	[Transient<ClockConsumer>]
	public static partial class ClosedGenericContainer;

	[Module]
	[ImportService(typeof(ILog<>))]
	[Transient<OrderService>]
	public static class LogModule;

	[Container]
	[Import(typeof(LogModule))]
	[Transient<PaymentService>]
	public static partial class ModuleContainer;

	[Container]
	[Import(typeof(CrossAssemblyExternalModule))]
	public static partial class CrossAssemblyModuleContainer;

	/// <summary>Serves every closing of <see cref="ILog{T}" />, <see cref="ISupportLog{T}" /> and <see cref="IPair{TFirst, TSecond}" />, and records each request.</summary>
	private sealed class GenericResolver : IExternalResolver
	{
		public List<(Type ServiceType, object? Key)> Requests { get; } = [];

		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
		{
			Requests.Add((serviceType, key));
			Type definition = serviceType.GetGenericTypeDefinition();
			Type[] arguments = serviceType.GetGenericArguments();
			instance = definition == typeof(IPair<,>)
				? Activator.CreateInstance(typeof(Pair<,>).MakeGenericType(arguments))!
				: Activator.CreateInstance(typeof(Log<>).MakeGenericType(arguments), key)!;
			return true;
		}
	}

	private sealed class ClockResolver : IExternalResolver
	{
		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
		{
			instance = serviceType == typeof(ExternalServiceTests.IClock)
				? key is null ? new ExternalServiceTests.FixedClock() : new ExternalServiceTests.UtcClock()
				: null;
			return instance is not null;
		}
	}

	[Test]
	public async Task OpenImport_RoutesEveryConsumedClosingToTheExternalResolver()
	{
		GenericResolver resolver = new();
		using OpenContainer.Root container = new(resolver);

		OrderService order = container.Resolve<OrderService>();
		PaymentService payment = container.Resolve<PaymentService>();

		await That(order.Log).Is<Log<OrderService>>()
			.Because("ILog<OrderService> is a closing of the imported ILog<>, so it is drawn from the external resolver");
		await That(payment.Log).Is<Log<PaymentService>>()
			.Because("one open import covers every closing, not just the first one");
		await That(resolver.Requests).IsEqualTo(new (Type, object?)[] { (typeof(ILog<OrderService>), null), (typeof(ILog<PaymentService>), null), })
			.Because("the resolver is asked for each concrete closing, unkeyed");
	}

	[Test]
	public async Task OpenImport_RoutesNestedClosingsAndDefinitionsWithSeveralTypeParameters()
	{
		using OpenContainer.Root container = new(new GenericResolver());

		NestedConsumer consumer = container.Resolve<NestedConsumer>();

		await That(consumer.Log).Is<Log<List<OrderService>>>()
			.Because("a closing whose type argument is itself generic still matches the imported definition");
		await That(consumer.Pair).Is<Pair<string, ILog<int>>>()
			.Because("an open import with two type parameters matches its closings");
	}

	[Test]
	public async Task OpenImport_WithFromKey_ForwardsTheKeyToTheExternalResolver()
	{
		GenericResolver resolver = new();
		using OpenContainer.Root container = new(resolver);

		KeyedConsumer consumer = container.Resolve<KeyedConsumer>();

		await That(consumer.Log.Key).IsEqualTo("audit")
			.Because("a [FromKey] on a routed closing is forwarded to the resolver, like the generic form");
		await That(resolver.Requests).IsEqualTo(new (Type, object?)[] { (typeof(ILog<KeyedConsumer>), "audit"), });
	}

	[Test]
	public async Task OpenImport_InjectedPropertyAndFactoryParameter_ResolveThroughTheExternalResolver()
	{
		using OpenContainer.Root container = new(new GenericResolver());

		await That(container.Resolve<PropertyConsumer>().Log).Is<Log<PropertyConsumer>>()
			.Because("an [Inject] property of a closing is routed too");
		await That(container.Resolve<FactoryProduct>().Log).Is<Log<FactoryProduct>>()
			.Because("a factory parameter of a closing is routed too");
	}

	[Test]
	public async Task OpenImport_AdvertisesExactlyTheConsumedClosings()
	{
		using OpenContainer.Root container = new();

		await That(((IAwaitenRoot)container).ExternalDependencies)
			.IsEqualTo([
				new AwaitenExternalDependency(typeof(ILog<OrderService>)),
				new AwaitenExternalDependency(typeof(ILog<PaymentService>)),
				new AwaitenExternalDependency(typeof(ILog<List<OrderService>>)),
				new AwaitenExternalDependency(typeof(IPair<string, ILog<int>>)),
				new AwaitenExternalDependency(typeof(ILog<KeyedConsumer>), "audit"),
				new AwaitenExternalDependency(typeof(ILog<PropertyConsumer>)),
				new AwaitenExternalDependency(typeof(ILog<FactoryProduct>)),
			]).InAnyOrder()
			.Because("each concrete closing the graph consumes is advertised so a host can verify it, and nothing else is");
	}

	[Test]
	public async Task OpenImport_RelationshipOverAClosing_ResolvesFromTheGraph()
	{
		GenericResolver resolver = new();
		using RelationshipContainer.Root container = new(resolver);

		LazyConsumer consumer = container.Resolve<LazyConsumer>();

		await That(consumer.Log.Value.Key).IsEqualTo("graph")
			.Because("a Lazy<T> over a closing is not routed, so it resolves the closing the container registers");
		await That(resolver.Requests).IsEmpty();
		await That(((IAwaitenRoot)container).ExternalDependencies)
			.IsEqualTo([new AwaitenExternalDependency(typeof(ILog<OrderService>)),])
			.Because("only the direct closing is external; the relationship's closing is Awaiten-owned");
	}

	[Test]
	public async Task OpenImport_RoutesTheClosingsThatClosingsOfAnOpenRegistrationConsume()
	{
		using OpenRegistrationContainer.Root container = new(new GenericResolver());

		RepositoryConsumer consumer = container.Resolve<RepositoryConsumer>();

		await That(consumer.Orders.Log).Is<Log<Repository<OrderService>>>()
			.Because("each closing of the open registration asks for its own closing of ILog<>, which is routed");
		await That(consumer.Payments.Log).Is<Log<Repository<PaymentService>>>();
		await That(((IAwaitenRoot)container).ExternalDependencies)
			.IsEqualTo([
				new AwaitenExternalDependency(typeof(ILog<Repository<OrderService>>)),
				new AwaitenExternalDependency(typeof(ILog<Repository<PaymentService>>)),
			]).InAnyOrder();
	}

	[Test]
	public async Task ClosedTypeofImport_BehavesLikeTheGenericForm()
	{
		using ClosedTypeofContainer.Root typeofContainer = new(new ClockResolver());
		using ClosedGenericContainer.Root genericContainer = new(new ClockResolver());

		ClockConsumer fromTypeof = typeofContainer.Resolve<ClockConsumer>();
		ClockConsumer fromGeneric = genericContainer.Resolve<ClockConsumer>();

		await That(fromTypeof.Clock).Is<ExternalServiceTests.FixedClock>();
		await That(fromTypeof.UtcClock).Is<ExternalServiceTests.UtcClock>()
			.Because("the closed typeof form forwards a [FromKey] key like the generic form");
		await That(fromGeneric.Clock).Is<ExternalServiceTests.FixedClock>();
		await That(fromGeneric.UtcClock).Is<ExternalServiceTests.UtcClock>();
		await That(((IAwaitenRoot)typeofContainer).ExternalDependencies)
			.IsEqualTo(((IAwaitenRoot)genericContainer).ExternalDependencies).InAnyOrder()
			.Because("both forms advertise the same external dependencies");
	}

	[Test]
	public async Task OpenImportOnAModule_RoutesClosingsOfTheImportingContainer()
	{
		using ModuleContainer.Root container = new(new GenericResolver());

		await That(container.Resolve<OrderService>().Log).Is<Log<OrderService>>()
			.Because("the module's own registration consumes a closing of the module's open import");
		await That(container.Resolve<PaymentService>().Log).Is<Log<PaymentService>>()
			.Because("an import declared on a module is honored for the whole importing container, like the generic form");
	}

	[Test]
	public async Task OpenImportOnACrossAssemblyModule_RoutesClosings()
	{
		using CrossAssemblyModuleContainer.Root container = new(new GenericResolver());

		SupportAuditor auditor = container.Resolve<SupportAuditor>();

		await That(auditor.Log).Is<Log<SupportAuditor>>()
			.Because("an open import read back from a referenced assembly's metadata routes its closings too");
		await That(((IAwaitenRoot)container).ExternalDependencies)
			.IsEqualTo([new AwaitenExternalDependency(typeof(ISupportLog<SupportAuditor>)),]);
	}
}
