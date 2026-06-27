using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt101MissingDependency
	{
		[Fact]
		public async Task ReportsWhenAConstructorParameterIsNotRegistered()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IMissing { }
			                                       public sealed class Service { public Service(IMissing missing) { } }

			                                       [Container]
			                                       [Transient<Service>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT101"))).IsTrue();
		}
	}
}
