using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt195Awt196Awt197ModuleScanExposure
	{
		[Fact]
		public async Task Awt195_ReportsWhenAMatchConstructorParameterIsInaccessible()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       public interface IRoaster { }
			                                       internal sealed class Secret { }

			                                       internal sealed class Roaster : IPlugin, IRoaster
			                                       {
			                                           public Roaster(Secret secret) { }
			                                       }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			                                       public static partial class PluginModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT195*Roaster*Secret*").AsWildcard()
				.Because("the generated factory's parameter type must be nameable by a consumer, and Secret is internal");
		}

		[Fact]
		public async Task Awt196_ReportsWhenNoExposureIsAccessible()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       internal sealed class Roaster : IPlugin { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.Self)]
			                                       public static partial class PluginModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*warning AWT196*Roaster*").AsWildcard()
				.Because("a Self exposure of an internal implementation gives a consumer no accessible type to resolve; the match is skipped with a warning, mirroring the container scan's AWT182/AWT188/AWT193 severity");
		}

		[Fact]
		public async Task Awt197_ReportsWhenAMatchWouldHaveMultipleExposures()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       public interface IRoaster { }
			                                       internal sealed class Roaster : IPlugin, IRoaster { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.Marker | ScanAs.MatchingInterface)]
			                                       public static partial class PluginModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT197*Roaster*").AsWildcard()
				.Because("a self-compiled match is reached through a single-interface factory, so multiple exposures are unsupported in v1");
		}

		[Fact]
		public async Task Awt197_ReportsWhenTwoScansExposeOneMatchUnderDifferentInterfaces()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       public interface IRoaster { }
			                                       internal sealed class Roaster : IPlugin, IRoaster { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.Marker)]
			                                       [Scan<IRoaster>(As = ScanAs.Marker)]
			                                       public static partial class PluginModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT197*Roaster*IPlugin, Lib.IRoaster*").AsWildcard()
				.Because("two scans exposing the same type under different interfaces would emit two factories, splitting the single shared instance a container scan gives one implementation, so the overlap is rejected like a multi-exposure within one scan");
		}

		[Fact]
		public async Task DoesNotReportForASingleAccessibleExposure()
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

			await That(result.Diagnostics).DoesNotContain("*AWT195*").AsWildcard();
			await That(result.Diagnostics).DoesNotContain("*AWT196*").AsWildcard();
			await That(result.Diagnostics).DoesNotContain("*AWT197*").AsWildcard();
		}
	}
}
