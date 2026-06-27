using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public class GeneralTests
{
	[Fact]
	public async Task DependencyCycle_ReportsAwt102WithThePath()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class A { public A(B b) { } }
		                                       public sealed class B { public B(A a) { } }

		                                       [Container]
		                                       [Singleton<A>]
		                                       [Singleton<B>]
		                                       public partial class MyContainer
		                                       {
		                                       }
		                                       """);

		string[] cycleDiagnostics = result.Diagnostics.Where(d => d.Contains("AWT102")).ToArray();
		await That(cycleDiagnostics).IsNotEmpty();
		await That(cycleDiagnostics.Any(d => d.Contains("->"))).IsTrue();
	}

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

		await That(source).Contains("if (serviceType == typeof(global::MyCode.IMiddle)) { instance = ResolveMiddle(); return true; }")
			.Because("services are dispatched by their service type");
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
	public async Task MissingDependency_ReportsAwt101()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IMissing { }
		                                       public sealed class Service { public Service(IMissing missing) { } }

		                                       [Container]
		                                       [Transient<Service>]
		                                       public partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Any(d => d.Contains("AWT101"))).IsTrue();
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
	public async Task NoAccessibleConstructor_ReportsAwt104()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Foo { private Foo() { } }

		                                       [Container]
		                                       [Singleton<Foo>]
		                                       public partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Any(d => d.Contains("AWT104"))).IsTrue();
	}

	[Theory]
	[InlineData("public interface Foo { }")]
	[InlineData("public abstract class Foo { }")]
	public async Task NotInstantiableImplementation_ReportsAwt103(string implementationDeclaration)
	{
		GeneratorResult result = Generator.Run($$"""
		                                         using Awaiten;

		                                         namespace MyCode;

		                                         {{implementationDeclaration}}

		                                         [Container]
		                                         [Singleton<Foo>]
		                                         public partial class MyContainer
		                                         {
		                                         }
		                                         """);

		await That(result.Diagnostics.Any(d => d.Contains("AWT103"))).IsTrue();
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
		await That(source).Contains("if (serviceType == typeof(global::MyCode.IReader)) { instance = ResolveStore(); return true; }")
			.Because("both service types dispatch to the one shared instance");
		await That(source).Contains("if (serviceType == typeof(global::MyCode.IWriter)) { instance = ResolveStore(); return true; }")
			.Because("both service types dispatch to the one shared instance");
	}

	[Fact]
	public async Task SameLifetimeMultiService_DoesNotReportAwt107()
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

		await That(result.Diagnostics.Any(d => d.Contains("AWT107"))).IsFalse()
			.Because("registering one implementation under several services with the same lifetime is valid");
	}

	[Fact]
	public async Task ConflictingLifetime_ReportsAwt107()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public interface IReader { }
			public interface IWriter { }
			public sealed class Store : IReader, IWriter { }

			[Container]
			[Singleton<Store, IReader>]
			[Scoped<Store, IWriter>]
			public partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics.Any(d => d.Contains("AWT107"))).IsTrue();
	}

	[Fact]
	public async Task DisposableTransient_ReportsAwt106()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using System;

			namespace MyCode;

			public sealed class Resource : IDisposable { public void Dispose() { } }

			[Container]
			[Transient<Resource>]
			public partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics.Any(d => d.Contains("AWT106"))).IsTrue();
	}

	[Fact]
	public async Task SingletonCapturingScoped_ReportsAwt105()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public sealed class ScopedDependency { }
			public sealed class SingletonConsumer { public SingletonConsumer(ScopedDependency dependency) { } }

			[Container]
			[Singleton<SingletonConsumer>]
			[Scoped<ScopedDependency>]
			public partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics.Any(d => d.Contains("AWT105"))).IsTrue();
	}

	[Fact]
	public async Task SingletonCapturingScopedThroughTransient_ReportsAwt105()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public sealed class ScopedDependency { }
			public sealed class TransientMiddle { public TransientMiddle(ScopedDependency dependency) { } }
			public sealed class SingletonConsumer { public SingletonConsumer(TransientMiddle middle) { } }

			[Container]
			[Singleton<SingletonConsumer>]
			[Transient<TransientMiddle>]
			[Scoped<ScopedDependency>]
			public partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics.Any(d => d.Contains("AWT105"))).IsTrue();
	}
}
