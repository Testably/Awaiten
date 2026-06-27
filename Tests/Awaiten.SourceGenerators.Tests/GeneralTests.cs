using System.Linq;

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
		await That(source).Contains("public T Get<T>() => (T)Resolve(typeof(T));");
	}

	[Fact]
	public async Task Graph_EmitsSingletonCachingAndTransientConstruction()
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

		// Singletons are cached in fields and memoized; transients are not.
		await That(source).Contains("private global::MyCode.Leaf? __instance_0;");
		await That(source).Contains("private global::MyCode.Middle? __instance_1;");
		await That(source).Contains("__instance_0 ??= new global::MyCode.Leaf()");
		await That(source).Contains("private global::MyCode.Top Resolve_2() => new global::MyCode.Top(Resolve_1(), Resolve_0());");

		// Service types are dispatched by type.
		await That(source).Contains("if (serviceType == typeof(global::MyCode.IMiddle)) { instance = Resolve_1(); return true; }");
	}

	[Fact]
	public async Task ScopedRegistration_IsResolvedAsSingletonForNow()
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
		await That(source).Contains("TODO Phase 2");
		await That(source).Contains("__instance_0 ??= new global::MyCode.Service()");
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
		await That(source).Contains("__instance_0 ??= new global::MyCode.Outer.Service()");
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
	public async Task AbstractImplementation_ReportsAwt103()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public interface IFoo { }

			[Container]
			[Singleton<IFoo>]
			public partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics.Any(d => d.Contains("AWT103"))).IsTrue();
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
		await That(source).Contains("__instance_0 ??= new global::MyCode.Foo()");
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
}
