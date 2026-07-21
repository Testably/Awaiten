using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt204ModuleScanHookInjectionMetadata
	{
		[Fact]
		public async Task ReportsAFromKeyHookDependencyParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IClock { }
			                                       public interface IPlugin { }
			                                       public interface IRoaster { }
			                                       internal sealed class Roaster : IPlugin, IRoaster { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.MatchingInterface, OnActivated = nameof(Wire))]
			                                       public static partial class PluginModule
			                                       {
			                                       	internal static void Wire(Roaster roaster, [FromKey("main")] IClock clock) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT204*clock*FromKey*").AsWildcard()
				.Because("the wrapper mirrors a bare type-and-name signature, so a [FromKey] would be silently dropped cross-assembly while a same-compilation container honored it");
		}

		[Fact]
		public async Task ReportsOnceWhenOverlappingScansNameTheSameFailingHook()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IClock { }
			                                       public interface IMarkerA { }
			                                       public interface IMarkerB { }
			                                       public interface IWorker { }

			                                       internal sealed class Worker : IMarkerA, IMarkerB, IWorker { }

			                                       [Module]
			                                       [Scan<IMarkerA>(As = ScanAs.MatchingInterface, OnActivated = nameof(Wire))]
			                                       [Scan<IMarkerB>(As = ScanAs.MatchingInterface, OnActivated = nameof(Wire))]
			                                       public static partial class WorkerModule
			                                       {
			                                       	internal static void Wire(Worker worker, [FromKey("main")] IClock clock) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Count(diagnostic => diagnostic.Contains("AWT204"))).IsEqualTo(1)
				.Because("the second overlapping scan restates the same failed hook for the slot merge, but the first scan already reported it");
		}
	}
}
