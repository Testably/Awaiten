using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt152NonStaticModule
	{
		[Fact]
		public async Task ReportsWhenAnImportedModuleIsNotStatic()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Logger { }

			                                       [Module]
			                                       [Singleton<Logger>]
			                                       public sealed class InfrastructureModule { }

			                                       [Container]
			                                       [Import(typeof(InfrastructureModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT152*InfrastructureModule*").AsWildcard()
				.Because("a module, like a container, is a pure definition and must be a static class");
		}

		[Fact]
		public async Task StillImportsTheNonStaticModulesRegistrations()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Logger { }
			                                       public sealed class Consumer
			                                       {
			                                           public Consumer(Logger logger) { }
			                                       }

			                                       [Module]
			                                       [Singleton<Logger>]
			                                       public sealed class InfrastructureModule { }

			                                       [Container]
			                                       [Import(typeof(InfrastructureModule))]
			                                       [Singleton<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the module is still imported, so its registrations do not additionally cascade as missing dependencies");
		}

		[Fact]
		public async Task DoesNotReportForAStaticModule()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Logger { }

			                                       [Module]
			                                       [Singleton<Logger>]
			                                       public static class InfrastructureModule { }

			                                       [Container]
			                                       [Import(typeof(InfrastructureModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT152*").AsWildcard()
				.Because("a static module satisfies the requirement");
		}

		[Fact]
		public async Task DoesNotReportForANonModuleImport_WhichAlreadyFailsWithAwt149()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Logger { }

			                                       [Singleton<Logger>]
			                                       public sealed class NotAModule { }

			                                       [Container]
			                                       [Import(typeof(NotAModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT152*").AsWildcard()
				.Because("a non-module import is rejected by AWT149 before the static requirement applies");
		}
	}
}
