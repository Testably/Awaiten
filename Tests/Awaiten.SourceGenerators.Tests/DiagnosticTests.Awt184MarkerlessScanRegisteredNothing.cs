using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt184MarkerlessScanRegisteredNothing
	{
		[Fact]
		public async Task ReportsWhenAMarkerlessMatchingInterfaceScanRegistersNothing()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // Matched by the filter, but neither follows the I + name convention.
			                                       public sealed class FirstWidget { }
			                                       public sealed class SecondWidget { }

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "*Widget" })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT184*").AsWildcard()
				.Because("candidates matched the filter but none implements its I + name interface");
		}

		[Fact]
		public async Task DoesNotReportWhenAtLeastOneMatchRegisters()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IFirstWidget { }
			                                       public sealed class FirstWidget : IFirstWidget { }
			                                       public sealed class SecondWidget { }   // no interface

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "*Widget" })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT184*").AsWildcard()
				.Because("FirstWidget registers under IFirstWidget, so the scan is not empty");
		}

		[Fact]
		public async Task DoesNotReportStaleExclusionsWhenTheScanSawNoCandidate()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IWidget { }

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "*Widget" }, Exclude = new[] { typeof(IWidget) })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT184*").AsWildcard()
				.Because("the assembly holds no concrete class, so the scan registers nothing");
			await That(result.Diagnostics).DoesNotContain("*AWT173*").AsWildcard()
				.Because("a stale-exclusion hint is noise when the scan saw no candidate to filter");
		}
	}
}
