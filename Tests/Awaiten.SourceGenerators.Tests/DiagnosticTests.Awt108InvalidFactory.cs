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

		[Fact]
		public async Task ReportsWhenTheNamedMemberIsNotAMethod()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Factory = nameof(NotAMethod))]
			                                       public partial class MyContainer
			                                       {
			                                       	private Service NotAMethod = new Service();
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT108"))).IsTrue()
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
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT108"))).IsTrue()
				.Because("an empty factory name is a mistake, not a silent fall back to the constructor");
		}
	}
}
