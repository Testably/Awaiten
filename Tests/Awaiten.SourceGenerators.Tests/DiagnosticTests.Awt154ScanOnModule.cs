using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt154ScanOnModule
	{
		[Fact]
		public async Task ReportsWhenAnImportedModuleDeclaresAScan()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }
			                                       public sealed class Logger { }

			                                       [Module]
			                                       [Scan(typeof(IPlugin), Lifetime = AwaitenLifetime.Singleton)]
			                                       [Singleton<Logger>]
			                                       public static class PluginModule { }

			                                       [Container]
			                                       [Import(typeof(PluginModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT154*PluginModule*").AsWildcard()
				.Because("a [Scan] is not collected from modules, so it would be silently dropped without the error");
		}

		[Fact]
		public async Task StillImportsTheModulesLifetimeRegistrations_AndDoesNotAlsoReportAwt151()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }
			                                       public sealed class Logger { }
			                                       public sealed class Consumer
			                                       {
			                                           public Consumer(Logger logger) { }
			                                       }

			                                       [Module]
			                                       [Scan(typeof(IPlugin), Lifetime = AwaitenLifetime.Singleton)]
			                                       [Singleton<Logger>]
			                                       public static class PluginModule { }

			                                       [Container]
			                                       [Import(typeof(PluginModule))]
			                                       [Singleton<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the module's lifetime registrations are still imported");
			await That(result.Diagnostics).DoesNotContain("*AWT151*").AsWildcard()
				.Because("the module declares a lifetime registration, so it is not empty");
		}

		[Fact]
		public async Task ReportsAlongsideAwt151ForAScanOnlyModule()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }

			                                       [Module]
			                                       [Scan(typeof(IPlugin), Lifetime = AwaitenLifetime.Singleton)]
			                                       public static class PluginModule { }

			                                       [Container]
			                                       [Import(typeof(PluginModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			// The scan is rejected (AWT154) and does not count as a contribution, so the module is also empty (AWT151).
			await That(result.Diagnostics).Contains("*AWT154*PluginModule*").AsWildcard();
			await That(result.Diagnostics).Contains("*AWT151*PluginModule*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotReportForAScanOnTheContainerItself()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }
			                                       public sealed class Logger { }

			                                       [Module]
			                                       [Singleton<Logger>]
			                                       public static class LoggingModule { }

			                                       [Container]
			                                       [Import(typeof(LoggingModule))]
			                                       [Scan(typeof(IPlugin), Lifetime = AwaitenLifetime.Singleton)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT154*").AsWildcard()
				.Because("[Scan] on the container is the supported placement");
		}
	}
}
