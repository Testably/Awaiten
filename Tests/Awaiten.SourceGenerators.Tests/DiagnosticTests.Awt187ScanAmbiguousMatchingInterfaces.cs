using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt187ScanAmbiguousMatchingInterfaces
	{
		[Fact]
		public async Task ReportsWhenAMatchImplementsSeveralSameNamedInterfaces()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace A { public interface IWorker { } }
			                                       namespace B { public interface IWorker { } }

			                                       namespace MyCode
			                                       {
			                                           public interface IMarker { }
			                                           public sealed class Worker : IMarker, A.IWorker, B.IWorker { }

			                                           [Container]
			                                           [Scan(typeof(IMarker), As = ScanAs.MatchingInterface)]
			                                           public static partial class MyContainer
			                                           {
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT187*").AsWildcard()
				.Because("neither IWorker is in Worker's own namespace, so the match registers under both");
		}

		[Fact]
		public async Task DoesNotReportWhenTheOwnNamespaceInterfaceWinsTheTiebreak()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Other { public interface IWorker { } }

			                                       namespace MyCode
			                                       {
			                                           public interface IMarker { }
			                                           public interface IWorker { }
			                                           public sealed class Worker : IMarker, IWorker, Other.IWorker { }

			                                           [Container]
			                                           [Scan(typeof(IMarker), As = ScanAs.MatchingInterface)]
			                                           public static partial class MyContainer
			                                           {
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT187*").AsWildcard()
				.Because("the own-namespace IWorker decides the tie, so a single interface registers");
		}

		[Fact]
		public async Task DoesNotReportWhenTheAccessibilityFilterResolvesTheTie()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace A { internal interface IWorker { } }
				namespace B { public interface IWorker { } }

				namespace Lib
				{
				    public interface IMarker { }
				    public sealed class Worker : IMarker, A.IWorker, B.IWorker { }
				}
				""", """
				using Awaiten;

				namespace MyCode;

				[Container]
				[Scan(typeof(Lib.IMarker), As = ScanAs.MatchingInterface, InAssembliesOf = new[] { typeof(Lib.IMarker) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).DoesNotContain("*AWT187*").AsWildcard()
				.Because("the internal A.IWorker is dropped, so only B.IWorker registers and no ambiguity remains");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("typeof(global::B.IWorker)");
			await That(source).DoesNotContain("A.IWorker");
		}

		[Fact]
		public async Task DoesNotReportWhenEverySameNamedInterfaceIsInaccessible()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace A { internal interface IWorker { } }
				namespace B { internal interface IWorker { } }

				namespace Lib
				{
				    public interface IMarker { }
				    public sealed class Worker : IMarker, A.IWorker, B.IWorker { }
				}
				""", """
				using Awaiten;

				namespace MyCode;

				[Container]
				[Scan(typeof(Lib.IMarker), As = ScanAs.MatchingInterface, InAssembliesOf = new[] { typeof(Lib.IMarker) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT188*").AsWildcard()
				.Because("the match registers nothing, which AWT188 reports per inaccessible interface");
			await That(result.Diagnostics).DoesNotContain("*AWT187*").AsWildcard()
				.Because("claiming the match is registered under each interface would contradict AWT188");
		}

		[Fact]
		public async Task DoesNotReportForAMarkerlessScan()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace A { public interface IWorker { } }
			                                       namespace B { public interface IWorker { } }

			                                       namespace MyCode
			                                       {
			                                           public sealed class Worker : A.IWorker, B.IWorker { }

			                                           [Container]
			                                           [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "Worker" })]
			                                           public static partial class MyContainer
			                                           {
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT187*").AsWildcard()
				.Because("a markerless match is never warned, like AWT139/AWT182/AWT188");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("typeof(global::A.IWorker)")
				.And.Contains("typeof(global::B.IWorker)")
				.Because("the ambiguous match still registers under each interface, just without the warning");
		}

		[Fact]
		public async Task DoesNotReportWhenTheMatchIsPrunedAsUnconstructable()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace A { public interface IWorker { } }
			                                       namespace B { public interface IWorker { } }

			                                       namespace MyCode
			                                       {
			                                           public interface IMarker { }
			                                           public interface IUnregistered { }
			                                           public sealed class Worker : IMarker, A.IWorker, B.IWorker
			                                           {
			                                               public Worker(IUnregistered dependency) { }
			                                           }

			                                           [Container]
			                                           [Scan(typeof(IMarker), As = ScanAs.MatchingInterface, SkipUnconstructable = true)]
			                                           public static partial class MyContainer
			                                           {
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT141*").AsWildcard()
				.Because("the match cannot be constructed and the scan opted into skipping it");
			await That(result.Diagnostics).DoesNotContain("*AWT187*").AsWildcard()
				.Because("a skipped match is registered under nothing, so the ambiguity warning would contradict AWT141");
		}
	}
}
