using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt182ScanNoMatchingInterface
	{
		[Fact]
		public async Task ReportsWhenAMatchingInterfaceScanMatchesATypeWithNoConventionInterface()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IDrink { }
			                                       public interface ILatte { }
			                                       public sealed class Latte : IDrink, ILatte { }
			                                       public sealed class Mocha : IDrink { }   // no IMocha

			                                       [Container]
			                                       [Scan(typeof(IDrink), As = ScanAs.MatchingInterface)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT182*").AsWildcard()
				.And.Contains("*Mocha*").AsWildcard()
				.Because("Mocha matched the scan but implements no interface named IMocha");
		}

		[Fact]
		public async Task DoesNotReportWhenTheSelfFlagIsAlsoSet()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IDrink { }
			                                       public sealed class Mocha : IDrink { }   // no IMocha

			                                       [Container]
			                                       [Scan(typeof(IDrink), As = ScanAs.Self | ScanAs.MatchingInterface)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT182"))).IsFalse()
				.Because("the Self flag still registers the match as its own concrete type");
		}

		[Fact]
		public async Task DoesNotReportForAMarkerlessScan()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IConformingWidget { }
			                                       public sealed class ConformingWidget : IConformingWidget { }
			                                       public sealed class BareWidget { }   // no interface, no convention

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "*Widget" })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT182"))).IsFalse()
				.Because("a markerless scan skips a non-conforming type silently instead of warning per type");
		}
	}
}
