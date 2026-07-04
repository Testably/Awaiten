using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Awaiten.SourceGenerators.Tests.TestHelpers;

/// <summary>
///     Runs a <see cref="DiagnosticAnalyzer" /> over an in-memory compilation and returns its
///     diagnostics, for behavior testing of the Awaiten analyzers. The <see cref="AwaitenGenerator" />
///     runs first - like in a real build, where analyzers see the post-generation compilation - so a
///     usage-site diagnostic (AWT156) can resolve the generated <c>Root</c>/<c>Scope</c> types.
/// </summary>
public static class Analyzer
{
	public static async Task<string[]> Run<TAnalyzer>(
		[StringSyntax("c#-test")] string source,
		params Type[] assemblyTypes)
		where TAnalyzer : DiagnosticAnalyzer, new()
	{
		CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
		SyntaxTree[] syntaxTrees = [CSharpSyntaxTree.ParseText(source, parseOptions),];

		CSharpCompilation compilation = CSharpCompilation.Create(
			"TestAssembly",
			syntaxTrees,
			References.For(assemblyTypes),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			[new AwaitenGenerator().AsSourceGenerator(),],
			[],
			parseOptions,
			null);
		driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);

		CompilationWithAnalyzers withAnalyzers = outputCompilation.WithAnalyzers(
			ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer()));
		ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

		// GetAnalyzerDiagnosticsAsync returns in-source-suppressed diagnostics too (with IsSuppressed
		// set); drop them so the result mirrors what a real build reports after #pragma/[SuppressMessage].
		return diagnostics.Where(d => !d.IsSuppressed).Select(d => d.ToString()).ToArray();
	}
}
