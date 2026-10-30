using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Awaiten.Docs.Tests;

public sealed class DocsPagesTests
{
	private static readonly string ProjectDirectory = GetProjectDirectory();

	private static readonly string PagesDirectory =
		Path.GetFullPath(Path.Combine(ProjectDirectory, "..", "..", "Docs", "pages"));

	private static readonly string ScaffoldDirectory = Path.Combine(ProjectDirectory, "Scaffold");

	public static IEnumerable<string> Pages()
		=> Directory.EnumerateFiles(PagesDirectory, "*.*", SearchOption.AllDirectories)
			.Where(path => path.EndsWith(".md") || path.EndsWith(".mdx"))
			.Select(path => path.Substring(PagesDirectory.Length + 1).Replace('\\', '/'))
			.OrderBy(page => page, StringComparer.Ordinal);

	[Test]
	[Explicit]
	[Category(TestCategories.Slow)]
	[MethodDataSource(nameof(Pages))]
	public void CodeBlocks_ShouldCompile(string page)
	{
		List<string> errors = SnippetCompiler.GetErrors(ExtractCodeBlocks(page), ScaffoldFiles(page));

		Fail.Unless(errors.Count == 0, string.Join(Environment.NewLine, errors));
	}

	[Test]
	public async Task Extract_ShouldFindTheCodeBlocksOfAllPages()
	{
		int count = Pages().Sum(page => ExtractCodeBlocks(page).Count);

		await That(count).IsGreaterThan(250)
			.Because("the check must not pass because it no longer finds the code blocks");
	}

	[Test]
	public async Task Extract_ShouldOnlyReturnCSharpBlocksWithoutNoCompileMarker()
	{
		string[] lines =
		[
			"# Title",
			"```csharp",
			"int a = 1;",
			"```",
			"```csharp no-compile",
			"int b = 2;",
			"```",
			"  ```csharp title=\"Awaiten\" expect=AWT101,AWT102",
			"  int c = 3;",
			"  int d = 4;",
			"  ```",
			"```sh",
			"dotnet add package Awaiten",
			"```",
		];

		List<CodeBlock> blocks = CodeBlock.Extract("page.md", lines);

		await That(blocks).IsEqualTo([
			new CodeBlock("page.md", 3, "int a = 1;"),
			new CodeBlock("page.md", 9, "  int c = 3;\n  int d = 4;", "AWT101,AWT102"),
		]);
		await That(blocks[1].ExpectedIds).IsEqualTo(["AWT101", "AWT102",]);
	}

	[Test]
	public async Task GetErrors_ShouldReportThePageAndLineOfTheBlock()
	{
		CodeBlock block = new("Docs/pages/page.md", 10, "int value = 1;\nvalue.Foo();");

		List<string> errors = SnippetCompiler.GetErrors([block,], ScaffoldFiles("page.md"));

		await That(errors).HasSingle().Which.StartsWith("Docs/pages/page.md(11,7): CS1061:");
	}

	[Test]
	public async Task GetErrors_ShouldMapGeneratorDiagnosticsToThePage()
	{
		CodeBlock block = new("Docs/pages/page.md", 10, """
		                                                public sealed class Cup(Grinder grinder);

		                                                [Container]
		                                                [Transient<Cup>]
		                                                public static partial class CoffeeShop;
		                                                """);

		List<string> errors = SnippetCompiler.GetErrors([block,], ScaffoldFiles("page.md"));

		await That(errors).HasSingle().Which.StartsWith("Docs/pages/page.md(13,").And.Contains("AWT101");
	}

	[Test]
	public async Task GetErrors_WhenTheBlockReportsExactlyTheExpectedIds_ShouldSucceed()
	{
		CodeBlock block = new("page.md", 1, """
		                                     public sealed class Cup(Grinder grinder);

		                                     [Container]
		                                     [Transient<Cup>]
		                                     public static partial class CoffeeShop;
		                                     """, "AWT101");

		List<string> errors = SnippetCompiler.GetErrors([block,], ScaffoldFiles("page.md"));

		await That(errors).IsEmpty();
	}

	[Test]
	[Arguments("AWT102")]
	[Arguments("AWT101,AWT102")]
	public async Task GetErrors_WhenTheBlockReportsOtherIdsThanExpected_ShouldFail(string expect)
	{
		CodeBlock block = new("page.md", 1, """
		                                     public sealed class Cup(Grinder grinder);

		                                     [Container]
		                                     [Transient<Cup>]
		                                     public static partial class CoffeeShop;
		                                     """, expect);

		List<string> errors = SnippetCompiler.GetErrors([block,], ScaffoldFiles("page.md"));

		await That(errors).HasSingle().Which.StartsWith($"page.md(1): expected exactly {expect}, but found")
			.And.Contains("AWT101");
	}

	[Test]
	public async Task GetErrors_WhenAnExpectedBlockCompiles_ShouldFail()
	{
		CodeBlock block = new("page.md", 1, "int value = 1;", "CS0103");

		List<string> errors = SnippetCompiler.GetErrors([block,], ScaffoldFiles("page.md"));

		await That(errors).HasSingle().Which.IsEqualTo("page.md(1): expected exactly CS0103, but found nothing");
	}

	[Test]
	public async Task GetErrors_ShouldShareTypesAndUsingsBetweenTheBlocksOfAPage()
	{
		CodeBlock first = new("page.md", 1, """
		                                     using System.Text;

		                                     public sealed class Menu(StringBuilder text);
		                                     """);
		CodeBlock second = new("page.md", 20, """
		                                      [Container]
		                                      [Singleton<StringBuilder>]
		                                      [Singleton<Menu>]
		                                      public static partial class CoffeeShop;

		                                      using var shop = new CoffeeShop.Root();
		                                      Menu menu = shop.Resolve<Menu>();
		                                      """);

		List<string> errors = SnippetCompiler.GetErrors([first, second,], ScaffoldFiles("page.md"));

		await That(errors).IsEmpty();
	}

	[Test]
	public async Task GetErrors_ShouldKeepTypesThatSeveralBlocksDeclareLocalToEachBlock()
	{
		CodeBlock first = new("page.md", 1, """
		                                     [Container]
		                                     [Singleton<Grinder>]
		                                     public static partial class CoffeeShop;

		                                     Grinder grinder = new CoffeeShop.Root().Resolve<Grinder>();
		                                     """);
		CodeBlock second = new("page.md", 20, """
		                                      [Container]
		                                      [Transient<Grinder>]
		                                      public static partial class CoffeeShop;
		                                      """);

		List<string> errors = SnippetCompiler.GetErrors([first, second,], ScaffoldFiles("page.md"));

		await That(errors).IsEmpty();
	}

	[Test]
	public async Task GetErrors_WhenABlockDeclaresNoVariant_ShouldContinueThePreviousBlock()
	{
		CodeBlock first = new("page.md", 1, """
		                                     [Container]
		                                     [Singleton<Grinder>]
		                                     public static partial class CoffeeShop;
		                                     """);
		CodeBlock second = new("page.md", 10, """
		                                      [Container]
		                                      [Transient<Grinder>]
		                                      public static partial class CoffeeShop;
		                                      """);
		CodeBlock third = new("page.md", 20, "using var shop = new CoffeeShop.Root();");

		List<string> errors = SnippetCompiler.GetErrors([first, second, third,], ScaffoldFiles("page.md"));

		await That(errors).IsEmpty()
			.Because("without the previous block, 'CoffeeShop' names the namespace of the linked domain");
	}

	[Test]
	public async Task GetErrors_ShouldCheckRegistrationAttributesWithoutAClass()
	{
		CodeBlock block = new("page.md", 1, "[Singleton<Grinder, IBrewer>]");
		CodeBlock container = new("page.md", 10, """
		                                         [Container]
		                                         public static partial class CoffeeShop;
		                                         """);

		List<string> errors = SnippetCompiler.GetErrors([block, container,], ScaffoldFiles("page.md"));

		await That(errors).HasSingle().Which.StartsWith("page.md(1,").And.Contains("CS0311");
	}

	[Test]
	public async Task GetErrors_ShouldNotShareTheTypesOfABlockWithAnExpectMarker()
	{
		CodeBlock mistake = new("page.md", 1, """
		                                       public sealed class Cup(Grinder grinder);

		                                       [Container]
		                                       [Transient<Cup>]
		                                       public static partial class CoffeeShop;
		                                       """, "AWT101");
		CodeBlock fixedBlock = new("page.md", 10, """
		                                          [Container]
		                                          [Transient<Cup>]
		                                          public static partial class Counter;
		                                          """);

		List<string> errors = SnippetCompiler.GetErrors([mistake, fixedBlock,], ScaffoldFiles("page.md"));

		await That(errors).IsEmpty()
			.Because("the other block registers the scaffold's Cup, not the broken one the marked block declares");
	}

	private static List<CodeBlock> ExtractCodeBlocks(string page)
		=> CodeBlock.Extract($"Docs/pages/{page}", File.ReadAllLines(Path.Combine(PagesDirectory, page)));

	private static string GetProjectDirectory([CallerFilePath] string path = "")
		=> Path.GetDirectoryName(path)!;

	private static IEnumerable<string> ScaffoldFiles(string page)
	{
		yield return Path.Combine(ScaffoldDirectory, "Scaffold.cs");
		yield return Path.GetFullPath(Path.Combine(ProjectDirectory, "..", "Awaiten.ExampleTests", "CoffeeShopDomain.cs"));
		string pageScaffold = Path.Combine(ScaffoldDirectory, Path.ChangeExtension(page, ".cs"));
		if (File.Exists(pageScaffold))
		{
			yield return pageScaffold;
		}
	}
}
