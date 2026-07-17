using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt173ScanExclusionNeverMatched
	{
		[Fact]
		public async Task ReportsAStaleExcludeType()
		{
			GeneratorResult result = Generator.Run("""
				using Awaiten;

				namespace MyCode;

				public interface IPlugin { }
				public sealed class KeepPlugin : IPlugin { }
				public sealed class Unrelated { }

				[Container]
				[Scan(typeof(IPlugin), Exclude = new[] { typeof(Unrelated) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT173*Unrelated*").AsWildcard()
				.Because("Unrelated is not a scan candidate, so the exclusion never applies");
		}

		[Fact]
		public async Task ReportsAStaleExcludePattern()
		{
			GeneratorResult result = Generator.Run("""
				using Awaiten;

				namespace MyCode;

				public interface IPlugin { }
				public sealed class OrderHandler : IPlugin { }

				[Container]
				[Scan(typeof(IPlugin), NamePatterns = new[] { "!Legacy*" })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT173*Legacy*").AsWildcard()
				.Because("no candidate name starts with Legacy, so the exclusion is dead");
		}

		[Fact]
		public async Task DoesNotReportAnExclusionThatApplied()
		{
			GeneratorResult result = Generator.Run("""
				using Awaiten;

				namespace MyCode;

				public interface IPlugin { }
				public sealed class KeepPlugin : IPlugin { }
				public sealed class DropPlugin : IPlugin { }

				[Container]
				[Scan(typeof(IPlugin), Exclude = new[] { typeof(DropPlugin) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).DoesNotContain("*AWT173*").AsWildcard()
				.Because("DropPlugin was excluded, so the exclusion did its job");
		}
	}
}
