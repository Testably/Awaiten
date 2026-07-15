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
			                                           public sealed class Worker : A.IWorker, B.IWorker { }

			                                           [Container]
			                                           [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "Worker" })]
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
			                                           public interface IWorker { }
			                                           public sealed class Worker : IWorker, Other.IWorker { }

			                                           [Container]
			                                           [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "Worker" })]
			                                           public static partial class MyContainer
			                                           {
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT187"))).IsFalse()
				.Because("the own-namespace IWorker decides the tie, so a single interface registers");
		}
	}
}
