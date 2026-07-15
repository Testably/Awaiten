using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt183MarkerlessScanInvalid
	{
		[Fact]
		public async Task ReportsWhenAMarkerlessScanIncludesTheMarkerExposure()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Widget { }

			                                       [Container]
			                                       [Scan(As = ScanAs.Marker, NamePatterns = new[] { "*Widget" })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT183*").AsWildcard()
				.Because("a markerless scan has no marker to register the Marker exposure under");
		}

		[Fact]
		public async Task ReportsWhenAMarkerlessScanDeclaresNoScopingFilter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IWidget { }
			                                       public sealed class Widget : IWidget { }

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT183*").AsWildcard()
				.Because("a markerless scan with no NamePatterns/NamespacePatterns/InAssembliesOf would sweep every concrete type");
		}

		[Fact]
		public async Task ReportsWhenAMarkerlessScansSoleFilterMatchesEverything()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IWidget { }
			                                       public sealed class Widget : IWidget { }

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "*" })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT183*").AsWildcard()
				.Because("a match-everything '*' pattern does not narrow, so the scan would still sweep every type");
		}

		[Fact]
		public async Task DoesNotReportForAMarkerlessScanWithAFilterAndNoMarkerExposure()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IWidget { }
			                                       public sealed class Widget : IWidget { }

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "*Widget" })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT183"))).IsFalse()
				.Because("a scoped markerless MatchingInterface scan is well-formed");
		}

		[Fact]
		public async Task DoesNotReportWhenInAssembliesOfIsTheOnlyScope()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using Awaiten.Tests.Support;

			                                       namespace MyCode;

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface, InAssembliesOf = new[] { typeof(ICrossAssemblyPlugin) })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """, typeof(global::Awaiten.Tests.Support.ICrossAssemblyPlugin));

			await That(result.Diagnostics.Any(d => d.Contains("AWT183"))).IsFalse()
				.Because("InAssembliesOf alone is a valid scope for a markerless scan");
		}
	}
}
