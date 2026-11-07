using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt195Awt196Awt197ModuleScanExposure
	{
		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
		public async Task Awt195_NamesAParameterTypeNestedPrivatelyInTheModule()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public interface IFormatter { }

			                                       [Module]
			                                       [Singleton<Formatter, IFormatter>]
			                                       [Singleton<Greeter, IGreeter>]
			                                       public static partial class GreeterModule
			                                       {
			                                           private sealed class Formatter : IFormatter { }

			                                           private sealed class Greeter : IGreeter
			                                           {
			                                               public Greeter(Formatter formatter) { }
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT195*'Lib.GreeterModule.Greeter' is self-compiled from a module registration, but its constructor parameter 'formatter' needs 'Lib.GreeterModule.Formatter', which is private to the module or a class containing it, so no container can register or resolve it; depend on an accessible service that 'Lib.GreeterModule.Formatter' implements instead*").AsWildcard()
				.Because("only the private type itself could satisfy the parameter, and no container can register a service it cannot name");
			await That(result.Diagnostics).DoesNotContain("*not accessible outside the module's assembly*").AsWildcard()
				.Because("widening the parameter's accessibility is not the remedy for a type that has to stay private");
		}

		[Test]
		public async Task Awt195_NamesAParameterTypeNestedPrivatelyInTheModuleForAScanMatch()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Collections.Generic;
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.Marker)]
			                                       public static partial class PluginModule
			                                       {
			                                           private sealed class Part { }

			                                           private sealed class FakePlugin : IPlugin
			                                           {
			                                               public FakePlugin(IEnumerable<Part> parts) { }
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT195*'Lib.PluginModule.FakePlugin' matched the module scan, but its constructor parameter 'parts' needs 'Lib.PluginModule.Part', which is private to the module or a class containing it*").AsWildcard()
				.Because("a private type argument hides the parameter from every container just as the private type itself does");
		}

		[Test]
		public async Task Awt195_NamesAParameterTypePrivateToAClassContainingTheModuleForAScanMatch()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }

			                                       public static partial class Fakes
			                                       {
			                                           private sealed class Part { }

			                                           private sealed class FakePlugin : IPlugin
			                                           {
			                                               public FakePlugin(Part part) { }
			                                           }

			                                           [Module]
			                                           [Scan<IPlugin>(As = ScanAs.Marker)]
			                                           public static partial class PluginModule { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT195*'Lib.Fakes.FakePlugin' matched the module scan, but its constructor parameter 'part' needs 'Lib.Fakes.Part', which is private to the module or a class containing it*").AsWildcard()
				.Because("a type private to a class containing the module is just as unregistrable as one nested in the module, so widening it is not the remedy");
			await That(result.Diagnostics).DoesNotContain("*not accessible outside the module's assembly*").AsWildcard();
		}
	}
}
