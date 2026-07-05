using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The open generic <c>typeof</c> forms of <c>[Decorate]</c> and <c>[Composite]</c>: a closed decorator/composite
///     is synthesized per closing of the service present in the graph and flows through the same chain/composite
///     builders as the closed forms. The forms reuse the open generic diagnostics: AWT127 (not unbound), AWT125
///     (arity) and AWT126 (a closing's type arguments violate the decorator's/composite's constraints); an open
///     decorator that matches no closing reports AWT123.
/// </summary>
public class OpenGenericDecoratorTests
{
	[Fact]
	public async Task OpenDecorator_SynthesizesAClosedDecoratorPerClosing()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public sealed class Customer { }
		                                       public interface IHandler<T> { }
		                                       public sealed class Handler<T> : IHandler<T> { }
		                                       public sealed class Logging<T> : IHandler<T> { public Logging(IHandler<T> inner) { } }
		                                       public sealed class Root { public Root(IHandler<Order> a, IHandler<Customer> b) { } }

		                                       [Container]
		                                       [Transient(typeof(Handler<>), typeof(IHandler<>))]
		                                       [Transient<Root>]
		                                       [Decorate(typeof(Logging<>), typeof(IHandler<>))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// A closed decorator is synthesized for each closing the root needs, wrapping the matching handler.
		await That(source).Contains("new global::MyCode.Logging<global::MyCode.Order>(")
			.Because("the Order closing is wrapped by Logging<Order>");
		await That(source).Contains("new global::MyCode.Logging<global::MyCode.Customer>(")
			.Because("the Customer closing is wrapped by Logging<Customer>");
	}

	[Fact]
	public async Task OpenDecorator_NotUnbound_ReportsAwt127()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IHandler<T> { }
		                                       public sealed class Handler<T> : IHandler<T> { }
		                                       public sealed class Logging<T> : IHandler<T> { public Logging(IHandler<T> inner) { } }

		                                       [Container]
		                                       [Transient(typeof(Handler<>), typeof(IHandler<>))]
		                                       [Decorate(typeof(Logging<int>), typeof(IHandler<>))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Any(d => d.Contains("AWT127"))).IsTrue()
			.Because("the typeof form must receive an unbound generic decorator such as typeof(Logging<>)");
	}

	[Fact]
	public async Task OpenDecorator_ArityMismatch_ReportsAwt125()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IHandler<T> { }
		                                       public sealed class Handler<T> : IHandler<T> { }
		                                       // Two type parameters for a one-parameter service: no closing can be mapped.
		                                       public sealed class Wrong<TA, TB> { }

		                                       [Container]
		                                       [Transient(typeof(Handler<>), typeof(IHandler<>))]
		                                       [Decorate(typeof(Wrong<,>), typeof(IHandler<>))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Any(d => d.Contains("AWT125"))).IsTrue()
			.Because("the open decorator's arity must match the service's");
	}

	[Fact]
	public async Task OpenDecorator_ClosingViolatesConstraints_ReportsAwt126()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IHandler<T> { }
		                                       public sealed class Handler<T> : IHandler<T> { }
		                                       // The decorator constrains T to a reference type, but the closing is IHandler<int>.
		                                       public sealed class Logging<T> : IHandler<T> where T : class { public Logging(IHandler<T> inner) { } }
		                                       public sealed class Root { public Root(IHandler<int> handler) { } }

		                                       [Container]
		                                       [Transient(typeof(Handler<>), typeof(IHandler<>))]
		                                       [Transient<Root>]
		                                       [Decorate(typeof(Logging<>), typeof(IHandler<>))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Any(d => d.Contains("AWT126"))).IsTrue()
			.Because("the closing IHandler<int> cannot construct Logging<int> under 'where T : class'");
	}

	[Fact]
	public async Task OpenDecorator_WithNoClosingToDecorate_ReportsAwt123()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IHandler<T> { }
		                                       public sealed class Handler<T> : IHandler<T> { }
		                                       public sealed class Logging<T> : IHandler<T> { public Logging(IHandler<T> inner) { } }

		                                       [Container]
		                                       [Transient(typeof(Handler<>), typeof(IHandler<>))]
		                                       [Decorate(typeof(Logging<>), typeof(IHandler<>))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// Nothing requires a closed IHandler<…>, so no closing is expanded and there is nothing to decorate.
		await That(result.Diagnostics.Any(d => d.Contains("AWT123"))).IsTrue()
			.Because("the open decorator matches no closing of the service");
	}

	[Fact]
	public async Task OpenComposite_SynthesizesAClosedCompositePerClosing()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IHandler<T> { }
		                                       public sealed class HandlerA<T> : IHandler<T> { }
		                                       public sealed class HandlerB<T> : IHandler<T> { }
		                                       public sealed class CompositeHandler<T> : IHandler<T> { public CompositeHandler(IEnumerable<IHandler<T>> inner) { } }
		                                       public sealed class Root { public Root(IHandler<Order> handler) { } }

		                                       [Container]
		                                       [Transient(typeof(HandlerA<>), typeof(IHandler<>))]
		                                       [Transient(typeof(HandlerB<>), typeof(IHandler<>))]
		                                       [Transient<Root>]
		                                       [Composite(typeof(CompositeHandler<>), typeof(IHandler<>))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("static __s => ResolveCompositeHandler(__s)")
			.Because("the closed composite is the public single-dispatch winner for IHandler<Order>");
		await That(source).Contains("new global::MyCode.CompositeHandler<global::MyCode.Order>(")
			.Because("the Order closing is fronted by CompositeHandler<Order>");
	}

	[Fact]
	public async Task OpenComposite_NotUnbound_ReportsAwt127()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IHandler<T> { }
		                                       public sealed class Handler<T> : IHandler<T> { }
		                                       public sealed class CompositeHandler<T> : IHandler<T> { public CompositeHandler(IEnumerable<IHandler<T>> inner) { } }

		                                       [Container]
		                                       [Transient(typeof(Handler<>), typeof(IHandler<>))]
		                                       [Composite(typeof(CompositeHandler<int>), typeof(IHandler<>))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Any(d => d.Contains("AWT127"))).IsTrue()
			.Because("the typeof form must receive an unbound generic composite such as typeof(CompositeHandler<>)");
	}
}
