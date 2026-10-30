using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	private const string DispatchTypes = """
	                                     using Awaiten;
	                                     using System;
	                                     using System.Collections.Generic;
	                                     using System.Threading;
	                                     using System.Threading.Tasks;

	                                     namespace MyCode;

	                                     public interface IHandler { object Handle(object message); }
	                                     public interface IHandler<T> : IHandler { }
	                                     public interface IQueryHandler<T> : IHandler { }
	                                     public interface IRequestHandler<TRequest, TResponse> : IHandler { }
	                                     public sealed class Placed { }
	                                     public sealed class Shipped { }
	                                     public sealed class PlacedHandler : IHandler<Placed> { public object Handle(object message) => message; }
	                                     public sealed class ShippedHandler : IHandler<Shipped> { public object Handle(object message) => message; }
	                                     public sealed class ShippedQuery : IQueryHandler<Shipped> { public object Handle(object message) => message; }
	                                     public sealed class PlacedRequest : IRequestHandler<Placed, Shipped> { public object Handle(object message) => message; }

	                                     """;

	public class Awt213UnsupportedTypeDispatch
	{
		[Test]
		public async Task ReportsWhenTheValueIsNotAFunc()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, IHandler> handlers) { } }

			                                                       [Container]
			                                                       [Singleton<PlacedHandler, IHandler<Placed>>]
			                                                       [Singleton<Mediator>]
			                                                       public static partial class MyContainer
			                                                       {
			                                                       }
			                                                       """);

			await That(result.Diagnostics).Contains("*AWT213*Func<TBase>*").AsWildcard()
				.Because("each value of a dispatch dictionary is a Func over the base interface");
			await That(result.Diagnostics).DoesNotContain("*AWT159*").AsWildcard()
				.Because("a Type-keyed dictionary is a dispatch dictionary, not a keyed dictionary with an unsupported key");
		}

		[Test]
		public async Task ReportsWhenTheValueIsAnAsyncFunc()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, Func<Task<IHandler>>> handlers) { } }

			                                                       [Container]
			                                                       [Singleton<PlacedHandler, IHandler<Placed>>]
			                                                       [Singleton<Mediator>]
			                                                       public static partial class MyContainer
			                                                       {
			                                                       }
			                                                       """);

			await That(result.Diagnostics).Contains("*AWT213*Func<TBase>*").AsWildcard()
				.Because("Func<Task<TBase>> values are not supported");
		}

		[Test]
		public async Task ReportsWhenTheBaseIsAGenericInterface()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, Func<IHandler<Placed>>> handlers) { } }

			                                                       [Container]
			                                                       [Singleton<PlacedHandler, IHandler<Placed>>]
			                                                       [Singleton<Mediator>]
			                                                       public static partial class MyContainer
			                                                       {
			                                                       }
			                                                       """);

			await That(result.Diagnostics).Contains("*AWT213*non-generic interface*").AsWildcard()
				.Because("the base of a dispatch dictionary must be a non-generic interface");
		}

		[Test]
		public async Task ReportsAFromKey()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class Mediator { public Mediator([FromKey("a")] IReadOnlyDictionary<Type, Func<IHandler>> handlers) { } }

			                                                       [Container]
			                                                       [Singleton<PlacedHandler, IHandler<Placed>>]
			                                                       [Singleton<Mediator>]
			                                                       public static partial class MyContainer
			                                                       {
			                                                       }
			                                                       """);

			await That(result.Diagnostics).Contains("*AWT213*FromKey*").AsWildcard()
				.Because("the dispatch dictionary holds every unkeyed closing, so a key cannot select it");
		}

		[Test]
		public async Task ReportsAClosingWithMoreThanOneTypeArgument()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, Func<IHandler>> handlers) { } }

			                                                       [Container]
			                                                       [Singleton<PlacedRequest, IRequestHandler<Placed, Shipped>>]
			                                                       [Singleton<Mediator>]
			                                                       public static partial class MyContainer
			                                                       {
			                                                       }
			                                                       """);

			await That(result.Diagnostics).Contains("*AWT213*single type argument*").AsWildcard()
				.Because("the dictionary is keyed by one type argument, which a two-parameter closing does not have");
		}

		[Test]
		public async Task ReportsClosingsOfTwoGenericDefinitions()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, Func<IHandler>> handlers) { } }

			                                                       [Container]
			                                                       [Singleton<ShippedHandler, IHandler<Shipped>>]
			                                                       [Singleton<ShippedQuery, IQueryHandler<Shipped>>]
			                                                       [Singleton<Mediator>]
			                                                       public static partial class MyContainer
			                                                       {
			                                                       }
			                                                       """);

			await That(result.Diagnostics).Contains("*AWT213*ambiguous*IHandler<T>*IQueryHandler<T>*").AsWildcard()
				.Because("a type argument does not identify one closing when two generic interfaces share the base");
		}

		[Test]
		public async Task DoesNotReportAWellFormedDispatchDictionary()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, Func<IHandler>> handlers) { } }

			                                                       [Container]
			                                                       [Scan(typeof(IHandler<>), As = ScanAs.Marker)]
			                                                       [Singleton<Mediator>]
			                                                       public static partial class MyContainer
			                                                       {
			                                                       }
			                                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("every scanned closing of IHandler<> has a single type argument and the same definition");
			await That(result.Sources.Values.Single(source => source.Contains("class MyContainer")))
				.Contains("[typeof(global::MyCode.Placed)] = new global::System.Func<global::MyCode.IHandler<global::MyCode.Placed>>(")
				.Because("each entry is keyed by the type argument and holds the closing's Func");
		}

		[Test]
		public async Task DoesNotReportAnEmptyFamily()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, Func<IHandler>> handlers) { } }

			                                                       [Container]
			                                                       [Singleton<Mediator>]
			                                                       public static partial class MyContainer
			                                                       {
			                                                       }
			                                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a dispatch dictionary with no closings is empty, like a collection, rather than a missing dependency");
		}

		[Test]
		public async Task DoesNotReportAnExplicitlyRegisteredDictionary()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class HandlerMap : Dictionary<Type, IHandler> { }
			                                                       public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, IHandler> handlers) { } }

			                                                       [Container]
			                                                       [Singleton<HandlerMap, IReadOnlyDictionary<Type, IHandler>>]
			                                                       [Singleton<Mediator>]
			                                                       public static partial class MyContainer
			                                                       {
			                                                       }
			                                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a registration of the exact dictionary type preempts synthesis");
		}

		[Test]
		public async Task AnAsyncInitializedEntry_IsReportedLikeAFunc()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class AsyncHandler : IHandler<Placed>, IAsyncInitializable
			                                                       {
			                                                           public object Handle(object message) => message;
			                                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                                       }
			                                                       public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, Func<IHandler>> handlers) { } }

			                                                       [Container]
			                                                       [Singleton<AsyncHandler, IHandler<Placed>>]
			                                                       [Singleton<Mediator>]
			                                                       public static partial class MyContainer
			                                                       {
			                                                       }
			                                                       """);

			await That(result.Diagnostics).Contains("*AWT119*AsyncHandler*").AsWildcard()
				.Because("each entry is a synchronous Func, which cannot await an async-initialized handler");
		}

		[Test]
		public async Task AParameterizedEntry_IsReportedLikeAFunc()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class ArgHandler : IHandler<Placed> { public ArgHandler([Arg] int id) { } public object Handle(object message) => message; }
			                                                       public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, Func<IHandler>> handlers) { } }

			                                                       [Container]
			                                                       [Transient<ArgHandler, IHandler<Placed>>]
			                                                       [Singleton<Mediator>]
			                                                       public static partial class MyContainer
			                                                       {
			                                                       }
			                                                       """);

			await That(result.Diagnostics).Contains("*AWT113*").AsWildcard()
				.Because("an entry's Func supplies no runtime arguments, which a parameterized handler needs");
		}

		[Test]
		public async Task OnAReleaseHook_IsReportedLikeAFunc()
		{
			GeneratorResult result = Generator.Run(DispatchTypes + """
			                                                       public sealed class Mediator { }

			                                                       [Container]
			                                                       [Singleton<PlacedHandler, IHandler<Placed>>]
			                                                       [Singleton<Mediator>(OnRelease = nameof(Release))]
			                                                       public static partial class MyContainer
			                                                       {
			                                                           private static void Release(Mediator mediator, IReadOnlyDictionary<Type, Func<IHandler>> handlers) { }
			                                                       }
			                                                       """);

			await That(result.Diagnostics).Contains("*AWT191*").AsWildcard()
				.Because("the dictionary's Funcs are dead by the time a release hook runs");
		}

		[Test]
		public async Task ASingletonDispatchingToADisposableTransient_IsAWT118()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(DispatchTypes + """
			                                                                           public sealed class Tool : IHandler<Placed>, IDisposable { public object Handle(object message) => message; public void Dispose() { } }
			                                                                           public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, Func<IHandler>> handlers) { } }

			                                                                           [Container]
			                                                                           [Transient<Tool, IHandler<Placed>>]
			                                                                           [Singleton<Mediator>]
			                                                                           public static partial class MyContainer
			                                                                           {
			                                                                           }
			                                                                           """);

			await That(diagnostics.Any(d => d.Contains("AWT118") && d.Contains("as Scoped"))).IsTrue()
				.Because("each call of a root-held entry strands a disposable transient on the root, and a scoped mediator bounds it");
		}

		[Test]
		public async Task AScopedDispatchingToADisposableTransient_IsNotAWT118()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(DispatchTypes + """
			                                                                           public sealed class Tool : IHandler<Placed>, IDisposable { public object Handle(object message) => message; public void Dispose() { } }
			                                                                           public sealed class Mediator { public Mediator(IReadOnlyDictionary<Type, Func<IHandler>> handlers) { } }

			                                                                           [Container]
			                                                                           [Transient<Tool, IHandler<Placed>>]
			                                                                           [Scoped<Mediator>]
			                                                                           public static partial class MyContainer
			                                                                           {
			                                                                           }
			                                                                           """);

			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsFalse()
				.Because("a scoped mediator's entries track what they build on its scope, which disposes it");
		}
	}
}
