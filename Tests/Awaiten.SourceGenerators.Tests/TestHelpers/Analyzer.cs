using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
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
	public static Task<string[]> Run<TAnalyzer>(
		[StringSyntax("c#-test")] string source,
		params Type[] assemblyTypes)
		where TAnalyzer : DiagnosticAnalyzer, new()
		=> Run<TAnalyzer>(source, additionalReferences: [], assemblyTypes);

	/// <summary>
	///     Runs the analyzer over <paramref name="source" /> with <paramref name="referencedSource" />
	///     compiled into a separate referenced assembly first - with the <see cref="AwaitenGenerator" />
	///     applied to it, so a <c>[Container]</c> declared there carries its generated
	///     <c>Root</c>/<c>Scope</c> into the metadata reference - for cross-assembly scenarios such as
	///     disposing another project's container.
	/// </summary>
	public static Task<string[]> RunWithReferencedAssembly<TAnalyzer>(
		[StringSyntax("c#-test")] string referencedSource,
		[StringSyntax("c#-test")] string source)
		where TAnalyzer : DiagnosticAnalyzer, new()
	{
		(Compilation referenced, _) = Generator.RunGenerator(referencedSource, [], [], "ReferencedAssembly");
		return Run<TAnalyzer>(source, [Generator.EmitToReference(referenced),], []);
	}

	private static async Task<string[]> Run<TAnalyzer>(
		[StringSyntax("c#-test")] string source,
		MetadataReference[] additionalReferences,
		Type[] assemblyTypes)
		where TAnalyzer : DiagnosticAnalyzer, new()
	{
		(Compilation outputCompilation, GeneratorDriverRunResult generatorResult) =
			Generator.RunGenerator(source, additionalReferences, assemblyTypes);

		// A snippet that does not compile (or trips the generator) would otherwise pass a negative
		// assertion vacuously, so a generator or compilation error fails the test run loudly instead.
		string[] errors = generatorResult.Diagnostics
			.Concat(outputCompilation.GetDiagnostics())
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.Select(d => d.ToString())
			.ToArray();
		if (errors.Length > 0)
		{
			throw new InvalidOperationException(
				"The analyzer test source does not compile:"
				+ Environment.NewLine + string.Join(Environment.NewLine, errors));
		}

		CompilationWithAnalyzers withAnalyzers = outputCompilation.WithAnalyzers(
			ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer()));
		ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

		// GetAnalyzerDiagnosticsAsync returns in-source-suppressed diagnostics too (with IsSuppressed
		// set); drop them so the result mirrors what a real build reports after #pragma/[SuppressMessage].
		return diagnostics.Where(d => !d.IsSuppressed).Select(d => d.ToString()).ToArray();
	}
}
