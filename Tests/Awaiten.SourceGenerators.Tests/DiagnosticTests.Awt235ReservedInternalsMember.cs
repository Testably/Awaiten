namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt235ReservedInternalsMember
	{
		public class Module
		{
			[Test]
			public async Task ANestedClassNamedInternalsInAModuleThatGeneratesMembers_ReportsAwt235()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					internal sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule
					{
					    public static class Internals { }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT235*Lib.GreetingModule*Internals*").AsWildcard();
				await That(result.Diagnostics).DoesNotContain("*CS0102*").AsWildcard()
					.Because("the module skips its generated class instead of colliding with the user's member");
			}

			[Test]
			public async Task AMemberNamedInternalsInAModuleThatGeneratesNothing_ReportsAwt235()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					public sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static class GreetingModule
					{
					    public static int Internals => 0;
					}
					""");

				await That(result.Diagnostics).Contains("*AWT235*Lib.GreetingModule*").AsWildcard()
					.Because("the name stays reserved, so adding a registration later cannot turn it into an error");
			}

			[Test]
			public async Task AContainerImportingAModuleWithAMemberNamedInternals_ReportsOnlyAwt235()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }

					[Module]
					[Singleton<GreetingModule.Greeter, IGreeter>]
					public static partial class GreetingModule
					{
					    private sealed class Greeter : IGreeter { }

					    public static class Internals { }
					}

					[Container]
					[Import(typeof(GreetingModule))]
					public static partial class Shop
					{
					}
					""");

				await That(result.Diagnostics).Contains("*AWT235*Lib.GreetingModule*").AsWildcard();
				await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard()
					.Because("the container does not call into a generated class the module cannot receive");
			}

			[Test]
			public async Task TheGeneratedInternalsClass_IsNotMistakenForAUserMemberByTheAnalyzer()
			{
				string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }

					[Module]
					[Singleton<GreetingModule.Greeter, IGreeter>]
					public static partial class GreetingModule
					{
					    private sealed class Greeter : IGreeter { }
					}

					[Container]
					[Import(typeof(GreetingModule))]
					public static partial class Shop;

					public static class Usage
					{
					    public static void Use()
					    {
					        using Shop.Root root = new();
					        root.Resolve<IGreeter>();
					    }
					}
					""");

				await That(diagnostics).DoesNotContain("*AWT218*").AsWildcard()
					.Because("the analyzer builds the graph over a compilation that contains the generated class, which still lets the container build the module's private type");
			}

			[Test]
			public async Task AMemberWithALongerName_DoesNotReportAwt235()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					internal sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule
					{
					    public static int InternalsCount => 0;
					}
					""");

				await That(result.Diagnostics).IsEmpty();
			}
		}

		public class Container
		{
			[Test]
			public async Task ANestedClassNamedInternalsInABaseContainer_ReportsAwt235()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace MyCode;

					public interface IGrinder { }

					[Container]
					[Singleton<Grinder, IGrinder>]
					public static partial class Shop
					{
					    private sealed class Grinder : IGrinder { }

					    internal static class Internals { }
					}

					[Container(Extends = typeof(Shop))]
					public static partial class TestShop
					{
					}
					""");

				await That(result.Diagnostics).Contains("*AWT235*MyCode.Shop*Internals*").AsWildcard();
				await That(result.Diagnostics).DoesNotContain("*CS0102*").AsWildcard()
					.Because("a container with an error emits only its shell, without the generated class");
			}

			[Test]
			public async Task AMethodNamedInternals_ReportsAwt235()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace MyCode;

					public sealed class Grinder { }

					[Container]
					[Singleton<Grinder>]
					public static partial class Shop
					{
					    private static void Internals() { }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT235*MyCode.Shop*").AsWildcard();
			}

			[Test]
			public async Task AMemberWithALongerName_DoesNotReportAwt235()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace MyCode;

					public sealed class Grinder { }

					[Container]
					[Singleton<Grinder>]
					public static partial class Shop
					{
					    private static void InternalsCheck() { }
					}
					""");

				await That(result.Diagnostics).IsEmpty();
			}
		}
	}
}
