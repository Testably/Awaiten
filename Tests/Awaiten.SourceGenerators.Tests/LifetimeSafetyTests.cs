using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated effect of <c>LifetimeSafety</c>. Strict (the default) escalates the root-accumulating
///     factory diagnostic (AWT117) to an error and withholds a disposable transient from by-type resolution
///     (its bare type and plain Func factory throw guidance, and it gets no typed resolver), while keeping
///     <c>Owned&lt;T&gt;</c> resolvable. Loose reports AWT117 as a warning and leaves everything resolvable.
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

	[Fact]
	public async Task Strict_EscalatesTheRootAccumulatingFactoryToAnError()
	{
		GeneratorResult result = Generator.Run(string.Format(RootAccumulatingSource, string.Empty));

		await That(result.Diagnostics.Any(d => d.Contains("error") && d.Contains("AWT117"))).IsTrue()
			.Because("strict lifetime safety (the default) makes the root-accumulating Func a compile-time error");
	}

	[Fact]
	public async Task Loose_ReportsTheRootAccumulatingFactoryAsAWarning()
	{
		GeneratorResult result = Generator.Run(string.Format(RootAccumulatingSource, "(LifetimeSafety = LifetimeSafety.Loose)"));

		await That(result.Diagnostics.Any(d => d.Contains("warning") && d.Contains("AWT117"))).IsTrue()
			.Because("loose lifetime safety downgrades the root-accumulating Func to a warning");
		await That(result.Diagnostics.Any(d => d.Contains("error") && d.Contains("AWT117"))).IsFalse();
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
		await That(source).Contains("withheld from by-type resolution under strict lifetime safety")
			.Because("resolving the withheld type by type throws a guidance exception");
		await That(source).Contains("typeof(global::Awaiten.Owned<global::MyCode.Widget>)")
			.Because("Owned<Widget> stays resolvable as the leak-free alternative");
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
