using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	// AWT134 fires when a container-side composition attribute is applied to a class in an assembly that declares
	// no [Container] - composition leaking into a domain assembly. Reported by AwaitenBoundaryAnalyzer, so these
	// tests drive that analyzer.
	public class Awt134BoundaryGuard
	{
		[Fact]
		public async Task ReportsForARegistrationAttributeOnADomainClass()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Grinder;

			                                       [Singleton<Grinder>]
			                                       public sealed class Misplaced
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT134"))).IsTrue()
				.Because("a registration attribute belongs on the [Container], not on a domain class in an assembly with no container");
		}

		[Fact]
		public async Task ReportsForAScanAttributeOnADomainClass()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IDrink;

			                                       [Scan<IDrink>]
			                                       public sealed class Misplaced
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT134"))).IsTrue()
				.Because("scanning is a container concern and does not belong on a domain class");
		}

		[Fact]
		public async Task DoesNotReportWhenTheAssemblyDeclaresAContainer()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Grinder;

			                                       [Container]
			                                       [Singleton<Grinder>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT134"))).IsFalse()
				.Because("the guard is scoped to assemblies with no [Container]; a registration on the container is exactly right");
		}

		[Fact]
		public async Task DoesNotReportForAModule()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Grinder;

			                                       [Module]
			                                       [Singleton<Grinder>]
			                                       public static partial class GrinderModule
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT134"))).IsFalse()
				.Because("a [Module] is composition code and may legitimately carry registrations in a library");
		}

		[Fact]
		public async Task DoesNotReportForTheConsumerSideEscapeHatches()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock;

			                                       public sealed class Service
			                                       {
			                                       	public Service([FromKey("utc")] IClock clock) { }

			                                       	[Inject] public IClock? Clock { get; set; }
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT134"))).IsFalse()
				.Because("[FromKey], [Arg] and [Inject] are the documented escape hatches allowed on domain types");
		}
	}
}
