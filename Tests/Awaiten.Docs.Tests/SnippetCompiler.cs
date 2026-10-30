using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Awaiten.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Awaiten.Docs.Tests;

/// <summary>
///     Compiles the code blocks of a page together with scaffold files, which declare the placeholders that the
///     samples take for granted, and runs the Awaiten generator and analyzers over them like a build does.
/// </summary>
/// <remarks>
///     All blocks of a page share the namespace <c>Snippets</c>, so that a type or a <c>using</c> directive that a page
///     shows once applies to all of its blocks, like it does for a reader. A type that several blocks declare, e.g. to
///     show variants of the same container, or that a block with an <c>expect=</c> marker declares to show a mistake,
///     stays local to each of them, nested in the <c>partial</c> class of its block so that a nested <c>[Container]</c>
///     still generates. A block that declares no variants derives from the block before, so that it uses the variants
///     declared there. The statements of each block become the body of an async method, and its members the members of
///     the surrounding class. Registration attributes that a block shows without a class are applied to a placeholder
///     class, which the compiler checks but the generator ignores. A <c>#line</c> directive maps every part back to the
///     page, and an initializer that is only a <c>//...</c> comment becomes <c>default!</c>.
/// </remarks>
internal static partial class SnippetCompiler
{
	private const string OmittedInitializerPattern = @"=\s*//\s*\.\.\.";

	/// <summary>
	///     Not a <c>.g.cs</c> name, which marks generated code that the analyzers skip.
	/// </summary>
	private const string SnippetsPath = "Snippets.cs";
	private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);
	private static readonly Lazy<MetadataReference[]> References = new(GetReferences);

	[GeneratedRegex(OmittedInitializerPattern)]
	private static partial Regex OmittedInitializer();

	/// <summary>
	///     Returns the problems of the <paramref name="blocks" /> and <paramref name="scaffoldFiles" />, each prefixed
	///     with the file and line: every error and Awaiten diagnostic of a block without an <c>expect=</c> marker or
	///     outside of all blocks, and every block whose diagnostic ids differ from the ones its marker lists.
	/// </summary>
	/// <remarks>
	///     The blocks without a marker compile together. Each block with a marker compiles on its own with them, so that
	///     a mistake it shows on purpose, e.g. a scan that matches too much, cannot leak into another block, and a
	///     diagnostic outside of all blocks that only this compilation reports, e.g. one without a location, is
	///     attributed to it.
	/// </remarks>
	public static List<string> GetErrors(IReadOnlyList<CodeBlock> blocks, IEnumerable<string> scaffoldFiles)
	{
		List<(CodeBlock Block, CompilationUnitSyntax Root)> parsed = blocks
			.Select(block => (block, CSharpSyntaxTree.ParseText(
				OmittedInitializer().Replace(block.Code, "= default!; //..."), ParseOptions).GetCompilationUnitRoot()))
			.ToList();
		List<(CodeBlock Block, CompilationUnitSyntax Root)> unmarked = parsed
			.Where(x => x.Block.Expect.Length == 0)
			.ToList();
		HashSet<string> sharedTypes = unmarked
			.SelectMany(x => x.Root.Members.Select(GetTypeName).OfType<string>().Distinct())
			.GroupBy(name => name)
			.Where(group => group.Count() == 1)
			.Select(group => group.Key)
			.ToHashSet();
		SyntaxTree[] scaffold = scaffoldFiles
			.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), ParseOptions, file))
			.ToArray();

		List<Reported> reported = Compile(unmarked, sharedTypes, scaffold);
		List<string> errors = [];
		HashSet<Reported> attributed = [];
		foreach ((CodeBlock block, _) in unmarked)
		{
			List<Reported> ofBlock = reported.Where(x => x.IsIn(block)).ToList();
			attributed.UnionWith(ofBlock);
			errors.AddRange(ofBlock.Select(x => x.Text));
		}

		errors.AddRange(reported.Where(x => !attributed.Contains(x)).Select(x => x.Text));
		foreach ((CodeBlock block, _) in parsed.Where(x => x.Block.Expect.Length > 0))
		{
			List<Reported> ofBlock = Compile(parsed.Where(x => x.Block.Expect.Length == 0 || x.Block == block).ToList(),
					sharedTypes, scaffold)
				.Where(x => x.IsIn(block) || (!x.IsInAny(blocks) && !reported.Contains(x)))
				.ToList();
			if (!ofBlock.Select(x => x.Id).ToHashSet().SetEquals(block.ExpectedIds))
			{
				errors.Add($"{block.Page}({block.Line}): expected exactly {block.Expect}, but found " +
				           (ofBlock.Count == 0
					           ? "nothing"
					           : Environment.NewLine + string.Join(Environment.NewLine, ofBlock.Select(x => "  " + x.Text))));
			}
		}

		return errors;
	}

	private static List<Reported> Compile(List<(CodeBlock Block, CompilationUnitSyntax Root)> parsed,
		HashSet<string> sharedTypes, SyntaxTree[] scaffold)
	{
		StringBuilder source = new();
		source.AppendLine("namespace Snippets").AppendLine("{");
		foreach ((CodeBlock block, CompilationUnitSyntax root) in parsed)
		{
			AppendMapped(source, block, root.Usings);
		}

		string? previous = null;
		for (int i = 0; i < parsed.Count; i++)
		{
			if (AppendBlock(source, parsed[i].Block, parsed[i].Root, i,
				    parsed[i].Block.Expect.Length == 0 ? sharedTypes : [], previous))
			{
				previous = $"Block{i}";
			}
		}

		source.AppendLine("}");

		CSharpCompilation compilation = CSharpCompilation.Create("Snippets",
			scaffold.Append(CSharpSyntaxTree.ParseText(source.ToString(), ParseOptions, SnippetsPath)),
			References.Value,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
				nullableContextOptions: NullableContextOptions.Enable));

		GeneratorDriver driver = CSharpGeneratorDriver.Create(
				[new AwaitenGenerator().AsSourceGenerator(),], parseOptions: ParseOptions)
			.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
		ImmutableArray<Diagnostic> analyzerDiagnostics = output
			.WithAnalyzers([new AwaitenAnalyzer(), new AwaitenBoundaryAnalyzer(),])
			.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();

		return driver.GetRunResult().Diagnostics
			.Concat(output.GetDiagnostics())
			.Concat(analyzerDiagnostics)
			.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error ||
			                     (diagnostic.Id.StartsWith("AWT") && diagnostic.Severity != DiagnosticSeverity.Hidden))
			.Select(diagnostic => Format(diagnostic, output))
			.Distinct()
			.ToList();
	}

	/// <summary>
	///     A generator reports its diagnostics with the unmapped position in the source tree, so the <c>#line</c>
	///     directives are applied through the tree here.
	/// </summary>
	private static Reported Format(Diagnostic diagnostic, Compilation compilation)
	{
		FileLinePositionSpan span = diagnostic.Location.GetMappedLineSpan();
		if (diagnostic.Location.SourceTree is null &&
		    compilation.SyntaxTrees.FirstOrDefault(tree => tree.FilePath == span.Path) is { } tree)
		{
			span = tree.GetMappedLineSpan(diagnostic.Location.SourceSpan);
		}

		int line = span.StartLinePosition.Line + 1;
		return new Reported(span.Path, line, diagnostic.Id,
			$"{span.Path}({line},{span.StartLinePosition.Character + 1}): " +
			$"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");
	}

	/// <summary>
	///     Appends the class of the block. A block that declares no variants of its own continues the
	///     <paramref name="previous" /> block, so it sees the variants declared there, like a reader does.
	/// </summary>
	/// <returns><see langword="false" /> when the class is static and therefore cannot be derived from.</returns>
	private static bool AppendBlock(StringBuilder source, CodeBlock block, CompilationUnitSyntax root, int index,
		HashSet<string> sharedTypes, string? previous)
	{
		List<SyntaxNode> statements = [];
		List<SyntaxNode> members = [];
		List<SyntaxNode> types = [];
		foreach (MemberDeclarationSyntax member in root.Members)
		{
			if (member is GlobalStatementSyntax { Statement: var statement, } &&
			    !(statement is LocalFunctionStatementSyntax localFunction && IsMethod(localFunction)))
			{
				statements.Add(member);
			}
			else if (member is BaseNamespaceDeclarationSyntax ||
			         (GetTypeName(member) is { } name && sharedTypes.Contains(name) &&
			          !member.Modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword))))
			{
				types.Add(member);
			}
			else
			{
				members.Add(member);
			}
		}

		bool isStatic = members.Any(member => member is GlobalStatementSyntax
		{
			Statement: LocalFunctionStatementSyntax localFunction,
		} && IsExtensionMethod(localFunction));
		string modifier = isStatic ? "static " : "";
		bool declaresVariants = members.Any(member =>
			member is MemberDeclarationSyntax declaration && GetTypeName(declaration) is not null);
		string baseClass = isStatic || declaresVariants || previous is null ? "" : $" : {previous}";

		source.AppendLine("#line default")
			.AppendLine($"public {modifier}partial class Block{index}{baseClass}")
			.AppendLine("{")
			.AppendLine($"public {modifier}async Task Run{index}()")
			.AppendLine("{");
		AppendMapped(source, block, statements);
		source.AppendLine("#line default").AppendLine("}");
		AppendMapped(source, block, members);
		source.AppendLine("#line default").AppendLine("}");
		AppendMapped(source, block, types);
		return !isStatic;
	}

	private static string? GetTypeName(MemberDeclarationSyntax member)
		=> member switch
		{
			BaseTypeDeclarationSyntax type => type.Identifier.Text,
			DelegateDeclarationSyntax @delegate => @delegate.Identifier.Text,
			_ => null,
		};

	/// <summary>
	///     A method at the top level of a block is parsed as a local function, so its accessibility or a <c>this</c>
	///     parameter tells that the sample shows a method of a class.
	/// </summary>
	private static bool IsMethod(LocalFunctionStatementSyntax localFunction)
		=> IsExtensionMethod(localFunction) || localFunction.Modifiers.Any(modifier =>
			modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.InternalKeyword) ||
			modifier.IsKind(SyntaxKind.PrivateKeyword) || modifier.IsKind(SyntaxKind.ProtectedKeyword));

	private static bool IsExtensionMethod(LocalFunctionStatementSyntax localFunction)
		=> localFunction.ParameterList.Parameters.FirstOrDefault()?.Modifiers.Any(SyntaxKind.ThisKeyword) == true;

	private static void AppendMapped(StringBuilder source, CodeBlock block, IEnumerable<SyntaxNode> nodes)
	{
		foreach (SyntaxNode node in nodes)
		{
			int line = block.Line + node.SyntaxTree.GetLineSpan(node.FullSpan).StartLinePosition.Line;
			source.AppendLine($"#line {line} \"{block.Page}\"").AppendLine(node.ToFullString());
			if (node is IncompleteMemberSyntax { Type: null, AttributeLists.Count: > 0, })
			{
				source.AppendLine($"private sealed class AttributeTarget{line};");
			}
		}
	}

	/// <summary>
	///     The assemblies of this test application, which references what a reader's project would, except this test
	///     assembly and the generator, which a reader's project only runs.
	/// </summary>
	private static MetadataReference[] GetReferences()
	{
		Dictionary<string, string> paths = new(StringComparer.OrdinalIgnoreCase);
		foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
		{
			paths[Path.GetFileNameWithoutExtension(path)] = path;
		}

		paths.Remove(typeof(SnippetCompiler).Assembly.GetName().Name!);
		paths.Remove(typeof(AwaitenGenerator).Assembly.GetName().Name!);
		return paths.Values.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToArray();
	}

	/// <summary>
	///     A reported diagnostic at the mapped <paramref name="Path" /> and one-based <paramref name="Line" />.
	/// </summary>
	private sealed record Reported(string Path, int Line, string Id, string Text)
	{
		public bool IsIn(CodeBlock block)
			=> Path == block.Page && Line >= block.Line && Line < block.Line + block.LineCount;

		public bool IsInAny(IEnumerable<CodeBlock> blocks) => blocks.Any(IsIn);
	}
}
