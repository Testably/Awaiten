using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt194ModuleScanNotPartial
	{
		[Fact]
		public async Task ReportsWhenAModuleWithAScanIsNotPartial()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       public interface IRoaster { }
			                                       internal sealed class Roaster : IPlugin, IRoaster { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			                                       public static class PluginModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT194*PluginModule*").AsWildcard()
				.Because("the module declares a [Scan] but is not partial, so the generated factories cannot be added");
		}

		[Fact]
		public async Task DoesNotReportForAPartialModuleWithAScan()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       public interface IRoaster { }
			                                       internal sealed class Roaster : IPlugin, IRoaster { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			                                       public static partial class PluginModule { }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT194*").AsWildcard()
				.Because("a partial module can receive the generated factories");
		}

		[Fact]
		public async Task DoesNotReportForAPlainModuleWithoutAScan()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public sealed class Logger { }

			                                       [Module]
			                                       [Singleton<Logger>]
			                                       public static class LoggingModule { }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT194*").AsWildcard()
				.Because("a module without a [Scan] self-compiles nothing, so it need not be partial");
		}
	}
}
