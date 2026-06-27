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

		[Fact]
		public async Task ReportsWhenTheNamedMemberIsNotAFieldOrProperty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Instance = nameof(NotAMember))]
			                                       public partial class MyContainer
			                                       {
			                                       	private Service NotAMember() => new Service();
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT109"))).IsTrue()
				.Because("a method is not a usable pre-built instance member even when its type matches");
		}

		[Fact]
		public async Task ReportsWhenTheInstanceNameIsEmpty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Instance = "")]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT109"))).IsTrue()
				.Because("an empty instance name is a mistake, not a silent fall back to the constructor");
		}
	}
}
