using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt109InvalidInstance
	{
		[Fact]
		public async Task ReportsWhenNoFieldOrPropertyOfThatNameHoldsTheServiceType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Instance = nameof(Missing))]
			                                       public partial class MyContainer
			                                       {
			                                       	private int Missing = 0;
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT109"))).IsTrue();
		}
	}
}
