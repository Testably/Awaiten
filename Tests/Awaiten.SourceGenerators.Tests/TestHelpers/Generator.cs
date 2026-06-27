using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Awaiten.SourceGenerators.Tests.TestHelpers;

/// <summary>
///     Drives the <see cref="AwaitenGenerator" /> over an in-memory compilation and returns the
///     generated sources plus any diagnostics, for snapshot/behavior testing.
/// </summary>
public static class Generator
{
	private static readonly string[] NoWarn =
	[
		"CS8019", /* Unnecessary using directive. */
	];

	public static GeneratorResult Run([StringSyntax("c#-test")] string source, params Type[] assemblyTypes)
	{
		AwaitenGenerator generator = new();
		CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
		SyntaxTree[] syntaxTrees = [CSharpSyntaxTree.ParseText(source, parseOptions),];

		CSharpCompilation compilation = CSharpCompilation.Create(
			"TestAssembly",
			syntaxTrees,
			References.For(assemblyTypes),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			[generator.AsSourceGenerator(),],
			[],
			parseOptions,
			null);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation,
			out ImmutableArray<Diagnostic> diagnostics);

		ImmutableArray<Diagnostic> compilationDiagnostics = outputCompilation.GetDiagnostics();
		GeneratorDriverRunResult runResult = driver.GetRunResult();
		Dictionary<string, string> generatedSources = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.ToDictionary(s => s.HintName, s => s.SourceText.ToString());
		string[] diagnosticMessages =
		[
			..compilationDiagnostics.Where(x => !NoWarn.Contains(x.Id)).Select(x => x.ToString()),
			..diagnostics.Where(x => !NoWarn.Contains(x.Id)).Select(x => x.ToString()),
		];
		return new GeneratorResult(generatedSources, diagnosticMessages);
	}
}
