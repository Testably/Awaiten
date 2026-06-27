using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt111ConflictingProduction
	{
		[Fact]
		public async Task ReportsWhenAnImplementationIsRegisteredWithDifferentProductionStrategies()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Factory = nameof(Make))]
			                                       [Singleton<Service>]
			                                       public partial class MyContainer
			                                       {
			                                       	private Service Make() => new Service();
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT111"))).IsTrue()
				.Because("coalescing keeps the first production strategy, so the contradicting one must not be silently dropped");
		}
	}
}
