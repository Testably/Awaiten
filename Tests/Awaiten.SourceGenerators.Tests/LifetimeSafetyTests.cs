using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated effect of <c>LifetimeSafety</c>. Strict (the default) escalates the root-accumulating
///     factory diagnostic (AWT118) to an error and withholds a disposable transient from by-type resolution
///     (its bare type and plain Func factory throw guidance, and it gets no typed resolver), while keeping
///     <c>Owned&lt;T&gt;</c> resolvable. Loose reports AWT118 as a warning and leaves everything resolvable.
/// </summary>
public class LifetimeSafetyTests
{
	private const string RootAccumulatingSource = """
	                                              using Awaiten;
	                                              using System;

	                                              namespace MyCode;

	                                              public sealed class Tool : IDisposable {{ public void Dispose() {{ }} }}
	                                              public sealed class Depot {{ public Depot(Func<Tool> tools) {{ }} }}

	                                              [Container{0}]
	                                              [Transient<Tool>]
	                                              [Singleton<Depot>]
	                                              public static partial class MyContainer
	                                              {{
	                                              }}
	                                              """;

	// AWT118 is reported by AwaitenAnalyzer (so a loose warning can be #pragma-suppressed), so its severity is
	// asserted through the analyzer harness rather than the generator's diagnostics.
	[Fact]
	public async Task Strict_EscalatesTheRootAccumulatingFactoryToAnError()
	{
		string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(string.Format(RootAccumulatingSource, string.Empty));

		await That(diagnostics.Any(d => d.Contains("error") && d.Contains("AWT118"))).IsTrue()
			.Because("strict lifetime safety (the default) makes the root-accumulating Func a compile-time error");
	}

	[Fact]
	public async Task Loose_ReportsTheRootAccumulatingFactoryAsAWarning()
	{
		string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(
			string.Format(RootAccumulatingSource, "(LifetimeSafety = LifetimeSafety.Loose)"));

		await That(diagnostics.Any(d => d.Contains("warning") && d.Contains("AWT118"))).IsTrue()
			.Because("loose lifetime safety downgrades the root-accumulating Func to a warning");
		await That(diagnostics.Any(d => d.Contains("error") && d.Contains("AWT118"))).IsFalse();
	}

	[Fact]
	public async Task Strict_TheRootAccumulatingFactoryError_CannotBeSuppressedInSource()
	{
		string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Tool : IDisposable { public void Dispose() { } }

		                                       #pragma warning disable AWT118
		                                       public sealed class Depot { public Depot(Func<Tool> tools) { } }
		                                       #pragma warning restore AWT118

		                                       [Container]
		                                       [Transient<Tool>]
		                                       [Singleton<Depot>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(diagnostics.Any(d => d.Contains("error") && d.Contains("AWT118"))).IsTrue()
			.Because("under strict lifetime safety AWT118 is reported through a NotConfigurable descriptor, so #pragma warning disable cannot silence it - the only opt-out is LifetimeSafety.Loose");
	}

	[Fact]
	public async Task Loose_TheRootAccumulatingFactoryWarning_CanBeSuppressedInSource()
	{
		string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Tool : IDisposable { public void Dispose() { } }

		                                       #pragma warning disable AWT118
		                                       public sealed class Depot { public Depot(Func<Tool> tools) { } }
		                                       #pragma warning restore AWT118

		                                       [Container(LifetimeSafety = LifetimeSafety.Loose)]
		                                       [Transient<Tool>]
		                                       [Singleton<Depot>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(diagnostics.Any(d => d.Contains("AWT118"))).IsFalse()
			.Because("a loose AWT118 warning is reported by an analyzer, so #pragma warning disable suppresses it in source");
	}

	[Fact]
	public async Task Strict_RootAccumulatingContainer_StillGeneratesCompilableCode()
	{
		// AWT118 is now an analyzer (not generator) error, so the generator no longer replaces the container
		// with a throwing error-body for this pattern - it emits the real (withholding) container. The build
		// still fails on the analyzer error, but the generated code itself must compile.
		GeneratorResult result = Generator.Run(string.Format(RootAccumulatingSource, string.Empty));

		await That(result.Diagnostics.Where(d => d.Contains("error"))).IsEmpty()
			.Because("the generated withholding container is valid C#; AWT118 is reported by the analyzer, not as a codegen error");
		await That(result.Sources.Keys.Any(k => k.Contains("MyContainer"))).IsTrue();
	}

	[Fact]
	public async Task Strict_WithholdsADisposableTransientFromByTypeResolution()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Widget : IDisposable { public void Dispose() { } }

		                                       [Container]
		                                       [Transient<Widget>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Where(d => d.Contains("error"))).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).DoesNotContain("global::Awaiten.IAwaitenResolver<global::MyCode.Widget>")
			.Because("a withheld disposable transient gets no typed resolver");
		await That(source).Contains("withheld from by-type resolution on the container root under strict lifetime safety")
			.Because("resolving the withheld type by type off the root throws a guidance exception");
		await That(source).Contains("typeof(global::Awaiten.Owned<global::MyCode.Widget>)")
			.Because("Owned<Widget> stays resolvable as the leak-free alternative");
		await That(source).Contains("static __s => __s.ResolveWidget()")
			.Because("under variant C the bare type is still dispatchable - so a child scope can resolve it");
		await That(source).Contains("__b.RootWithheld && this is Root")
			.Because("the Root withholds the slot through its per-slot flag, while a child scope resolves it");
	}

	[Fact]
	public async Task Strict_WithholdsThePlainFuncOverATransitivelyDisposableService_ButKeepsTheBareTypeResolvable()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Spark : IDisposable { public void Dispose() { } }
		                                       public sealed class Tool { public Tool(Spark spark) { } }

		                                       [Container]
		                                       [Transient<Spark>]
		                                       [Transient<Tool>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Where(d => d.Contains("error"))).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("a plain Func over 'MyCode.Tool' is withheld")
			.Because("building the non-disposable Tool on demand transitively rebuilds its disposable Spark, so its plain Func accumulates on the root and is withheld there");
		await That(source).Contains("static __s => __s.ResolveTool()")
			.Because("the bare non-disposable Tool stays resolvable by type - a single resolution is bounded");
		await That(source).Contains("() => new global::System.Func<global::MyCode.Tool>(() => ResolveTool());")
			.Because("under variant C the plain Func is dispatchable too - the Root flag withholds it, but a child scope resolves it");
		await That(source).Contains("typeof(global::System.Func<global::Awaiten.Owned<global::MyCode.Tool>>)")
			.Because("Func<Owned<Tool>> remains the leak-free factory that drains the transitive Spark with the handle");
	}

	[Fact]
	public async Task Loose_KeepsADisposableTransientFullyResolvable()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Widget : IDisposable { public void Dispose() { } }

		                                       [Container(LifetimeSafety = LifetimeSafety.Loose)]
		                                       [Transient<Widget>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Where(d => d.Contains("error"))).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::Awaiten.IAwaitenResolver<global::MyCode.Widget>")
			.Because("loose keeps the typed resolver");
		await That(source).DoesNotContain("withheld from by-type resolution")
			.Because("nothing is withheld under loose lifetime safety");
	}
}
