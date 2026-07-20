namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt154ModuleScanNotExpanded
	{
		[Fact]
		public async Task Awt154_ReportsWhenTheModuleAssemblyWasBuiltWithoutTheGenerator()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IClock { }
				public sealed class SystemClock : IClock { }
				public interface IPlugin { }

				internal sealed class Roaster : IPlugin { }

				[Module]
				[Singleton<SystemClock, IClock>]
				[Scan<IPlugin>(As = ScanAs.Marker)]
				public static partial class PluginModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(PluginModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT154*PluginModule*").AsWildcard()
				.Because("the module's metadata carries a [Scan] but no generated expansion marker, so the scan would silently contribute nothing; the module declaring another registration must not mask that");
		}

		[Fact]
		public async Task Awt154_ReportsForAScanOnlyModuleWithoutMisleadingAwt151()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IPlugin { }
				internal sealed class Roaster : IPlugin { }

				[Module]
				[Scan<IPlugin>(As = ScanAs.Marker)]
				public static partial class PluginModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(PluginModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT154*PluginModule*").AsWildcard()
				.Because("the unexpanded scan is the actual fault");
			await That(result.Diagnostics).DoesNotContain("*AWT151*").AsWildcard()
				.Because("a module declaring a [Scan] is not an empty module; AWT154 names the real problem instead");
		}

		[Fact]
		public async Task NoAwt154WhenTheGeneratorExpandedAScanThatMatchedNothing()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IPlugin { }

				[Module]
				[Scan<IPlugin>(As = ScanAs.Marker)]
				public static partial class PluginModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(PluginModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).DoesNotContain("*AWT154*").AsWildcard()
				.Because("the generated expansion marker is emitted even for a scan that matched nothing, so an empty expansion is distinguishable from a missing one");
			await That(result.Diagnostics).DoesNotContain("*AWT151*").AsWildcard()
				.Because("the empty scan was already reported in the module's own build, not blamed on the consumer's [Import]");
		}
	}
}
