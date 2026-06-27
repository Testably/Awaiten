using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt108InvalidFactory
	{
		[Fact]
		public async Task ReportsWhenNoMethodOfThatNameReturnsTheServiceType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Factory = nameof(Missing))]
			                                       public partial class MyContainer
			                                       {
			                                       	private int Missing() => 0;
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT108"))).IsTrue();
		}
	}
}
