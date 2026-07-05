using System.Linq;

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

		await That(source).Contains("new global::MyCode.Consumer(new global::System.Func<global::MyCode.Leaf>(() => ResolveLeaf(__s)))")
			.Because("the Func parameter is supplied as a factory bound to the owner's resolver");
		await That(source).Contains("new __Bucket(typeof(global::System.Func<global::MyCode.Leaf>),")
			.Because("Func<T> is also resolvable directly through the dispatch table");
		await That(source).Contains("(Scope __s) => new global::System.Func<global::MyCode.Leaf>(() => ResolveLeaf(__s));")
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
		await That(source).Contains("lock (__s.__gate)")
			.Because("singletons are created once under a lock on a private gate, not the publicly reachable scope");
		await That(source).Contains("__s._middle = new global::MyCode.Middle(Root.ResolveLeaf(__s.__root));")
			.Because("singletons are memoized into their backing field and read straight off the root scope");
		await That(source).Contains("return new global::MyCode.Top(Root.ResolveMiddle(__s.__root), Root.ResolveLeaf(__s.__root));")
			.Because("transients are constructed on each request, not cached");

		await That(source).Contains("new __Bucket(typeof(global::MyCode.IMiddle),")
			.Because("each service type is a key in the static dispatch table");
		await That(source).Contains("static __s => Root.ResolveMiddle(__s.__root)")
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
		await That(source).Contains("__s._foo = new global::MyCode.Foo();");
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

		await That(source).Contains("new global::MyCode.Consumer(new global::System.Lazy<global::MyCode.Leaf>(() => Root.ResolveLeaf(__s.__root)))")
			.Because("the Lazy parameter is supplied as a lazy bound to the owner's resolver");
		await That(source).Contains("new __Bucket(typeof(global::System.Lazy<global::MyCode.Leaf>),")
			.Because("Lazy<T> is also resolvable directly through the dispatch table");
		await That(source).Contains("(Scope __s) => new global::System.Lazy<global::MyCode.Leaf>(() => Root.ResolveLeaf(__s.__root));")
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
		await That(source).Contains("static __s => Root.ResolveStore(__s.__root)")
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
		await That(source).Contains("__s._service = new global::MyCode.Outer.Service();");
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
		await That(source).Contains("(one instance per scope)")
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
		await That(source).Contains("(__s.__disposables ??= new global::System.Collections.Generic.List<object>()).Add(created);")
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
		await That(source).Contains("MakeService(Root.ResolveSettings(__s.__root))")
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
		await That(source).Contains("static __s => Root.ResolveIClock(__s.__root)")
			.Because("a child scope reaches the root's pre-built instance through the static dispatch, like any other singleton");
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

		// The resolver is internal so a root-owned singleton's Func or a throwaway Owned<T> scope can bind it.
		await That(source).Contains("internal static global::MyCode.Robot ResolveRobot(Scope __s, string a0)");
		await That(source).Contains("new global::MyCode.Robot(Root.ResolveEngine(__s.__root), a0)");
		await That(source).Contains("new global::System.Func<string, global::MyCode.Robot>((a0) => ResolveRobot(__s, a0))");
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

		// Handler<Order>'s IValidator<Order> dependency drives a further expansion of Validator<Order>. The worklist iterates to a fixpoint.
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

		// Both open registrations expand at OrderPlaced into a closed array; the winner still takes the single-dispatch slot.
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

		// IHandler<OrderPlaced> has no exact registration, so the contravariant IHandler<DomainEvent> is redirected to.
		await That(source).Contains("return new global::MyCode.OrderConsumer(ResolveDomainEventHandler(__s));");
		// The requested closed type is also a top-level dispatch alias routing to the DomainEventHandler resolver.
		await That(source).Contains("new __Bucket(typeof(global::MyCode.IHandler<global::MyCode.OrderPlaced>), static __s => ResolveDomainEventHandler(__s), false)");
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

		// The collection unions the exact OrderPlacedHandler (leading) with the contravariant IHandler<DomainEvent> (following).
		await That(source).Contains("new global::MyCode.IHandler<global::MyCode.OrderPlaced>[] { ResolveOrderPlacedHandler(__s), ResolveDomainEventHandler(__s) }");
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

		// Lazy<T> and Task<T> are invariant in T, so no variance conversion exists to the declared wrapper. The wrapped request stays a plain missing dependency (AWT101).
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

		// int converts to object only by boxing, not a reference conversion, so variance does not apply. IHandler<object> never satisfies IHandler<int>.
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

		// A variant closed generic makes by-type dispatch fall back to runtime variance matching on a miss, so an imperative Resolve of a differently-closed request still routes. An invariant interface needs no fallback machinery.
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

		// A variance match makes the dependency container-resolved, so it never falls through to [ImportServices].
		await That(source).Contains("return new global::MyCode.OrderConsumer(ResolveDomainEventHandler(__s));");
		await That(source).DoesNotContain("__s.__ResolveExternal(typeof(global::MyCode.IHandler<global::MyCode.OrderPlaced>)");
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
		await That(source).Contains("new global::MyCode.Service((global::MyCode.ILogger)__s.__ResolveExternal(typeof(global::MyCode.ILogger), null))");
		// The external dependency is advertised in the container metadata.
		await That(source).Contains("global::Awaiten.IAwaitenContainerMetadata.ExternalDependencies");
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
		await That(source).Contains("(global::MyCode.ILogger)__s.__ResolveExternal(typeof(global::MyCode.ILogger), null)");
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

		// The seed scanned the same constructor the container builds through, so the open generic expands from the registration; only ILogger is external.
		await That(source).Contains("new global::MyCode.Repository<global::MyCode.Order>()");
		await That(source).DoesNotContain("__s.__ResolveExternal(typeof(global::MyCode.IRepository<global::MyCode.Order>)");
		await That(source).Contains("(global::MyCode.ILogger)__s.__ResolveExternal(typeof(global::MyCode.ILogger), null)");
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
		await That(source).Contains("(global::MyCode.ILogger)__s.__ResolveExternal(typeof(global::MyCode.ILogger), \"audit\")");
	}

	[Fact]
	public async Task GeneratedResolvers_CarryLifetimeSpecificXmlDocSummaries()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Single { }
		                                       public sealed class PerScope { }
		                                       public sealed class Fresh { }

		                                       [Container]
		                                       [Singleton<Single>]
		                                       [Scoped<PerScope>]
		                                       [Transient<Fresh>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("/// <summary>")
			.Because("the generated members carry XML doc summaries");
		await That(source).Contains("Resolves the singleton <see cref=\"global::MyCode.Single\" /> (one instance per container).")
			.Because("the Root's singleton resolver documents its lifetime");
		await That(source).Contains("Resolves the scoped <see cref=\"global::MyCode.PerScope\" /> (one instance per scope).")
			.Because("the scoped resolver documents its per-scope caching");
		await That(source).Contains("Resolves the transient <see cref=\"global::MyCode.Fresh\" /> (a new instance per call).")
			.Because("the transient resolver documents its fresh-per-call semantics");
		await That(source).Contains("The container root: owns the singleton instances and serves as the default scope.")
			.Because("the Root class itself is documented");
		await That(source).Contains("A resolution scope: caches scoped services and disposes the instances it created.")
			.Because("the Scope class itself is documented");
		await That(source).Contains("Disposes every tracked instance in reverse creation order.")
			.Because("the disposal surface is documented");
	}

	[Fact]
	public async Task KeyedDictionaryDependency_MaterializesAllKeyedRegistrationsAsADictionary()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class Slow : IChannel { }
			public sealed class Router { public Router(IReadOnlyDictionary<string, IChannel> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<Slow, IChannel>(Key = "slow")]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The keyed dictionary materializes inline, keyed by each member's [Key] in registration order, each calling its static resolver over the current owner.
		await That(source).Contains("new global::MyCode.Router(new global::System.Collections.Generic.Dictionary<string, global::MyCode.IChannel> { [\"fast\"] = Root.ResolveFast(__s.__root), [\"slow\"] = Root.ResolveSlow(__s.__root) })");
		// IReadOnlyDictionary<string, T> is also publicly resolvable by type.
		await That(source).Contains("typeof(global::System.Collections.Generic.IReadOnlyDictionary<string, global::MyCode.IChannel>)");
	}

	[Fact]
	public async Task EmptyKeyedDictionary_MaterializesAnEmptyDictionaryWithoutAwt101()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Host { public Host(IReadOnlyDictionary<string, IChannel> channels) { } }

			[Container]
			[Singleton<Host>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// An element type with no keyed registration yields a completed empty dictionary, not a missing dependency.
		await That(source).Contains("new global::MyCode.Host(new global::System.Collections.Generic.Dictionary<string, global::MyCode.IChannel> {  })");
	}

	[Fact]
	public async Task NonStringKeyedDictionary_ReportsAwt159()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;

			namespace MyCode;

			public enum ProcessType { A, B }
			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class Host { public Host(IReadOnlyDictionary<ProcessType, IChannel> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<Host>]
			public static partial class MyContainer
			{
			}
			""");

		// v1 supports only string keys, so a non-string key type is rejected (AWT159).
		await That(result.Diagnostics).Contains("*AWT159*").AsWildcard();
	}

	[Fact]
	public async Task ExplicitlyRegisteredKeyedDictionary_WinsOverSynthesisOnInjection()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class ChannelMap : Dictionary<string, IChannel> { }
			public sealed class Router { public Router(IReadOnlyDictionary<string, IChannel> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<ChannelMap, IReadOnlyDictionary<string, IChannel>>]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The dictionary is itself a registered service, so the parameter is a direct dependency on it, not the synthesized keyed dictionary.
		await That(source).Contains("new global::MyCode.Router(Root.ResolveChannelMap(__s.__root))")
			.Because("an explicitly registered dictionary service wins over the synthesized keyed dictionary on injection");

		// The synthesized dictionary is suppressed outright; no dictionary literal is emitted anywhere.
		await That(source).DoesNotContain("new global::System.Collections.Generic.Dictionary<string, global::MyCode.IChannel>")
			.Because("a registered dictionary suppresses the synthesized keyed dictionary, mirroring the collection SynthesisSuppressed gate");
	}

	[Fact]
	public async Task ExplicitlyRegisteredKeyedDictionary_WinsOverSynthesisOnPropertyInjectionToo()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class ChannelMap : Dictionary<string, IChannel> { }
			public sealed class Router
			{
			    [Inject]
			    public IReadOnlyDictionary<string, IChannel> Channels { get; set; }
			}

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<ChannelMap, IReadOnlyDictionary<string, IChannel>>]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// An [Inject] member resolves like a constructor parameter, so the registered dictionary preempts synthesis for the property too.
		await That(source).Contains("Channels = Root.ResolveChannelMap(__s.__root)")
			.Because("an explicitly registered dictionary service preempts the synthesized keyed dictionary on property injection");
	}

	[Fact]
	public async Task ExplicitlyRegisteredNonStringKeyedDictionary_IsADirectDependencyWithoutAwt159()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class PortMap : Dictionary<int, IChannel> { }
			public sealed class Router { public Router(IReadOnlyDictionary<int, IChannel> ports) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<PortMap, IReadOnlyDictionary<int, IChannel>>]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		// An explicitly registered dictionary is an ordinary direct dependency whatever its key type; AWT159 gates only the synthesized dictionary.
		await That(result.Diagnostics).IsEmpty()
			.Because("an explicitly registered non-string-keyed dictionary is an opaque registered value, not a rejected synthesized collection");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("new global::MyCode.Router(Root.ResolvePortMap(__s.__root))");
	}

	[Fact]
	public async Task FromKeyOnASynthesizedKeyedDictionary_ReportsAwt160()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class Router { public Router([FromKey("fast")] IReadOnlyDictionary<string, IChannel> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		// A [FromKey] cannot select within the synthesized dictionary, so it is rejected (AWT160).
		await That(result.Diagnostics).Contains("*AWT160*").AsWildcard();
	}

	[Fact]
	public async Task FromKeyOnAKeyedDictionary_ResolvesAnExplicitKeyedRegistrationOfTheDictionaryType()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class ChannelMap : Dictionary<string, IChannel> { }
			public sealed class Router { public Router([FromKey("primary")] IReadOnlyDictionary<string, IChannel> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<ChannelMap, IReadOnlyDictionary<string, IChannel>>(Key = "primary")]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		// A dictionary registered under the requested key preempts synthesis, so the [FromKey] is a legitimate keyed selection (no AWT160).
		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("new global::MyCode.Router(Root.ResolveChannelMap(__s.__root))")
			.Because("a [FromKey] on a keyed dictionary selects an explicitly registered dictionary service under that key");
	}

	[Fact]
	public async Task SameImplementationRegisteredUnderTwoKeys_AppearsUnderBothDictionaryKeys()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class Router { public Router(IReadOnlyDictionary<string, IChannel> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<Fast, IChannel>(Key = "turbo")]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// One implementation under two keys yields two dictionary entries sharing the single resolver (and, as a singleton, the single instance).
		await That(source).Contains("[\"fast\"] = Root.ResolveFast(__s.__root), [\"turbo\"] = Root.ResolveFast(__s.__root)");
	}

	[Fact]
	public async Task AwaitedKeyedDictionary_AllSynchronousMembers_MaterializesACompletedTask()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading.Tasks;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class Slow : IChannel { }
			public sealed class Router { public Router(Task<IReadOnlyDictionary<string, IChannel>> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<Slow, IChannel>(Key = "slow")]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("Task<IReadOnlyDictionary<string, T>> is the awaited keyed dictionary of T, not a missing dependency on the dictionary type");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Every keyed member is synchronous, so the awaited dictionary is a completed Task.FromResult over the synchronous dictionary.
		await That(source).Contains("new global::MyCode.Router(global::System.Threading.Tasks.Task.FromResult<global::System.Collections.Generic.IReadOnlyDictionary<string, global::MyCode.IChannel>>(new global::System.Collections.Generic.Dictionary<string, global::MyCode.IChannel> { [\"fast\"] = Root.ResolveFast(__s.__root), [\"slow\"] = Root.ResolveSlow(__s.__root) }))")
			.Because("an all-synchronous awaited keyed dictionary completes immediately over the materialized dictionary");
	}

	[Fact]
	public async Task AwaitedKeyedDictionaryWithAnAsyncMember_AwaitsItInsideTheProducedTaskWithoutTaintingTheConsumer()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading;
			using System.Threading.Tasks;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class AsyncSlow : IChannel, IAsyncInitializable
			{
			    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			}
			public sealed class Router { public Router(Task<IReadOnlyDictionary<string, IChannel>> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<AsyncSlow, IChannel>(Key = "slow")]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		// The awaited dictionary awaits its members behind the returned task, so an async-tainted member is legal (never AWT122, unlike the synchronous dictionary).
		await That(result.Diagnostics).DoesNotContain("*AWT122*").AsWildcard()
			.Because("an awaited keyed dictionary awaits its async-initialized members behind the produced task");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The async-tainted member is awaited inside an immediately-invoked async lambda (no ambient token; the consumer is built synchronously); the sync member resolves directly.
		await That(source).Contains("((global::System.Func<global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyDictionary<string, global::MyCode.IChannel>>>)(async () => (global::System.Collections.Generic.IReadOnlyDictionary<string, global::MyCode.IChannel>)new global::System.Collections.Generic.Dictionary<string, global::MyCode.IChannel> { [\"fast\"] = Root.ResolveFast(__s.__root), [\"slow\"] = await Root.ResolveAsyncSlowAsync(__s.__root, default).ConfigureAwait(false) }))()")
			.Because("the async-tainted keyed member is awaited inside the produced task, in registration order");

		// The awaited keyed dictionary launders its members' taint, so the router stays synchronously constructible even with an async-tainted member.
		await That(source).Contains("typeof(global::MyCode.Router), static __s => Root.ResolveRouter(__s.__root)")
			.Because("a consumer of an awaited keyed dictionary stays synchronously resolvable even when a member is async-tainted");
	}

	[Fact]
	public async Task EmptyAwaitedKeyedDictionary_MaterializesACompletedEmptyTaskWithoutAwt101()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading.Tasks;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Host { public Host(Task<IReadOnlyDictionary<string, IChannel>> channels) { } }

			[Container]
			[Singleton<Host>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("a service with no keyed registration resolves to an empty awaited keyed dictionary, not a missing-dependency error");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.Host(global::System.Threading.Tasks.Task.FromResult<global::System.Collections.Generic.IReadOnlyDictionary<string, global::MyCode.IChannel>>(new global::System.Collections.Generic.Dictionary<string, global::MyCode.IChannel> {  }))")
			.Because("an element type with no keyed registration materializes a completed empty awaited keyed dictionary");
	}

	[Fact]
	public async Task NonStringAwaitedKeyedDictionary_ReportsAwt159()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading.Tasks;

			namespace MyCode;

			public enum ProcessType { A, B }
			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class Host { public Host(Task<IReadOnlyDictionary<ProcessType, IChannel>> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<Host>]
			public static partial class MyContainer
			{
			}
			""");

		// The awaited keyed dictionary supports string keys only, exactly like the synchronous dictionary.
		await That(result.Diagnostics).Contains("*AWT159*").AsWildcard()
			.Because("a non-string key type is rejected for the awaited keyed dictionary just as for the synchronous one");
	}

	[Fact]
	public async Task FromKeyOnASynthesizedAwaitedKeyedDictionary_ReportsAwt160()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading.Tasks;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class Router { public Router([FromKey("fast")] Task<IReadOnlyDictionary<string, IChannel>> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		// A [FromKey] cannot select within the synthesized awaited dictionary, so it is rejected (AWT160), like the synchronous one.
		await That(result.Diagnostics).Contains("*AWT160*").AsWildcard()
			.Because("a [FromKey] on a synthesized awaited keyed dictionary is rejected just as on the synchronous one");
	}

	[Fact]
	public async Task ExplicitlyRegisteredAwaitedKeyedDictionary_WinsOverSynthesisOnInjection()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading.Tasks;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class ChannelMapTask : Task<IReadOnlyDictionary<string, IChannel>>
			{
			    public ChannelMapTask() : base(() => null) { }
			}
			public sealed class Router { public Router(Task<IReadOnlyDictionary<string, IChannel>> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<ChannelMapTask, Task<IReadOnlyDictionary<string, IChannel>>>]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The Task<…> is itself a registered service, so the parameter is a direct dependency on it, not a synthesized awaited dictionary.
		await That(source).Contains("new global::MyCode.Router(Root.ResolveChannelMapTask(__s.__root))")
			.Because("an explicitly registered Task<IReadOnlyDictionary<…>> claims its own exact shape, winning over the synthesized awaited keyed dictionary");
		await That(source).DoesNotContain("global::System.Threading.Tasks.Task.FromResult<global::System.Collections.Generic.IReadOnlyDictionary<string, global::MyCode.IChannel>>(new global::System.Collections.Generic.Dictionary")
			.Because("no awaited keyed dictionary is synthesized behind the registered shape");
	}

	[Fact]
	public async Task RegisteredSyncKeyedDictionary_SuppressesTheAwaitedViewOnInjection()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading.Tasks;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class ChannelMap : Dictionary<string, IChannel> { }
			public sealed class Router { public Router(Task<IReadOnlyDictionary<string, IChannel>> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<ChannelMap, IReadOnlyDictionary<string, IChannel>>]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		// A registered synchronous dictionary claims the awaited Task<…> view too (all-or-nothing), so the awaited sibling becomes a plain missing dependency on the unregistered Task<…> type.
		await That(result.Diagnostics)
			.Contains("*AWT101*requires 'System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyDictionary<string, MyCode.IChannel>>', which is not registered*").AsWildcard()
			.Because("a registered synchronous keyed dictionary suppresses the awaited view, mirroring how a registered sync collection shape suppresses Task<C>");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).DoesNotContain("Task.FromResult<global::System.Collections.Generic.IReadOnlyDictionary<string, global::MyCode.IChannel>>")
			.Because("no awaited keyed dictionary is synthesized behind the registered synchronous one");
	}

	[Fact]
	public async Task AwaitedKeyedDictionary_JoinsTheByTypeDispatch()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading;
			using System.Threading.Tasks;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class AsyncSlow : IChannel, IAsyncInitializable
			{
			    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			}

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<AsyncSlow, IChannel>(Key = "slow")]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).DoesNotContain("*AWT122*").AsWildcard();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Even when a keyed member is async-tainted (withholding the synchronous shape), the awaited Task<…> view is always synchronously obtainable and gets its own dispatch slot.
		await That(source).Contains("typeof(global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyDictionary<string, global::MyCode.IChannel>>), static __s => __R")
			.Because("the awaited keyed dictionary joins the synchronous by-type dispatch even when a member is async-tainted");
	}

	[Fact]
	public async Task RegisteredSyncKeyedDictionary_SuppressesTheAwaitedByTypeDispatch()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading.Tasks;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class ChannelMap : Dictionary<string, IChannel> { }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<ChannelMap, IReadOnlyDictionary<string, IChannel>>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The registered synchronous dictionary owns the slot; no awaited Task<…> view is synthesized behind it (all-or-nothing).
		await That(source).DoesNotContain("typeof(global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyDictionary<string, global::MyCode.IChannel>>)")
			.Because("a registered synchronous keyed dictionary suppresses the synthesized awaited view on the by-type dispatch too");
	}

	[Fact]
	public async Task AsyncTaintedRegisteredAwaitedKeyedDictionary_IsNotShadowedOnTheByTypeDispatch()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading;
			using System.Threading.Tasks;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class ChannelMapTask : Task<IReadOnlyDictionary<string, IChannel>>, IAsyncInitializable
			{
			    public ChannelMapTask() : base(() => null) { }
			    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			}

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<ChannelMapTask, Task<IReadOnlyDictionary<string, IChannel>>>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The async-tainted registration is excluded from the sync dispatch that seeds the seen guard, so the synthesized dictionary could silently claim its slot on the sync path. The registration is therefore checked directly.
		await That(source).DoesNotContain("global::System.Threading.Tasks.Task.FromResult<global::System.Collections.Generic.IReadOnlyDictionary<string, global::MyCode.IChannel>>")
			.Because("no awaited keyed dictionary is synthesized behind the explicitly registered Task<…>, even when that registration is async-tainted");
		await That(source).Contains("ResolveChannelMapTaskAsync")
			.Because("the async-tainted registration keeps its own async resolution path");
	}

	[Fact]
	public async Task NonStringAwaitedKeyedDictionary_OverARegisteredDictionary_ResolvesTheRegistration()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading.Tasks;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class IntMap : Dictionary<int, IChannel> { }
			public sealed class Host { public Host(Task<IReadOnlyDictionary<int, IChannel>> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<IntMap, IReadOnlyDictionary<int, IChannel>>]
			[Singleton<Host>]
			public static partial class MyContainer
			{
			}
			""");

		// A non-string key admits no synthesized awaited view (unregistered it is AWT159), so over a registered dictionary it stays the bare Task relationship resolving the registration (neither AWT159 nor AWT101).
		await That(result.Diagnostics).IsEmpty()
			.Because("Task<IReadOnlyDictionary<int, T>> over a registered IReadOnlyDictionary<int, T> resolves the registration through the bare Task relationship");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("ResolveIntMap")
			.Because("the awaited view hands back the registered dictionary behind a task");
	}

	[Fact]
	public async Task FromKeyAwaitedKeyedDictionary_OverAKeyRegisteredDictionary_ResolvesTheRegistration()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System.Collections.Generic;
			using System.Threading.Tasks;

			namespace MyCode;

			public interface IChannel { }
			public sealed class Fast : IChannel { }
			public sealed class ChannelMap : Dictionary<string, IChannel> { }
			public sealed class Router { public Router([FromKey("map")] Task<IReadOnlyDictionary<string, IChannel>> channels) { } }

			[Container]
			[Singleton<Fast, IChannel>(Key = "fast")]
			[Singleton<ChannelMap, IReadOnlyDictionary<string, IChannel>>(Key = "map")]
			[Singleton<Router>]
			public static partial class MyContainer
			{
			}
			""");

		// A [FromKey] selection admits no synthesized awaited view (it would be AWT160), so over a dictionary registered under that key it stays the bare Task relationship resolving the registration (neither AWT160 nor AWT101).
		await That(result.Diagnostics).IsEmpty()
			.Because("[FromKey] Task<IReadOnlyDictionary<string, T>> over a dictionary registered under that key resolves the registration");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("ResolveChannelMap")
			.Because("the awaited view hands back the keyed registered dictionary behind a task");
	}


	[Fact]
	public async Task RequestingTypeFactory_EmitsTheConsumerTypeofPerSite()
	{
		GeneratorResult result = Generator.Run("""
			#nullable enable
			using System;
			using Awaiten;

			namespace MyCode;

			public interface ILogger { }
			public sealed class Logger : ILogger { public Logger(string c) { } }
			public sealed class Alpha { public Alpha(ILogger logger) { } }
			public sealed class Beta { public Beta(ILogger logger) { } }

			[Container]
			[Transient<ILogger>(Factory = nameof(CreateLogger))]
			[Transient<Alpha>]
			[Transient<Beta>]
			public static partial class MyContainer
			{
				private static ILogger CreateLogger([RequestingType] Type? t) => new Logger(t?.FullName ?? "<root>");
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The factory resolver takes the requesting type (never cached) and forwards it into the [RequestingType] slot.
		await That(source).Contains("internal static global::MyCode.ILogger ResolveILogger(Scope __s, global::System.Type? __requestingType)");
		await That(source).Contains("CreateLogger(__requestingType!)");
		// Each construction site embeds its own consumer typeof(…) literal.
		await That(source).Contains("new global::MyCode.Alpha(ResolveILogger(__s, typeof(global::MyCode.Alpha)))");
		await That(source).Contains("new global::MyCode.Beta(ResolveILogger(__s, typeof(global::MyCode.Beta)))");
		// A top-level resolve has no requesting consumer, so the by-type dispatch passes null.
		await That(source).Contains("ResolveILogger(__s, null)");
	}

	[Fact]
	public async Task RequestingTypeFactory_DeclaredSingleton_IsStillBuiltFreshPerCall()
	{
		GeneratorResult result = Generator.Run("""
			#nullable enable
			using System;
			using Awaiten;

			namespace MyCode;

			public interface ILogger { }
			public sealed class Logger : ILogger { public Logger(string c) { } }
			public sealed class Alpha { public Alpha(ILogger logger) { } }

			[Container]
			[Singleton<ILogger>(Factory = nameof(CreateLogger))]
			[Transient<Alpha>]
			public static partial class MyContainer
			{
				private static ILogger CreateLogger([RequestingType] Type? t) => new Logger(t?.FullName ?? "<root>");
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The declared lifetime is ignored for caching: even a Singleton-declared requesting-type factory gets a fresh Scope-hosted resolver, never a cached field or Root resolver.
		await That(source).Contains("internal static global::MyCode.ILogger ResolveILogger(Scope __s, global::System.Type? __requestingType)");
		await That(source).DoesNotContain("Root.ResolveILogger")
			.Because("a requesting-type factory is never root-owned, even when declared a singleton");
		await That(source).Contains("new global::MyCode.Alpha(ResolveILogger(__s, typeof(global::MyCode.Alpha)))");
	}

	[Fact]
	public async Task RequestingType_OnANonTypeParameter_ReportsAwt162()
	{
		GeneratorResult result = Generator.Run("""
			using System;
			using Awaiten;

			namespace MyCode;

			public interface ILogger { }
			public sealed class Logger : ILogger { public Logger(string c) { } }

			[Container]
			[Transient<ILogger>(Factory = nameof(CreateLogger))]
			public static partial class MyContainer
			{
				private static ILogger CreateLogger([RequestingType] string notAType) => new Logger(notAType);
			}
			""");

		await That(result.Diagnostics.Any(d => d.Contains("AWT162"))).IsTrue();
	}

	[Fact]
	public async Task RequestingType_WithAnArgParameter_ReportsAwt163()
	{
		GeneratorResult result = Generator.Run("""
			using System;
			using Awaiten;

			namespace MyCode;

			public interface ILogger { }
			public sealed class Logger : ILogger { public Logger(string c, int n) { } }
			public sealed class Alpha { public Alpha(Func<int, ILogger> f) { } }

			[Container]
			[Transient<ILogger>(Factory = nameof(CreateLogger))]
			[Transient<Alpha>]
			public static partial class MyContainer
			{
				private static ILogger CreateLogger([RequestingType] Type? t, [Arg] int n) => new Logger(t?.FullName ?? "<root>", n);
			}
			""");

		await That(result.Diagnostics.Any(d => d.Contains("AWT163"))).IsTrue();
	}

	[Fact]
	public async Task AsyncFactoryRequestingType_EmitsAnAwaitingResolverThatTakesTheRequestingType()
	{
		GeneratorResult result = Generator.Run("""
			#nullable enable
			using System;
			using System.Threading.Tasks;
			using Awaiten;

			namespace MyCode;

			public interface ILogger { }
			public sealed class Logger : ILogger { public Logger(string c) { } }
			public sealed class Alpha { public Alpha(ILogger logger) { } }

			[Container]
			[Transient<ILogger>(Factory = nameof(CreateLogger))]
			[Transient<Alpha>]
			public static partial class MyContainer
			{
				private static async Task<ILogger> CreateLogger([RequestingType] Type? t) { await Task.Yield(); return new Logger(t?.FullName ?? "<root>"); }
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// An async requesting-type factory gets a fresh async resolver; its consumer awaits it with its own typeof(…), and the top-level arm passes null.
		await That(source).Contains("ResolveILoggerAsync(Scope __s, global::System.Type? __requestingType, global::System.Threading.CancellationToken cancellationToken)");
		await That(source).Contains("await ResolveILoggerAsync(__s, typeof(global::MyCode.Alpha), cancellationToken).ConfigureAwait(false)");
		await That(source).Contains("ResolveILoggerAsync(__s, null, __ct)");
	}

	[Fact]
	public async Task AsyncTaintedRequestingType_ThroughAnAsyncDependency_Compiles()
	{
		GeneratorResult result = Generator.Run("""
			#nullable enable
			using System;
			using System.Threading;
			using System.Threading.Tasks;
			using Awaiten;

			namespace MyCode;

			public interface ILogger { }
			public sealed class Db : IAsyncInitializable { public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask; }
			public sealed class Logger : ILogger { public Logger(string c, Db db) { } }
			public sealed class Alpha { public Alpha(ILogger logger) { } }

			[Container]
			[Singleton<Db>]
			[Transient<ILogger>(Factory = nameof(CreateLogger))]
			[Transient<Alpha>]
			public static partial class MyContainer
			{
				private static ILogger CreateLogger([RequestingType] Type? t, Db db) => new Logger(t?.FullName ?? "<root>", db);
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("ResolveILoggerAsync(Scope __s, global::System.Type? __requestingType, global::System.Threading.CancellationToken cancellationToken)");
		await That(source).Contains("await ResolveILoggerAsync(__s, typeof(global::MyCode.Alpha), cancellationToken).ConfigureAwait(false)");
	}

	[Fact]
	public async Task AsyncRequestingType_UnderSyncResolveAfterInit_EmitsABlockingSyncResolverTakingTheRequestingType()
	{
		GeneratorResult result = Generator.Run("""
			#nullable enable
			using System;
			using System.Threading.Tasks;
			using Awaiten;

			namespace MyCode;

			public interface ILogger { }
			public sealed class Logger : ILogger { public Logger(string c) { } }
			public sealed class Alpha { public Alpha(ILogger logger) { } }

			[Container(SyncResolveAfterInit = true)]
			[Transient<ILogger>(Factory = nameof(CreateLogger))]
			[Transient<Alpha>]
			public static partial class MyContainer
			{
				private static async Task<ILogger> CreateLogger([RequestingType] Type? t) { await Task.Yield(); return new Logger(t?.FullName ?? "<root>"); }
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Pragmatic mode's blocking sync resolver forwards the requesting type. The async-tainted consumer still builds through the async resolver with its own typeof(…).
		await That(source).Contains("internal static global::MyCode.ILogger ResolveILogger(Scope __s, global::System.Type? __requestingType)");
		await That(source).Contains("ResolveILoggerAsync(__s, __requestingType, default).GetAwaiter().GetResult()");
		await That(source).Contains("new global::MyCode.Alpha(await ResolveILoggerAsync(__s, typeof(global::MyCode.Alpha), cancellationToken).ConfigureAwait(false))");
	}

	[Fact]
	public async Task LifecycleHooks_CallActivationAndQueueReleaseRunAheadOfDisposal()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public sealed class Service { }

			[Container]
			[Singleton<Service>(OnActivated = nameof(Started), OnRelease = nameof(Stopping))]
			public static partial class MyContainer
			{
				private static void Started(Service service) { }
				private static void Stopping(Service service) { }
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// A reverse-drained release queue is emitted, the activation hook runs on a local post-construction, and
		// the release hook is queued as an action (capturing that same local by value) that is drained (reverse
		// creation order) before the disposables on teardown.
		await That(source).Contains("global::System.Collections.Generic.List<global::System.Action>? __releases;");
		await That(source).Contains("Started(created);");
		await That(source).Contains(".Add(() => Stopping(created));");
		await That(source).Contains("__toRelease[__index]();");

		// The cache field is published (= created;) only after the activation hook has run, so the lock-free fast
		// path never hands a concurrent caller a published-but-not-yet-activated instance.
		int activationAt = source.IndexOf("Started(created);");
		int publishAt = source.IndexOf("= created;");
		await That(activationAt >= 0 && publishAt > activationAt).IsTrue()
			.Because("the cache field must be published only after activation completes");
	}
}
