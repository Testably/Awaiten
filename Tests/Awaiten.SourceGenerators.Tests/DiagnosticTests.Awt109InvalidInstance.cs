namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt109InvalidInstance
	{
		[Test]
		public async Task ReportsWhenNoFieldOrPropertyOfThatNameHoldsTheServiceType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Instance = nameof(Missing))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static int Missing = 0;
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT109*").AsWildcard();
		}

		[Test]
		public async Task ReportsWhenTheNamedMemberIsNotAFieldOrProperty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Instance = nameof(NotAMember))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static Service NotAMember() => new Service();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT109*").AsWildcard()
				.Because("a method is not a usable pre-built instance member even when its type matches");
		}

		[Test]
		public async Task ReportsWhenTheInstanceNameIsEmpty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Instance = "")]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT109*").AsWildcard()
				.Because("an empty instance name is a mistake, not a silent fall back to the constructor");
		}
	}
}
