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
		=> Run(source, additionalReferences: [], assemblyTypes);

	/// <summary>
	///     Runs the generator over <paramref name="source" /> with <paramref name="referencedSource" />
	///     compiled into a separate referenced assembly first - for cross-assembly scenarios such as a
	///     <c>[Module]</c> living in another project.
	/// </summary>
	public static GeneratorResult RunWithReferencedAssembly(
		[StringSyntax("c#-test")] string referencedSource,
		[StringSyntax("c#-test")] string source)
	{
		CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
		CSharpCompilation referenced = CSharpCompilation.Create(
			"ReferencedAssembly",
			[CSharpSyntaxTree.ParseText(referencedSource, parseOptions),],
			References.For([]),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		using System.IO.MemoryStream stream = new();
		Microsoft.CodeAnalysis.Emit.EmitResult emitted = referenced.Emit(stream);
		if (!emitted.Success)
		{
			throw new InvalidOperationException(
				"The referenced assembly does not compile: "
				+ string.Join(Environment.NewLine, emitted.Diagnostics.Select(d => d.ToString())));
		}

		return Run(source, [MetadataReference.CreateFromImage(stream.ToArray()),]);
	}

	private static GeneratorResult Run(
		[StringSyntax("c#-test")] string source,
		MetadataReference[] additionalReferences,
		params Type[] assemblyTypes)
	{
		AwaitenGenerator generator = new();
		CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
		SyntaxTree[] syntaxTrees = [CSharpSyntaxTree.ParseText(source, parseOptions),];

		CSharpCompilation compilation = CSharpCompilation.Create(
			"TestAssembly",
			syntaxTrees,
			[..References.For(assemblyTypes), ..additionalReferences,],
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
