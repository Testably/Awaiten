using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt189HookParameterIsArg
	{
		[Fact]
		public async Task ReportsWhenAHookParameterIsMarkedArg()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service, [Arg] int count) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT189*").AsWildcard()
				.Because("a runtime [Arg] cannot be supplied to a lifecycle hook - its parameters resolve from the graph");
		}

		[Fact]
		public async Task DoesNotReportForAGraphResolvedHookParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }
			                                       public sealed class Settings { }

			                                       [Container]
			                                       [Singleton<Settings>]
			                                       [Transient<Service>(OnRelease = nameof(Released))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Released(Service service, Settings settings) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT189*").AsWildcard()
				.Because("a hook parameter resolved from the graph is not a runtime argument");
		}

		[Fact]
		public async Task ReportsAnArgParameterOnAModuleScanHook()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       public interface IRoaster { }
			                                       internal sealed class Roaster : IPlugin, IRoaster { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.MatchingInterface, OnActivated = nameof(Wire))]
			                                       public static partial class PluginModule
			                                       {
			                                       	internal static void Wire(Roaster roaster, [Arg] int count) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT189*count*").AsWildcard()
				.Because("a module hook parameter cannot be a runtime [Arg] either; the wrapper has no call site to supply one");
		}
	}
}
