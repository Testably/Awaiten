using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of decorators: <c>[Decorate&lt;IService, D&gt;]</c> wraps the registered service
///     so consumers receive <c>D(inner)</c>. Multiple decorators chain in declaration order (last declared
///     is outermost), a decorated service injected anywhere receives the outermost decorator, and a
///     collection view of the service yields the decorated chain (the decorator is unbypassable). The
///     containers and services are nested types, so the enclosing class is <c>partial</c>.
/// </summary>
public partial class DecoratorTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task SingleDecorator_WrapsTheRegisteredImplementation()
	{
		using SingleContainer.Root container = new();

		IService service = container.Resolve<IService>();

		await That(service).Is<LoggingDecorator>();
		await That(service.Describe()).IsEqualTo("Logging(Real)");
	}

	[Fact]
	public async Task MultipleDecorators_ChainInDeclarationOrderOutermostLast()
	{
		using MultiContainer.Root container = new();

		IService service = container.Resolve<IService>();

		// [Decorate<IService, D1>] then [Decorate<IService, D2>] => D2(D1(Real)).
		await That(service.Describe()).IsEqualTo("D2(D1(Real))");
	}

	[Fact]
	public async Task Decorator_ResolvesItsOtherDependenciesFromTheGraph()
	{
		using ExtraDepsContainer.Root container = new();

		IService service = container.Resolve<IService>();

		await That(service.Describe()).IsEqualTo("Audited(Real, by clock)");
	}

	[Fact]
	public async Task DecoratedService_InjectedIntoAnotherConsumer_ReceivesTheOutermostDecorator()
	{
		using ConsumerContainer.Root container = new();

		Consumer consumer = container.Resolve<Consumer>();

		await That(consumer.Service).Is<LoggingDecorator>();
		await That(consumer.Service.Describe()).IsEqualTo("Logging(Real)");
	}

	[Fact]
	public async Task DecoratedService_AsCollection_YieldsOneDecoratedChainForASingleBaseImpl()
	{
		using SingleContainer.Root container = new();

		IService[] services = container.Resolve<IService[]>();

		// A single base impl yields exactly one element — the full decorated chain, never the bare Real.
		await That(services).HasCount(1);
		await That(services[0]).Is<LoggingDecorator>();
		await That(services[0].Describe()).IsEqualTo("Logging(Real)");
	}

	[Fact]
	public async Task DecoratedService_AsCollection_WrapsEachBaseImplForMultipleRegistrations()
	{
		using MultiImplContainer.Root container = new();

		string[] described = container.Resolve<IEnumerable<IService>>().Select(s => s.Describe()).ToArray();

		// A decorator decorates every registration (member by member): [D(Real1), D(Real2)].
		await That(described).HasCount(2);
		await That(described[0]).IsEqualTo("Logging(Real1)");
		await That(described[1]).IsEqualTo("Logging(Real2)");
		await That(container.Resolve<IEnumerable<IService>>().All(s => s is LoggingDecorator)).IsTrue();
	}

	[Fact]
	public async Task MultipleRegistrations_SingleDispatch_ReturnsTheDecoratedFirstWins()
	{
		using MultiImplContainer.Root container = new();

		IService service = container.Resolve<IService>();

		await That(service).Is<LoggingDecorator>();
		await That(service.Describe()).IsEqualTo("Logging(Real1)");
	}

	[Fact]
	public async Task Decorator_InheritsTheDecoratedRegistrationsSingletonLifetime()
	{
		using SingletonLifetimeContainer.Root container = new();

		IService first = container.Resolve<IService>();
		IService second = container.Resolve<IService>();

		await That(first).IsSameAs(second);
	}

	[Fact]
	public async Task Decorator_InheritsTheDecoratedRegistrationsTransientLifetime()
	{
		using TransientLifetimeContainer.Root container = new();

		IService first = container.Resolve<IService>();
		IService second = container.Resolve<IService>();

		await That(ReferenceEquals(first, second)).IsFalse();
	}

	[Fact]
	public async Task ExplicitOrder_PositionsTheDecoratorRatherThanDeclarationOrder()
	{
		using ExplicitOrderContainer.Root container = new();

		IService service = container.Resolve<IService>();

		// D2 is declared first but carries Order = 1, so it sits outside D1 (Order 0, the default): D2(D1(Real)).
		await That(service.Describe()).IsEqualTo("D2(D1(Real))");
	}

	[Fact]
	public async Task Decorator_WithInnerParameterTypedAsABaseOfTheService_ResolvesThroughTheChain()
	{
		using BaseTypedInnerContainer.Root container = new();

		IDerivedService service = container.Resolve<IDerivedService>();

		// The decorator's inner parameter is IBase, a base of the decorated IDerivedService. The chain link is
		// registered under IDerivedService, so the redirect must key to that, not to the parameter's own base type.
		await That(service).Is<BaseTypedDecorator>();
		await That(service.Describe()).IsEqualTo("Base(Real)");
	}

	[Fact]
	public async Task Decorator_InheritsTheDecoratedRegistrationsScopedLifetime()
	{
		using ScopedLifetimeContainer.Root container = new();
		using IAwaitenScope scope1 = container.CreateScope();
		using IAwaitenScope scope2 = container.CreateScope();

		IService a = scope1.Resolve<IService>();
		IService b = scope1.Resolve<IService>();
		IService c = scope2.Resolve<IService>();

		await That(a).Is<LoggingDecorator>();
		await That(a).IsSameAs(b).Because("a scoped decorator is one instance within a scope");
		await That(a).IsNotSameAs(c).Because("each scope builds its own decorated instance");
	}

	[Fact]
	public async Task Decorator_OfAnAsyncInitializableService_InitializesTheInnerThenWrapsItAsync()
	{
		using AsyncInnerContainer.Root container = new();

		// The inner is IAsyncInitializable, so the whole chain is async-tainted: reachable only through
		// ResolveAsync (the redirect keeps the taint flowing from the inner up through the decorator).
		await That(() => container.Resolve<IService>()).Throws<InvalidOperationException>()
			.Because("an async-tainted decorated service is not exposed to synchronous resolution");

		IService service = await container.ResolveAsync<IService>(Ct);

		await That(service).Is<LoggingDecorator>();
		await That(service.Describe()).IsEqualTo("Logging(Real(init))")
			.Because("the async-initializable inner is initialized before the decorator wraps it");
	}

	[Fact]
	public async Task Dispose_DisposesBothTheDecoratorAndItsInner_OutermostFirst()
	{
		DisposalLog log;
		using (DisposalOrderContainer.Root container = new())
		{
			log = container.Resolve<DisposalLog>();
			container.Resolve<IService>();
		}

		// Both the decorator and the implementation it wraps are container-owned and disposed; the decorator is
		// built after its inner, so it is disposed first (outermost-first) - see the DecorateAttribute remarks.
		await That(log.Order).HasCount(2);
		await That(log.Order[0]).IsEqualTo("Decorator");
		await That(log.Order[1]).IsEqualTo("Real");
	}

	public interface IService
	{
		string Describe();
	}

	public interface IBase
	{
		string Describe();
	}

	public interface IDerivedService : IBase;

	public sealed class DerivedReal : IDerivedService
	{
		public string Describe() => "Real";
	}

	// A decorator whose single inner parameter is typed as IBase, a base of the decorated IDerivedService.
	public sealed class BaseTypedDecorator(IBase inner) : IDerivedService
	{
		public string Describe() => $"Base({inner.Describe()})";
	}

	public sealed class ScopedReal : IService
	{
		public string Describe() => "Real";
	}

	// An async-initializable base implementation, to check the async taint flows up through the decorator chain.
	public sealed class AsyncReal : IService, IAsyncInitializable
	{
		private bool _initialized;

		public string Describe() => _initialized ? "Real(init)" : "Real";

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			_initialized = true;
			return Task.CompletedTask;
		}
	}

	// A shared sink recording disposal order; both the inner and the decorator take it as an extra dependency.
	public sealed class DisposalLog
	{
		public List<string> Order { get; } = new();
	}

	public sealed class DisposableReal(DisposalLog log) : IService, IDisposable
	{
		public string Describe() => "Real";

		public void Dispose() => log.Order.Add("Real");
	}

	public sealed class DisposableDecorator(IService inner, DisposalLog log) : IService, IDisposable
	{
		public string Describe() => $"Disposable({inner.Describe()})";

		public void Dispose() => log.Order.Add("Decorator");
	}

	public sealed class Real : IService
	{
		public string Describe() => "Real";
	}

	public sealed class Real1 : IService
	{
		public string Describe() => "Real1";
	}

	public sealed class Real2 : IService
	{
		public string Describe() => "Real2";
	}

	public sealed class LoggingDecorator(IService inner) : IService
	{
		public string Describe() => $"Logging({inner.Describe()})";
	}

	public sealed class D1(IService inner) : IService
	{
		public string Describe() => $"D1({inner.Describe()})";
	}

	public sealed class D2(IService inner) : IService
	{
		public string Describe() => $"D2({inner.Describe()})";
	}

	public sealed class Clock
	{
		public string Now() => "clock";
	}

	// A decorator with an extra (non-decorated) dependency resolved from the graph.
	public sealed class AuditingDecorator(IService inner, Clock clock) : IService
	{
		public string Describe() => $"Audited({inner.Describe()}, by {clock.Now()})";
	}

	public sealed class Consumer(IService service)
	{
		public IService Service => service;
	}

	[Container]
	[Transient<Real, IService>]
	[Decorate<IService, LoggingDecorator>]
	public static partial class SingleContainer;

	[Container]
	[Transient<Real, IService>]
	[Decorate<IService, D1>]
	[Decorate<IService, D2>]
	public static partial class MultiContainer;

	[Container]
	[Transient<Clock>]
	[Transient<Real, IService>]
	[Decorate<IService, AuditingDecorator>]
	public static partial class ExtraDepsContainer;

	[Container]
	[Transient<Real, IService>]
	[Transient<Consumer>]
	[Decorate<IService, LoggingDecorator>]
	public static partial class ConsumerContainer;

	[Container]
	[Transient<Real1, IService>]
	[Transient<Real2, IService>]
	[Decorate<IService, LoggingDecorator>]
	public static partial class MultiImplContainer;

	[Container]
	[Singleton<Real, IService>]
	[Decorate<IService, LoggingDecorator>]
	public static partial class SingletonLifetimeContainer;

	[Container]
	[Transient<Real, IService>]
	[Decorate<IService, LoggingDecorator>]
	public static partial class TransientLifetimeContainer;

	[Container]
	[Transient<Real, IService>]
	[Decorate<IService, D2>(Order = 1)]
	[Decorate<IService, D1>]
	public static partial class ExplicitOrderContainer;

	[Container]
	[Transient<DerivedReal, IDerivedService>]
	[Decorate<IDerivedService, BaseTypedDecorator>]
	public static partial class BaseTypedInnerContainer;

	[Container]
	[Scoped<ScopedReal, IService>]
	[Decorate<IService, LoggingDecorator>]
	public static partial class ScopedLifetimeContainer;

	[Container]
	[Singleton<AsyncReal, IService>]
	[Decorate<IService, LoggingDecorator>]
	public static partial class AsyncInnerContainer;

	[Container]
	[Singleton<DisposalLog>]
	[Singleton<DisposableReal, IService>]
	[Decorate<IService, DisposableDecorator>]
	public static partial class DisposalOrderContainer;
}
