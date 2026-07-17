using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt185ScanExposesNothing
	{
		[Fact]
		public async Task ReportsWhenAsResolvesToNoFlag()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IDrink { }
			                                       public sealed class Latte : IDrink { }

			                                       [Container]
			                                       [Scan(typeof(IDrink), As = ScanAs.Self & ScanAs.Marker)]   // & is empty, meant |
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT185*").AsWildcard()
				.Because("As & yields no flag, so the scan would register nothing");
		}

		[Fact]
		public async Task ReportsWhenAsHasOnlyAnUnrecognizedBit()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IDrink { }
			                                       public sealed class Latte : IDrink { }

			                                       [Container]
			                                       [Scan(typeof(IDrink), As = (ScanAs)8)]   // no recognized exposure bit
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT185*").AsWildcard()
				.Because("an out-of-range value sets no Self/Marker/MatchingInterface bit, so nothing is exposed");
		}

		[Fact]
		public async Task DoesNotReportForTheDefaultSelfExposure()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IDrink { }
			                                       public sealed class Latte : IDrink { }

			                                       [Container]
			                                       [Scan(typeof(IDrink))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT185*").AsWildcard()
				.Because("an unset As keeps the Self default, which exposes the match");
		}
	}
}
