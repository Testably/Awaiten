using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated shape of decorators: <c>[Decorate&lt;D, IService&gt;]</c> is model-building over the
///     existing keyed-resolution plumbing — each chain link is a synthetic-keyed instance whose inner
///     parameter resolves the link below it, and the public dispatch and every consumer reach the outermost
///     decorator. The collection view is rewritten to the decorated chain, so the decorator is unbypassable.
/// </summary>
public class DecoratorTests
{
	[Fact]
	public async Task Decorator_EmitsTheChainedDispatchOutermostFirst()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IService { }
		                                       public sealed class Real : IService { }
		                                       public sealed class LoggingDecorator : IService { public LoggingDecorator(IService inner) { } }
		                                       public sealed class D2 : IService { public D2(IService inner) { } }
		                                       public sealed class Consumer { public Consumer(IService service) { } }

		                                       [Container]
		                                       [Transient<Real, IService>]
		                                       [Transient<Consumer>]
		                                       [Decorate<LoggingDecorator, IService>]
		                                       [Decorate<D2, IService>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The chain is built from synthetic-keyed links: D2(LoggingDecorator(Real)). Each link constructs the
		// link below it, so the outermost decorator's resolver constructs the inner one, down to the bare Real.
		await That(source).Contains("new global::MyCode.D2(ResolveLoggingDecorator())")
			.Because("the outermost decorator wraps the next-lower link");
		await That(source).Contains("new global::MyCode.LoggingDecorator(ResolveReal())")
			.Because("the inner decorator wraps the bare implementation");

		// The public IService dispatch and every consumer reach the outermost decorator, never the bare Real.
		await That(source).Contains("static __s => __s.ResolveD2()")
			.Because("the public IService dispatch resolves the outermost decorator");
		await That(source).Contains("new global::MyCode.Consumer(ResolveD2())")
			.Because("a consumer of IService receives the outermost decorator");

		// The collection view is decorated too — one element, the full chain, never the bare Real.
		await That(source).Contains("new global::MyCode.IService[] { ResolveD2() }")
			.Because("the collection view yields the decorated chain, so the decorator is unbypassable");
		await That(source).DoesNotContain("static __s => __s.ResolveReal()")
			.Because("the bare implementation is no longer publicly dispatched — only the decorator chain is");
	}

	[Fact]
	public async Task Decorator_WrapsEachRegistrationOfAMultiplyRegisteredService()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IService { }
		                                       public sealed class Real1 : IService { }
		                                       public sealed class Real2 : IService { }
		                                       public sealed class LoggingDecorator : IService { public LoggingDecorator(IService inner) { } }
		                                       public sealed class Host { public Host(IEnumerable<IService> services) { } }

		                                       [Container]
		                                       [Transient<Real1, IService>]
		                                       [Transient<Real2, IService>]
		                                       [Transient<Host>]
		                                       [Decorate<LoggingDecorator, IService>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// A service with several registrations is decorated member by member: the collection materializes one
		// distinct LoggingDecorator instance per base impl, so the decorator type is emitted twice.
		int decoratorConstructions = source.Split(new[] { "new global::MyCode.LoggingDecorator(", }, System.StringSplitOptions.None).Length - 1;
		await That(decoratorConstructions).IsEqualTo(2)
			.Because("each of the two registrations gets its own decorator instance");
		await That(source).Contains("new global::MyCode.LoggingDecorator(ResolveReal1())");
		await That(source).Contains("new global::MyCode.LoggingDecorator(ResolveReal2())");
	}
}
