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
		public async Task ReportsWhenAMatchEverythingPatternSitsAlongsideANarrowerOne()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IWidget { }
			                                       public sealed class Widget : IWidget { }

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "*", "*Widget" })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT183*").AsWildcard()
				.Because("includes are OR-combined, so the '*' still matches every type despite the narrower '*Widget'");
		}

		[Fact]
		public async Task ReportsWhenTheSoleNamespacePatternIsWildcardOnly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IWidget { }
			                                       public sealed class Widget : IWidget { }

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface, NamespacePatterns = new[] { "*" })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT183*").AsWildcard()
				.Because("'*' names nothing, so it sweeps every type in a root namespace instead of scoping the scan");
		}

		[Fact]
		public async Task ReportsWhenTheNamespacePatternConstrainsOnlyTheDepth()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IWidget { }
			                                       public sealed class Widget : IWidget { }

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface, NamespacePatterns = new[] { "**.*" })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT183*").AsWildcard()
				.Because("'**.*' matches every non-global namespace at any depth, so it does not scope the scan");
		}

		[Fact]
		public async Task DoesNotReportWhenANamespacePatternNamesASegment()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IWidget { }
			                                       public sealed class Widget : IWidget { }

			                                       [Container]
			                                       [Scan(As = ScanAs.MatchingInterface, NamespacePatterns = new[] { "MyCode.**" })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT183*").AsWildcard()
				.Because("a namespace pattern that names a segment positively bounds the scan");
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

			await That(result.Diagnostics).DoesNotContain("*AWT183*").AsWildcard()
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

			await That(result.Diagnostics).DoesNotContain("*AWT183*").AsWildcard()
				.Because("InAssembliesOf alone is a valid scope for a markerless scan");
		}
	}
}
