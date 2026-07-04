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

		return Run(source, [EmitToReference(referenced),]);
	}

	private static GeneratorResult Run(
		[StringSyntax("c#-test")] string source,
		MetadataReference[] additionalReferences,
		params Type[] assemblyTypes)
	{
		(Compilation outputCompilation, GeneratorDriverRunResult runResult) =
			RunGenerator(source, additionalReferences, assemblyTypes);

		ImmutableArray<Diagnostic> compilationDiagnostics = outputCompilation.GetDiagnostics();
		Dictionary<string, string> generatedSources = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.ToDictionary(s => s.HintName, s => s.SourceText.ToString());
		string[] diagnosticMessages =
		[
			..compilationDiagnostics.Where(x => !NoWarn.Contains(x.Id)).Select(x => x.ToString()),
			..runResult.Diagnostics.Where(x => !NoWarn.Contains(x.Id)).Select(x => x.ToString()),
		];
		return new GeneratorResult(generatedSources, diagnosticMessages);
	}

	/// <summary>
	///     The single generator-driver bootstrap shared by <see cref="Generator" /> and
	///     <see cref="Analyzer" />: parses <paramref name="source" />, runs the <see cref="AwaitenGenerator" />
	///     over it and returns the post-generation compilation plus the driver's run result (which carries the
	///     generator diagnostics and the generated sources).
	/// </summary>
	internal static (Compilation Output, GeneratorDriverRunResult Result) RunGenerator(
		[StringSyntax("c#-test")] string source,
		MetadataReference[] additionalReferences,
		Type[] assemblyTypes,
		string assemblyName = "TestAssembly")
	{
		CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
		SyntaxTree[] syntaxTrees = [CSharpSyntaxTree.ParseText(source, parseOptions),];

		CSharpCompilation compilation = CSharpCompilation.Create(
			assemblyName,
			syntaxTrees,
			[..References.For(assemblyTypes), ..additionalReferences,],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			[new AwaitenGenerator().AsSourceGenerator(),],
			[],
			parseOptions,
			null);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);
		return (outputCompilation, driver.GetRunResult());
	}

	/// <summary>
	///     Emits a compilation to an in-memory assembly and returns a metadata reference to it, failing
	///     loudly (with the offending diagnostics) when it does not compile.
	/// </summary>
	internal static MetadataReference EmitToReference(Compilation compilation)
	{
		using System.IO.MemoryStream stream = new();
		Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream);
		if (!emitted.Success)
		{
			throw new InvalidOperationException(
				"The referenced assembly does not compile: "
				+ string.Join(Environment.NewLine, emitted.Diagnostics.Select(d => d.ToString())));
		}

		return MetadataReference.CreateFromImage(stream.ToArray());
	}
}
