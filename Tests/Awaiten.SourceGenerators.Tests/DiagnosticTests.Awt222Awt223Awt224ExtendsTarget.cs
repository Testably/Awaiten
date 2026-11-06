namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt222ExtendsNotAContainer
	{
		[Test]
		public async Task ReportsWhenTheBaseIsNotAContainer()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public static class Helpers { }

			                                       [Container(Extends = typeof(Helpers))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*(7,12)*AWT222*TestShop*Helpers*not marked [Container]*").AsWildcard()
				.Because("a type that is not a [Container] has no composition to inherit, and the error points at the Extends argument");
		}

		[Test]
		public async Task ReportsWhenTheBaseIsAModule()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }

			                                       [Module]
			                                       [Singleton<Clock>]
			                                       public static class ClockModule { }

			                                       [Container(Extends = typeof(ClockModule))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT222*ClockModule*").AsWildcard()
				.Because("a module is imported with [Import], not extended");
		}

		[Test]
		public async Task ReportsWhenTheBaseIsGeneric()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       [Container]
			                                       public static partial class Shop<T>
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop<>))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT222*TestShop*it is generic*").AsWildcard()
				.Because("a generic type has no single composition to extend");
		}

		[Test]
		public async Task DoesNotReportForAContainerBase()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
		}
	}

	public class Awt223ExtendsItself
	{
		[Test]
		public async Task ReportsWhenTheContainerExtendsItself()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT223*Shop*names itself*").AsWildcard();
			await That(result.Diagnostics).DoesNotContain("*AWT224*").AsWildcard()
				.Because("a container naming itself is its own fault, not a chain");
		}

		[Test]
		public async Task DoesNotReportWithoutExtends()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       [Container]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT223*").AsWildcard();
		}
	}

	public class Awt224ExtendsChain
	{
		[Test]
		public async Task ReportsWhenTheBaseExtendsAnotherContainer()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(TestShop))]
			                                       public static partial class SlowTestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT224*SlowTestShop*TestShop*Shop*").AsWildcard()
				.Because("extending is one level deep");
			await That(result.Diagnostics).HasCount(1)
				.Because("the valid one-level TestShop reports nothing");
		}

		[Test]
		public async Task ReportsWhenTheBaseInAReferencedAssemblyExtendsAnotherContainer()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
			                                                             using Awaiten;

			                                                             namespace Production;

			                                                             [Container]
			                                                             public static partial class Shop { }

			                                                             [Container(Extends = typeof(Shop))]
			                                                             public static partial class StagingShop { }
			                                                             """, """
			                                                                  using Awaiten;

			                                                                  namespace MyCode;

			                                                                  [Container(Extends = typeof(Production.StagingShop))]
			                                                                  public static partial class TestShop
			                                                                  {
			                                                                  }
			                                                                  """);

			await That(result.Diagnostics).Contains("*AWT224*TestShop*StagingShop*Shop*").AsWildcard();
		}
	}
}
