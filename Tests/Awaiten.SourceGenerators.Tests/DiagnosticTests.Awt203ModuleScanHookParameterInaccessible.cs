namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt203ModuleScanHookParameterInaccessible
	{
		[Fact]
		public async Task ReportsAnInaccessibleHookDependencyParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       public interface IRoaster { }
			                                       internal sealed class Secret { }
			                                       internal sealed class Roaster : IPlugin, IRoaster { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.MatchingInterface, OnActivated = nameof(Wire))]
			                                       public static partial class PluginModule
			                                       {
			                                       	internal static void Wire(Roaster roaster, Secret secret) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT203*Roaster*Secret*").AsWildcard()
				.Because("the wrapper is public and resolves its parameters from the consumer's graph, so a hook dependency type must be nameable outside the module's assembly");
		}
	}
}
