using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Awaiten.SourceGenerators.Tests.TestHelpers;

/// <summary>
///     Runs a <see cref="DiagnosticAnalyzer" /> over an in-memory compilation and returns its diagnostics.
///     The <see cref="AwaitenGenerator" /> runs first, as in a real build, so a usage-site diagnostic (AWT156)
///     can resolve the generated <c>Root</c>/<c>Scope</c> types.
/// </summary>
public static class Analyzer
{
	public static Task<string[]> Run<TAnalyzer>(
		[StringSyntax("c#-test")] string source,
		params Type[] assemblyTypes)
		where TAnalyzer : DiagnosticAnalyzer, new()
		=> Run<TAnalyzer>(source, additionalReferences: [], assemblyTypes);

	/// <summary>
	///     Compiles <paramref name="referencedSource" /> into a separate referenced assembly (with the
	///     <see cref="AwaitenGenerator" /> applied, so a <c>[Container]</c> there carries its generated
	///     <c>Root</c>/<c>Scope</c> into the metadata reference), then runs the analyzer over
	///     <paramref name="source" />. For cross-assembly scenarios such as disposing another project's container.
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

		// A snippet that fails to compile would pass negative assertions vacuously, so fail loudly instead.
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

		// GetAnalyzerDiagnosticsAsync includes in-source-suppressed diagnostics; drop them to mirror a real build.
		return diagnostics.Where(d => !d.IsSuppressed).Select(d => d.ToString()).ToArray();
	}
}
