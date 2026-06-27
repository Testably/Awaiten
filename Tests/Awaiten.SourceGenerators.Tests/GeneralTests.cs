namespace Awaiten.SourceGenerators.Tests;

public class GeneralTests
{
	[Fact]
	public async Task EmptyContainer_EmitsAPartialImplementationWithoutDiagnostics()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       [Container]
		                                       public partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources).HasCount(1);
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("partial class MyContainer : global::Awaiten.IAwaitenContainer");
		await That(source).Contains("public object Resolve(global::System.Type serviceType)");
		await That(source).Contains("public global::Awaiten.IAwaitenScope CreateScope() => new Scope(this);");
		await That(source).Contains("private sealed class Scope : global::Awaiten.IAwaitenScope");
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
		                                       public partial class MyContainer
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
			.Because("singletons are created once under a lock");
		await That(source).Contains("_middle = new global::MyCode.Middle(ResolveLeaf());")
			.Because("singletons are memoized into their backing field");
		await That(source).Contains("return new global::MyCode.Top(ResolveMiddle(), ResolveLeaf());")
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
		                                       public partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("_foo = new global::MyCode.Foo();");
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
		                                       public partial class MyContainer
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
		                                       	public partial class Inner
		                                       	{
		                                       	}
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources).HasCount(1);
		string source = result.Sources["Awaiten.MyCode.Outer+Inner.g.cs"];
		await That(source).Contains("partial class Outer");
		await That(source).Contains("partial class Inner : global::Awaiten.IAwaitenContainer");
		await That(source).Contains("_service = new global::MyCode.Outer.Service();");
	}

	[Fact]
	public async Task SameSimpleName_InDifferentNamespaces_DoNotCollide()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace A { public sealed class S1 { } [Container][Singleton<S1>] public partial class MyContainer { } }
		                                       namespace B { public sealed class S2 { } [Container][Singleton<S2>] public partial class MyContainer { } }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources).HasCount(2);
		await That(result.Sources.ContainsKey("Awaiten.A.MyContainer.g.cs")).IsTrue();
		await That(result.Sources.ContainsKey("Awaiten.B.MyContainer.g.cs")).IsTrue();
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
		                                       public partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("private volatile global::MyCode.Service? _service;")
			.Because("a scoped registration is cached per owner");
		await That(source).Contains("Scoped: one instance per owner")
			.Because("the container acts as the root scope");
		await That(source).Contains("private sealed class Scope : global::Awaiten.IAwaitenScope")
			.Because("scoped instances also live on each created scope");
	}

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
		                                       public partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source)
			.Contains("private static readonly global::System.Collections.Generic.Dictionary<global::System.Type, int> __dispatch")
			.Because("resolution is dispatched through a static type-to-case table");
		await That(source).Contains("if (__dispatch.TryGetValue(serviceType, out int __case))")
			.Because("the container dispatches through its own table");
		await That(source).Contains("MyContainer.__dispatch.TryGetValue(serviceType, out int __case)")
			.Because("the nested scope reuses the single table built on the container");
		await That(source.Contains("if (serviceType == typeof(")).IsFalse()
			.Because("the linear if-chain is no longer emitted");
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
		                                       public partial class MyContainer
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
		                                       public partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.Consumer(new global::System.Lazy<global::MyCode.Leaf>(() => ResolveLeaf()))")
			.Because("the Lazy parameter is supplied as a lazy bound to the owner's resolver");
		await That(source).Contains("{ typeof(global::System.Lazy<global::MyCode.Leaf>),")
			.Because("Lazy<T> is also resolvable directly through the dispatch table");
		await That(source).Contains("instance = new global::System.Lazy<global::MyCode.Leaf>(() => ResolveLeaf()); return true;")
			.Because("its dispatch case builds a fresh lazy over the target's resolver");
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
		                                       public partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Registering Lazy<Leaf> directly and registering Leaf (which synthesizes a Lazy<Leaf> entry)
		// would both claim typeof(Lazy<Leaf>); the synthetic entry must be dropped so the static
		// dispatch dictionary has exactly one key for it (a duplicate would throw at runtime).
		int keyCount = source.Split(new[] { "typeof(global::System.Lazy<global::MyCode.Leaf>)", }, System.StringSplitOptions.None).Length - 1;
		await That(keyCount).IsEqualTo(1)
			.Because("the explicit registration and the synthetic relationship must not produce a duplicate dispatch key");
		await That(source.Contains("new global::System.Lazy<global::MyCode.Leaf>(() => ResolveLeaf())")).IsFalse()
			.Because("the synthetic Lazy<Leaf> factory is dropped in favour of the explicit registration's resolver");
	}
}
