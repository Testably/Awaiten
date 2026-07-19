using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt202ModuleScanInAssembliesOf
	{
		[Fact]
		public async Task Awt202_ReportsWhenAModuleScanNamesAssemblies()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       internal sealed class Roaster : IPlugin { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.Marker, InAssembliesOf = new[] { typeof(IPlugin) })]
			                                       public static partial class PluginModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT202*PluginModule*").AsWildcard()
				.Because("a self-compiled scan sweeps only the module's own assembly; InAssembliesOf would see just another assembly's public types, which a container [Scan] already covers");
			await That(result.Sources.Keys.Where(key => key.Contains("ModuleScan"))).IsNotEmpty()
				.Because("the module still emits its expansion marker; the rejected scan just contributes no factories");
		}
	}
}
