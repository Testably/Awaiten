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

			await That(result.Diagnostics.Any(d => d.Contains("AWT184"))).IsFalse()
				.Because("FirstWidget registers under IFirstWidget, so the scan is not empty");
		}
	}
}
