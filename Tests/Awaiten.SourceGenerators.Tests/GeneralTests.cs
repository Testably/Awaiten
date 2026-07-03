namespace Awaiten.SourceGenerators.Tests;

public class GeneralTests
{
	[Fact]
	public async Task Container_DispatchesThroughAStaticTableSharedWithTheScope()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class A { }
		                                       public sealed class B { }

		                                       [Container]
		                                       [Singleton<A>]
		                                       [Singleton<B>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source)
			.Contains("private static readonly __Bucket[] __buckets")
			.Because("resolution is dispatched through a static type-to-resolver bucket table");
		await That(source).Contains("public sealed class Root : Scope")
			.Because("the root scope is the usable container instance, created with new MyContainer.Root()");
		await That(source).Contains("(object?)__b.Key == (object?)serviceType")
			.Because("the scope dispatches by probing the static bucket table that lives on it");
		await That(source).Contains("if (__b.Key is null)")
			.Because("the windows fill front-first, so a miss stops at the first empty slot");
		await That(source).Contains("private static readonly global::Awaiten.AwaitenRegistration[] __registrations")
			.Because("the registration metadata is a static array, not rebuilt per Root construction");
		await That(source).DoesNotContain("if (serviceType == typeof(")
			.Because("the linear if-chain is no longer emitted");
	}

	[Fact]
	public async Task EmptyContainer_EmitsAPartialImplementationWithoutDiagnostics()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       [Container]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources).HasCount(1);
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("static partial class MyContainer");
		await That(source).Contains("public object Resolve(global::System.Type serviceType)");
		await That(source).Contains("public Scope CreateScope() => new Scope(__root);");
		await That(source).Contains("public class Scope : global::Awaiten.IAwaitenScope");
	}

	[Fact]
	public async Task ExplicitlyRegisteredRelationshipType_WinsTheDispatchSlotWithoutADuplicateKey()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Leaf { }

		                                       [Container]
		                                       [Transient<Leaf>]
		                                       [Singleton<System.Lazy<Leaf>>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		int keyCount = source.Split(new[]
		{
			"new __Bucket(typeof(global::System.Lazy<global::MyCode.Leaf>)",
		}, StringSplitOptions.None).Length - 1;
		await That(keyCount).IsEqualTo(1)
			.Because("the explicit registration and the synthetic relationship must not produce a duplicate dispatch key");
		await That(source).DoesNotContain("new global::System.Lazy<global::MyCode.Leaf>(() => ResolveLeaf())")
			.Because("the synthetic Lazy<Leaf> factory is dropped in favour of the explicit registration's resolver");
	}

	[Fact]
	public async Task FuncDependency_EmitsADeferredFactoryBoundToTheOwner()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Leaf { }
		                                       public sealed class Consumer { public Consumer(Func<Leaf> leaf) { } }

		                                       [Container]
		                                       [Transient<Leaf>]
		                                       [Transient<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.Consumer(new global::System.Func<global::MyCode.Leaf>(() => ResolveLeaf()))")
			.Because("the Func parameter is supplied as a factory bound to the owner's resolver");
		await That(source).Contains("new __Bucket(typeof(global::System.Func<global::MyCode.Leaf>),")
			.Because("Func<T> is also resolvable directly through the dispatch table");
		await That(source).Contains("() => new global::System.Func<global::MyCode.Leaf>(() => ResolveLeaf());")
			.Because("its dispatch slot builds a fresh factory over the target's resolver");
	}

	[Fact]
	public async Task Graph_EmitsThreadSafeSingletonCachingAndTransientConstruction()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Leaf { }
		                                       public interface IMiddle { }
		                                       public sealed class Middle : IMiddle { public Middle(Leaf leaf) { } }
		                                       public sealed class Top { public Top(IMiddle middle, Leaf leaf) { } }

		                                       [Container]
		                                       [Singleton<Leaf>]
		                                       [Singleton<Middle, IMiddle>]
		                                       [Transient<Top>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("private volatile global::MyCode.Leaf? _leaf;")
			.Because("reference-type singletons are cached in a volatile backing field");
		await That(source).Contains("private volatile global::MyCode.Middle? _middle;")
			.Because("reference-type singletons are cached in a volatile backing field");
		await That(source).Contains("lock (__gate)")
			.Because("singletons are created once under a lock on a private gate, not the publicly reachable scope");
		await That(source).Contains("_middle = new global::MyCode.Middle(__root.ResolveLeaf());")
			.Because("singletons are memoized into their backing field and read straight off the root scope");
		await That(source).Contains("return new global::MyCode.Top(__root.ResolveMiddle(), __root.ResolveLeaf());")
			.Because("transients are constructed on each request, not cached");

		await That(source).Contains("new __Bucket(typeof(global::MyCode.IMiddle),")
			.Because("each service type is a key in the static dispatch table");
		await That(source).Contains("static __s => __s.ResolveMiddle()")
			.Because("its dispatch slot resolves the registered service");
	}

	[Fact]
	public async Task InternalConstructor_InTheSameAssembly_IsUsable()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Foo { internal Foo() { } }

		                                       [Container]
		                                       [Singleton<Foo>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("_foo = new global::MyCode.Foo();");
	}

	[Fact]
	public async Task LazyDependency_EmitsADeferredLazyBoundToTheOwner()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Leaf { }
		                                       public sealed class Consumer { public Consumer(Lazy<Leaf> leaf) { } }

		                                       [Container]
		                                       [Singleton<Leaf>]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.Consumer(new global::System.Lazy<global::MyCode.Leaf>(() => __root.ResolveLeaf()))")
			.Because("the Lazy parameter is supplied as a lazy bound to the owner's resolver");
		await That(source).Contains("new __Bucket(typeof(global::System.Lazy<global::MyCode.Leaf>),")
			.Because("Lazy<T> is also resolvable directly through the dispatch table");
		await That(source).Contains("() => new global::System.Lazy<global::MyCode.Leaf>(() => ResolveLeaf());")
			.Because("its dispatch slot builds a fresh lazy over the target's resolver");
	}

	[Fact]
	public async Task MultiServiceRegistration_CoalescesIntoASingleSharedInstance()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IReader { }
		                                       public interface IWriter { }
		                                       public sealed class Store : IReader, IWriter { }

		                                       [Container]
		                                       [Singleton<Store, IReader>]
		                                       [Singleton<Store, IWriter>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("private volatile global::MyCode.Store? _store;")
			.Because("the implementation is coalesced into one backing field");
		await That(source).Contains("new __Bucket(typeof(global::MyCode.IReader),")
			.Because("each service type is a key in the static dispatch table");
		await That(source).Contains("new __Bucket(typeof(global::MyCode.IWriter),")
			.Because("each service type is a key in the static dispatch table");
		await That(source).Contains("static __s => __s.ResolveStore()")
			.Because("both service types dispatch to the one shared instance");
	}

	[Fact]
	public async Task NestedContainer_ReopensTheEnclosingTypesAsPartial()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public partial class Outer
		                                       {
		                                       	public sealed class Service { }

		                                       	[Container]
		                                       	[Singleton<Service>]
		                                       	public static partial class Inner
		                                       	{
		                                       	}
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources).HasCount(1);
		string source = result.Sources["Awaiten.MyCode.Outer+Inner.g.cs"];
		await That(source).Contains("partial class Outer");
		await That(source).Contains("static partial class Inner");
		await That(source).Contains("_service = new global::MyCode.Outer.Service();");
	}

	[Fact]
	public async Task SameSimpleName_InDifferentNamespaces_DoNotCollide()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace A { public sealed class S1 { } [Container][Singleton<S1>] public static partial class MyContainer { } }
		                                       namespace B { public sealed class S2 { } [Container][Singleton<S2>] public static partial class MyContainer { } }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources).HasCount(2);
		await That(result.Sources).ContainsKey("Awaiten.A.MyContainer.g.cs");
		await That(result.Sources).ContainsKey("Awaiten.B.MyContainer.g.cs");
	}

	[Fact]
	public async Task ScopedRegistration_EmitsPerScopeStorageOnTheContainerAndTheScope()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Service { }

		                                       [Container]
		                                       [Scoped<Service>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("private volatile global::MyCode.Service? _service;")
			.Because("a scoped registration is cached per scope");
		await That(source).Contains("Scoped: one instance per scope")
			.Because("scoped instances live on the scope, not the container");
		await That(source).Contains("public class Scope : global::Awaiten.IAwaitenScope")
			.Because("the scope is the single resolver and is publicly accessible");
	}

	[Fact]
	public async Task FactoryRegistration_CallsTheContainerMethodInsteadOfAConstructor()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class SystemClock : IClock { }

		                                       [Container]
		                                       [Transient<IClock>(Factory = nameof(MakeClock))]
		                                       public static partial class MyContainer
		                                       {
		                                       	private static IClock MakeClock() => new SystemClock();
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("global::MyCode.IClock created = MakeClock();")
			.Because("the scope calls the static factory method by simple name instead of constructing the type (the realized instance is captured so its disposal can be tracked at runtime)");
		await That(source).DoesNotContain("new global::MyCode.SystemClock(")
			.Because("a factory registration is produced by its method, never constructed directly");
	}

	[Fact]
	public async Task FactoryReturningInterface_TracksDisposalByARuntimeCheckOnTheRealizedInstance()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class DisposableClock : IClock, IDisposable { public void Dispose() { } }

		                                       [Container]
		                                       [Transient<IClock>(Factory = nameof(MakeClock))]
		                                       public static partial class MyContainer
		                                       {
		                                       	private static IClock MakeClock() => new DisposableClock();
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("if (created is global::System.IDisposable or global::System.IAsyncDisposable)")
			.Because("a factory declared to return a non-disposable interface may build a concrete IDisposable (or IAsyncDisposable), so disposal is tracked by a runtime check on the realized instance");
		await That(source).Contains("(__disposables ??= new global::System.Collections.Generic.List<object>()).Add(created);")
			.Because("a genuinely-disposable factory output is still registered for teardown");
		await That(source).DoesNotContain("((global::System.IDisposable)created).Dispose();")
			.Because("the realized instance is reached through the runtime pattern, never an unchecked cast");
	}

	[Fact]
	public async Task FactoryReturningANonDisposableSealedType_EmitsNoDisposalTracking()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class PlainClock : IClock { }

		                                       [Container]
		                                       [Transient<IClock>(Factory = nameof(MakeClock))]
		                                       public static partial class MyContainer
		                                       {
		                                       	private static PlainClock MakeClock() => new PlainClock();
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("return MakeClock();")
			.Because("a factory whose declared return type is a sealed non-disposable class cannot hide a disposable, so its resolver returns the call directly with no tracking");
		await That(source).DoesNotContain("created is global::System.IDisposable")
			.Because("a provably non-disposable factory output gets no runtime disposal check");
		await That(source).DoesNotContain("(__disposables ??= new")
			.Because("a provably non-disposable factory output is never added to the disposal list");
	}

	[Fact]
	public async Task FactoryRegistration_ResolvesTheMethodParametersFromTheGraph()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Settings { }
		                                       public sealed class Service { public Service(Settings settings) { } }

		                                       [Container]
		                                       [Singleton<Settings>]
		                                       [Transient<Service>(Factory = nameof(MakeService))]
		                                       public static partial class MyContainer
		                                       {
		                                       	private static Service MakeService(Settings settings) => new Service(settings);
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("MakeService(__root.ResolveSettings())")
			.Because("the factory method's parameters are resolved from the graph");
		await That(source).DoesNotContain("__container.MakeService")
			.Because("a static factory is in scope of the nested type directly and needs no container receiver");
	}

	[Fact]
	public async Task InstanceRegistration_ReturnsTheContainerMemberWithoutConstructingOrDisposing()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class FixedClock : IClock { }

		                                       [Container]
		                                       [Singleton<IClock>(Instance = nameof(Clock))]
		                                       public static partial class MyContainer
		                                       {
		                                       	private static readonly IClock Clock = new FixedClock();
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("return Clock;")
			.Because("the container hands back its own pre-built static member rather than constructing the type");
		await That(source).Contains("return __root.ResolveIClock();")
			.Because("the nested scope delegates to the root scope like any other singleton");
		await That(source).DoesNotContain("new global::MyCode.FixedClock")
			.Because("an Instance registration is never constructed by the container");
		await That(source).DoesNotContain("__disposables.Add")
			.Because("the container does not own a pre-built Instance, so it never registers it for disposal");
		await That(source).Contains("externallyOwned: true")
			.Because("the registration metadata advertises the container's non-ownership, so a bridging host does not assume disposal either");
	}

	[Fact]
	public async Task RuntimeArguments_EmitAParameterizedResolverReachedOnlyThroughItsFuncFactory()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Engine { }
		                                       public sealed class Robot
		                                       {
		                                       	public Robot(Engine engine, [Arg] string name) { }
		                                       }
		                                       public sealed class Plant { public Plant(Func<string, Robot> robots) { } }

		                                       [Container]
		                                       [Singleton<Engine>]
		                                       [Transient<Robot>]
		                                       [Singleton<Plant>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The resolver takes the runtime argument and is internal so a root-owned singleton's Func - and a
		// throwaway Owned<T> scope - can bind it; its graph dependency is still read straight off the root scope.
		await That(source).Contains("internal global::MyCode.Robot ResolveRobot(string a0)");
		await That(source).Contains("new global::MyCode.Robot(__root.ResolveEngine(), a0)");
		// The consumer receives a Func that forwards the runtime argument to that resolver.
		await That(source).Contains("new global::System.Func<string, global::MyCode.Robot>((a0) => ResolveRobot(a0))");
		// A parameterized service is reachable only through its Func factory, never directly.
		await That(source).Contains("typeof(global::System.Func<string, global::MyCode.Robot>)");
		await That(source).DoesNotContain("typeof(global::MyCode.Robot)")
			.Because("the bare parameterized service type is not dispatchable");
		await That(source).DoesNotContain("global::Awaiten.IAwaitenResolver<global::MyCode.Robot>")
			.Because("a parameterized service has no typed resolution fast path");
	}

	[Fact]
	public async Task OpenGeneric_ExpandsToTheClosedImplementationRequiredByAConsumer()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T> { }
		                                       public sealed class Root { public Root(IRepository<Order> orders) { } }

		                                       [Container]
		                                       [Transient(typeof(Repository<>), typeof(IRepository<>))]
		                                       [Transient<Root>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The open registration is expanded into the closed Repository<Order>, dispatched under the closed
		// service IRepository<Order> and constructed for the Root consumer.
		await That(source).Contains("new global::MyCode.Repository<global::MyCode.Order>()");
		await That(source).Contains("typeof(global::MyCode.IRepository<global::MyCode.Order>)");
	}

	[Fact]
	public async Task OpenGeneric_ExpandsTransitivelyThroughAClosedGenericDependency()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IValidator<T> { }
		                                       public sealed class Validator<T> : IValidator<T> { }
		                                       public sealed class Handler<T> { public Handler(IValidator<T> validator) { } }
		                                       public sealed class Root { public Root(Handler<Order> handler) { } }

		                                       [Container]
		                                       [Transient(typeof(Validator<>), typeof(IValidator<>))]
		                                       [Transient(typeof(Handler<>))]
		                                       [Transient<Root>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Handler<Order> is expanded from the self-registration; its own IValidator<Order> dependency drives a
		// further expansion of Validator<Order> - the worklist iterates to a fixpoint.
		await That(source).Contains("new global::MyCode.Validator<global::MyCode.Order>()");
		await That(source).Contains("new global::MyCode.Handler<global::MyCode.Order>(");
	}

	[Fact]
	public async Task OpenGenericCollection_ExpandsEveryOpenRegistrationIntoTheClosedCollection()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public sealed class OrderPlaced { }
		                                       public interface IHandler<T> { }
		                                       public sealed class AuditHandler<T> : IHandler<T> { }
		                                       public sealed class ProjectionHandler<T> : IHandler<T> { }
		                                       public sealed class Dispatcher { public Dispatcher(IEnumerable<IHandler<OrderPlaced>> handlers) { } }

		                                       [Container]
		                                       [Transient(typeof(AuditHandler<>), typeof(IHandler<>))]
		                                       [Transient(typeof(ProjectionHandler<>), typeof(IHandler<>))]
		                                       [Transient<Dispatcher>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Both open registrations are expanded at OrderPlaced, so the collection of IHandler<OrderPlaced> is a
		// closed array over both expanded implementations (the winner still takes the single-dispatch slot).
		await That(source).Contains("new global::MyCode.IHandler<global::MyCode.OrderPlaced>[] {");
		await That(source).Contains("new global::MyCode.AuditHandler<global::MyCode.OrderPlaced>()");
		await That(source).Contains("new global::MyCode.ProjectionHandler<global::MyCode.OrderPlaced>()");
	}

	[Fact]
	public async Task Variance_RedirectsASingleServiceRequestToTheVarianceCompatibleRegistration()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public class DomainEvent { }
			public sealed class OrderPlaced : DomainEvent { }
			public interface IHandler<in T> { }
			public sealed class DomainEventHandler : IHandler<DomainEvent> { }
			public sealed class OrderConsumer { public OrderConsumer(IHandler<OrderPlaced> handler) { } }

			[Container]
			[Transient<DomainEventHandler, IHandler<DomainEvent>>]
			[Transient<OrderConsumer>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// IHandler<OrderPlaced> has no exact registration; the contravariant IHandler<DomainEvent> (in T) is
		// redirected to, reusing its resolver in the consumer's construction.
		await That(source).Contains("return new global::MyCode.OrderConsumer(ResolveDomainEventHandler());");
		// The requested closed type is a top-level dispatch alias on the same target (Part B): Resolve(typeof(
		// IHandler<OrderPlaced>)) routes to the DomainEventHandler resolver too.
		await That(source).Contains("new __Bucket(typeof(global::MyCode.IHandler<global::MyCode.OrderPlaced>), static __s => __s.ResolveDomainEventHandler(), false)");
	}

	[Fact]
	public async Task Variance_UnionsVarianceCompatibleRegistrationsIntoAClosedGenericCollection()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;

			namespace MyCode;

			public class DomainEvent { }
			public sealed class OrderPlaced : DomainEvent { }
			public interface IHandler<in T> { }
			public sealed class OrderPlacedHandler : IHandler<OrderPlaced> { }
			public sealed class DomainEventHandler : IHandler<DomainEvent> { }
			public sealed class Dispatcher { public Dispatcher(IEnumerable<IHandler<OrderPlaced>> handlers) { } }

			[Container]
			[Transient<OrderPlacedHandler, IHandler<OrderPlaced>>]
			[Transient<DomainEventHandler, IHandler<DomainEvent>>]
			[Transient<Dispatcher>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The collection of IHandler<OrderPlaced> unions the exact OrderPlacedHandler (leading) with the
		// contravariant IHandler<DomainEvent> registration (following).
		await That(source).Contains("new global::MyCode.IHandler<global::MyCode.OrderPlaced>[] { ResolveOrderPlacedHandler(), ResolveDomainEventHandler() }");
	}

	[Fact]
	public async Task Variance_DoesNotRedirectThroughAnInvariantLazyOrTaskWrapper()
	{
		GeneratorResult result = Generator.Run("""
			using System;
			using System.Threading.Tasks;
			using Awaiten;

			namespace MyCode;

			public class DomainEvent { }
			public sealed class OrderPlaced : DomainEvent { }
			public interface IHandler<in T> { }
			public sealed class DomainEventHandler : IHandler<DomainEvent> { }
			public sealed class LazyConsumer { public LazyConsumer(Lazy<IHandler<OrderPlaced>> handler) { } }
			public sealed class TaskConsumer { public TaskConsumer(Task<IHandler<OrderPlaced>> handler) { } }

			[Container]
			[Transient<DomainEventHandler, IHandler<DomainEvent>>]
			[Transient<LazyConsumer>]
			[Transient<TaskConsumer>]
			public static partial class MyContainer
			{
			}
			""");

		// Lazy<T> and Task<T> are invariant in T: no conversion exists from a wrapper over the registered
		// IHandler<DomainEvent> to the declared wrapper over IHandler<OrderPlaced>, so redirecting would emit an
		// argument the parameter cannot accept. The wrapped request stays a plain missing dependency instead.
		await That(result.Diagnostics).Contains("*AWT101*").AsWildcard();
	}

	[Fact]
	public async Task Variance_ValueTypeArgumentIsNeverVarianceConvertible()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public interface IHandler<in T> { }
			public sealed class ObjectHandler : IHandler<object> { }
			public sealed class IntConsumer { public IntConsumer(IHandler<int> handler) { } }

			[Container]
			[Transient<ObjectHandler, IHandler<object>>]
			[Transient<IntConsumer>]
			public static partial class MyContainer
			{
			}
			""");

		// int converts to object only by boxing, not by a reference conversion, so C# variance does not apply:
		// IHandler<object> never satisfies IHandler<int>, and the request is a plain missing dependency.
		await That(result.Diagnostics).Contains("*AWT101*").AsWildcard();
	}

	[Fact]
	public async Task Variance_EmitsTheRuntimeFallbackOnlyWhenAVariantCandidateExists()
	{
		GeneratorResult variant = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public class DomainEvent { }
			public interface IHandler<in T> { }
			public sealed class DomainEventHandler : IHandler<DomainEvent> { }

			[Container]
			[Transient<DomainEventHandler, IHandler<DomainEvent>>]
			public static partial class MyContainer
			{
			}
			""");

		GeneratorResult invariant = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public sealed class Order { }
			public interface IStore<T> { }
			public sealed class OrderStore : IStore<Order> { }

			[Container]
			[Transient<OrderStore, IStore<Order>>]
			public static partial class MyContainer
			{
			}
			""");

		await That(variant.Diagnostics).IsEmpty();
		await That(invariant.Diagnostics).IsEmpty();

		// A registered variant closed generic interface makes the by-type dispatch fall back to runtime variance
		// matching on a miss, so a purely imperative Resolve of a differently-closed request (which no consumer
		// parameter turned into a compile-time alias) still routes. An invariant interface can never satisfy a
		// different closure, so such a container emits no fallback machinery at all.
		await That(variant.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("__TryResolveVariant");
		await That(invariant.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("__TryResolveVariant");
	}

	[Fact]
	public async Task Variance_WinsOverTheImportServicesFallThrough()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public class DomainEvent { }
			public sealed class OrderPlaced : DomainEvent { }
			public interface IHandler<in T> { }
			public sealed class DomainEventHandler : IHandler<DomainEvent> { }
			public sealed class OrderConsumer { public OrderConsumer(IHandler<OrderPlaced> handler) { } }

			[Container]
			[ImportServices]
			[Transient<DomainEventHandler, IHandler<DomainEvent>>]
			[Transient<OrderConsumer>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// A variance match makes the dependency container-resolved, so it is not "otherwise-unresolved" and
		// never falls through to the external provider: the redirect to the registered IHandler<DomainEvent>
		// wins over [ImportServices].
		await That(source).Contains("return new global::MyCode.OrderConsumer(ResolveDomainEventHandler());");
		await That(source).DoesNotContain("__ResolveExternal(typeof(global::MyCode.IHandler<global::MyCode.OrderPlaced>)");
	}

	[Fact]
	public async Task OpenGeneric_SeedsExpansionFromTheConstructorTheContainerActuallyUses()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T> { }
		                                       // Only an internal constructor: the container (same assembly) uses it, so seeding must scan it
		                                       // too - a public-only seed heuristic would miss IRepository<Order> and report a false AWT101.
		                                       public sealed class Root { internal Root(IRepository<Order> orders) { } }

		                                       [Container]
		                                       [Transient(typeof(Repository<>), typeof(IRepository<>))]
		                                       [Transient<Root>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The internal constructor's IRepository<Order> dependency seeded expansion of Repository<Order>.
		await That(source).Contains("new global::MyCode.Repository<global::MyCode.Order>()");
	}

	[Fact]
	public async Task OpenGeneric_SelectsTheResolvableConstructorDependingOnAnExpandableOpenGeneric()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T> { }
		                                       public sealed class Dep { }
		                                       public sealed class Unregistered { }

		                                       public sealed class Service
		                                       {
		                                           // The greedier constructor is unresolvable (Unregistered has no registration); the container
		                                           // uses this one. IRepository<Order> appears in no other constructor, so it is expanded only if
		                                           // the seed recognizes it as satisfiable-via-open-generic and scans this constructor.
		                                           public Service(IRepository<Order> repository) { }
		                                           public Service(Dep dep, Unregistered unregistered) { }
		                                       }

		                                       [Container]
		                                       [Transient(typeof(Repository<>), typeof(IRepository<>))]
		                                       [Transient<Dep>]
		                                       [Transient<Service>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("the resolvable constructor's open generic dependency is expandable, so no AWT101 is reported");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The seed picked the resolvable one-parameter constructor and expanded its open generic dependency.
		await That(source).Contains("new global::MyCode.Repository<global::MyCode.Order>()");
	}

	[Fact]
	public async Task FromServicesParameter_IsResolvedExternallyWithoutAwt101()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface ILogger { }
		                                       public sealed class Service { public Service([FromServices] ILogger logger) { } }

		                                       [Container]
		                                       [Singleton<Service>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a [FromServices] parameter is resolved from the external provider, not the Awaiten graph");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("protected object __ResolveExternal(global::System.Type serviceType, object? serviceKey)");
		await That(source).Contains("new global::MyCode.Service((global::MyCode.ILogger)__ResolveExternal(typeof(global::MyCode.ILogger), null))");
		// The external dependency is advertised in the container metadata.
		await That(source).Contains("public global::System.Collections.Generic.IReadOnlyList<global::System.Type> ExternalDependencies");
		await That(source).Contains("typeof(global::MyCode.ILogger)");
	}

	[Fact]
	public async Task ImportServices_RoutesUnresolvedDependenciesExternallyWithoutAwt101()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface ILogger { }
		                                       public sealed class Service { public Service(ILogger logger) { } }

		                                       [Container]
		                                       [ImportServices]
		                                       [Singleton<Service>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("[ImportServices] routes an otherwise-unresolved direct dependency to the external provider");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("(global::MyCode.ILogger)__ResolveExternal(typeof(global::MyCode.ILogger), null)");
	}

	[Fact]
	public async Task ImportServices_ExpandsOpenGenericsForTheImportSelectedConstructor()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T> { }
		                                       public sealed class Dep { }
		                                       public interface ILogger { }

		                                       public sealed class Service
		                                       {
		                                           public Service(Dep dep) { }
		                                           // With [ImportServices] the greedier constructor is satisfiable (ILogger falls through to
		                                           // the external provider), so the container builds through it - and IRepository<Order> must
		                                           // still be expanded from the open registration, not routed externally too.
		                                           public Service(Dep dep, IRepository<Order> repository, ILogger logger) { }
		                                       }

		                                       [Container]
		                                       [ImportServices]
		                                       [Transient(typeof(Repository<>), typeof(IRepository<>))]
		                                       [Transient<Dep>]
		                                       [Transient<Service>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("the open generic dependency of the import-selected constructor is expandable");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The seed scanned the same (greedier) constructor the container builds through, so the open generic
		// was expanded from the Awaiten registration; only ILogger is external.
		await That(source).Contains("new global::MyCode.Repository<global::MyCode.Order>()");
		await That(source).DoesNotContain("__ResolveExternal(typeof(global::MyCode.IRepository<global::MyCode.Order>)");
		await That(source).Contains("(global::MyCode.ILogger)__ResolveExternal(typeof(global::MyCode.ILogger), null)");
	}

	[Fact]
	public async Task FromServicesWithFromKey_ForwardsTheKeyToTheExternalResolver()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface ILogger { }
		                                       public sealed class Service { public Service([FromServices] [FromKey("audit")] ILogger logger) { } }

		                                       [Container]
		                                       [Singleton<Service>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a keyed [FromServices] parameter is resolved from the external provider under its key");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("(global::MyCode.ILogger)__ResolveExternal(typeof(global::MyCode.ILogger), \"audit\")");
	}
}
