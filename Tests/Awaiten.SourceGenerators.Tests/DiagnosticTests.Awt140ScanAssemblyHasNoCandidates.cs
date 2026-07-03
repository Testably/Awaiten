using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt140ScanAssemblyHasNoCandidates
	{
		[Fact]
		public async Task ReportsWhenAnInAssembliesOfAssemblyHasNoConcreteTypeAssignableToTheMarker()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using Awaiten.Tests.Support;

			                                       namespace MyCode;

			                                       // The support assembly has no concrete type assignable to IUnmatchedMarker.
			                                       public interface IUnmatchedMarker { }

			                                       [Container]
			                                       [Scan(typeof(IUnmatchedMarker), InAssembliesOf = new[] { typeof(ICrossAssemblyPlugin) })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """, typeof(global::Awaiten.Tests.Support.ICrossAssemblyPlugin));

			await That(result.Diagnostics).Contains("*AWT140*").AsWildcard()
				.Because("the named referenced assembly holds no concrete type assignable to the marker");
		}

		[Fact]
		public async Task DoesNotReportWhenTheAssemblyHasAssignableTypes()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using Awaiten.Tests.Support;

			                                       namespace MyCode;

			                                       [Container]
			                                       [Scan(typeof(ICrossAssemblyPlugin), InAssembliesOf = new[] { typeof(ICrossAssemblyPlugin) })]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """, typeof(global::Awaiten.Tests.Support.ICrossAssemblyPlugin));

			await That(result.Diagnostics.Any(d => d.Contains("AWT140"))).IsFalse()
				.Because("the referenced assembly has concrete plugins assignable to the marker");
		}
	}
}
