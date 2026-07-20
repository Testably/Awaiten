using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt200Awt201ModuleScanLimitations
	{
		[Fact]
		public async Task Awt200_ReportsWhenAMatchHasAnInjectProperty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGrinder { }
			                                       public interface IPlugin { }
			                                       public interface IRoaster { }

			                                       internal sealed class Roaster : IPlugin, IRoaster
			                                       {
			                                           [Inject]
			                                           public IGrinder? Grinder { get; set; }
			                                       }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			                                       public static partial class PluginModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT200*Roaster*Grinder*[Inject]*").AsWildcard()
				.Because("an [Inject] property's semantics cannot be mirrored by the generated factory, so silently skipping it would construct the match differently than a container scan");
		}

		[Fact]
		public async Task Awt200_ReportsWhenAMatchConstructorParameterIsMarkedArg()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       public interface IRoaster { }

			                                       internal sealed class Roaster : IPlugin, IRoaster
			                                       {
			                                           public Roaster([Arg] int strength) { }
			                                       }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			                                       public static partial class PluginModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT200*Roaster*strength*[Arg]*").AsWildcard()
				.Because("an [Arg] parameter takes its value from the resolution call, which a mirrored factory parameter resolved from the graph would silently change");
		}

		[Fact]
		public async Task Awt201_ReportsForAGenericModuleWithAScan()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       public interface IRoaster { }
			                                       internal sealed class Roaster : IPlugin, IRoaster { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			                                       public static partial class PluginModule<T> { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT201*PluginModule*").AsWildcard()
				.Because("no closed module exists for a consumer to import, and a bare-named partial would emit an unrelated non-generic class");
			await That(result.Sources.Keys.Where(key => key.Contains("ModuleScan"))).IsEmpty()
				.Because("the orphan partial must not be emitted");
		}

		[Fact]
		public async Task Awt201_ReportsForAModuleNestedInAGenericTypeWithAScan()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       public interface IRoaster { }
			                                       internal sealed class Roaster : IPlugin, IRoaster { }

			                                       public static partial class Outer<T>
			                                       {
			                                           [Module]
			                                           [Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			                                           public static partial class PluginModule { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT201*PluginModule*").AsWildcard()
				.Because("a module nested in a generic type is just as unimportable as a generic module itself");
		}
	}
}
