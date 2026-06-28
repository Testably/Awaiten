namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt113NonStaticContainer
	{
		[Fact]
		public async Task ReportsForANonStaticContainerClass()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT113*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotReportForAStaticContainerClass()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT113*").AsWildcard();
		}
	}
}
