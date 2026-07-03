namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt131ConflictingExternalParameter
	{
		[Fact]
		public async Task ReportsWhenAParameterIsMarkedBothFromServicesAndArg()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { public Service([FromServices][Arg] string value) { } }

			                                       [Container]
			                                       [Transient<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT131*").AsWildcard()
				.And.Contains("*[FromServices]*").AsWildcard()
				.Because("a parameter cannot be both an external dependency and a runtime argument");
		}
	}
}
