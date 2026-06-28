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
			                                       public static partial class MyContainer
			                                       {
			                                       	private static int Missing() => 0;
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT108*").AsWildcard();
		}

		[Fact]
		public async Task ReportsWhenTheNamedMemberIsNotAMethod()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Factory = nameof(NotAMethod))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static Service NotAMethod = new Service();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT108*").AsWildcard()
				.Because("a field is not a usable factory method even when its type matches");
		}

		[Fact]
		public async Task ReportsWhenTheFactoryNameIsEmpty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Factory = "")]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT108*").AsWildcard()
				.Because("an empty factory name is a mistake, not a silent fall back to the constructor");
		}
	}
}
