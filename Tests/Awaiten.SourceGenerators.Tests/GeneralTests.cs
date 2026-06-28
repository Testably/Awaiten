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
			.Contains("private static readonly global::System.Collections.Generic.Dictionary<global::System.Type, int> __dispatch")
			.Because("resolution is dispatched through a static type-to-case table");
		await That(source).Contains("public sealed class Root : Scope")
			.Because("the root scope is the usable container instance, created with new MyContainer.Root()");
		await That(source).Contains("__dispatch.TryGetValue(serviceType, out int __case)")
			.Because("the scope dispatches through the static table that lives on it");
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
			"typeof(global::System.Lazy<global::MyCode.Leaf>)",
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
		await That(source).Contains("{ typeof(global::System.Func<global::MyCode.Leaf>),")
			.Because("Func<T> is also resolvable directly through the dispatch table");
		await That(source).Contains("instance = new global::System.Func<global::MyCode.Leaf>(() => ResolveLeaf()); return true;")
			.Because("its dispatch case builds a fresh factory over the target's resolver");
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
		await That(source).Contains("lock (this)")
			.Because("singletons are created once under a lock");
		await That(source).Contains("_middle = new global::MyCode.Middle(__root.ResolveLeaf());")
			.Because("singletons are memoized into their backing field and read straight off the root scope");
		await That(source).Contains("return new global::MyCode.Top(__root.ResolveMiddle(), __root.ResolveLeaf());")
			.Because("transients are constructed on each request, not cached");

		await That(source).Contains("{ typeof(global::MyCode.IMiddle),")
			.Because("each service type is a key in the static dispatch table");
		await That(source).Contains("instance = ResolveMiddle(); return true;")
			.Because("its dispatch case resolves the registered service");
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
		await That(source).Contains("{ typeof(global::System.Lazy<global::MyCode.Leaf>),")
			.Because("Lazy<T> is also resolvable directly through the dispatch table");
		await That(source).Contains("instance = new global::System.Lazy<global::MyCode.Leaf>(() => ResolveLeaf()); return true;")
			.Because("its dispatch case builds a fresh lazy over the target's resolver");
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
		await That(source).Contains("{ typeof(global::MyCode.IReader),")
			.Because("each service type is a key in the static dispatch table");
		await That(source).Contains("{ typeof(global::MyCode.IWriter),")
			.Because("each service type is a key in the static dispatch table");
		await That(source).Contains("instance = ResolveStore(); return true;")
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
		await That(source).Contains("return MakeClock();")
			.Because("the scope calls the static factory method by simple name instead of constructing the type");
		await That(source).DoesNotContain("new global::MyCode.SystemClock(")
			.Because("a factory registration is produced by its method, never constructed directly");
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
	}
}
