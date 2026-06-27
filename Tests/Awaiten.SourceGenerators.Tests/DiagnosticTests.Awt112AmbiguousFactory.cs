using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt112AmbiguousFactory
	{
		[Fact]
		public async Task ReportsWhenTheFactoryNameMatchesMoreThanOneMethod()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Factory = nameof(Make))]
			                                       public partial class MyContainer
			                                       {
			                                       	private Service Make() => new Service();
			                                       	private Service Make(int x) => new Service();
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT112"))).IsTrue()
				.Because("an overloaded factory name makes the producer choice order-dependent");
		}
	}
}
