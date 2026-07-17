using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt174ScanPatternMatchesEverything
	{
		[Fact]
		public async Task ReportsANameIncludeOfStar()
		{
			GeneratorResult result = Generator.Run("""
				using Awaiten;

				namespace MyCode;

				public interface IPlugin { }
				public sealed class OrderPlugin : IPlugin { }

				[Container]
				[Scan(typeof(IPlugin), NamePatterns = new[] { "*" })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT174*").AsWildcard()
				.Because("'*' matches every name, so it does not narrow the scan");
		}

		[Fact]
		public async Task ReportsANamespaceIncludeOfDoubleStar()
		{
			GeneratorResult result = Generator.Run("""
				using Awaiten;

				namespace MyCode;

				public interface IPlugin { }
				public sealed class OrderPlugin : IPlugin { }

				[Container]
				[Scan(typeof(IPlugin), NamespacePatterns = new[] { "**" })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT174*").AsWildcard()
				.Because("'**' matches every namespace, so it does not narrow the scan");
		}

		[Fact]
		public async Task DoesNotReportANarrowingPattern()
		{
			GeneratorResult result = Generator.Run("""
				using Awaiten;

				namespace MyCode;

				public interface IPlugin { }
				public sealed class OrderHandler : IPlugin { }

				[Container]
				[Scan(typeof(IPlugin), NamePatterns = new[] { "*Handler" })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).DoesNotContain("*AWT174*").AsWildcard()
				.Because("'*Handler' does not match every candidate");
		}
	}
}
