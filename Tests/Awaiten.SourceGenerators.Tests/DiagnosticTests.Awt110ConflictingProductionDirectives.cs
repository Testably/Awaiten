using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt110ConflictingProductionDirectives
	{
		[Fact]
		public async Task ReportsWhenAnAttributeSetsBothFactoryAndInstance()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Service : IService { }

			                                       [Container]
			                                       [Singleton<IService>(Factory = nameof(Make), Instance = nameof(_field))]
			                                       public partial class MyContainer
			                                       {
			                                       	private IService Make() => new Service();
			                                       	private readonly IService _field = new Service();
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT110"))).IsTrue()
				.Because("Factory and Instance are mutually exclusive directives");
		}
	}
}
