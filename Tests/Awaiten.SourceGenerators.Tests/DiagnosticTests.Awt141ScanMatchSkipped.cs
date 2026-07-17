using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt141ScanMatchSkipped
	{
		[Fact]
		public async Task SkipUnconstructable_SkipsAnUnconstructableScanMatchWithAWarning()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class NeedsName : IPlugin
			                                       {
			                                           public NeedsName(string name) { }
			                                       }
			                                       public sealed class OkPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), SkipUnconstructable = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT141*NeedsName*").AsWildcard()
				.Because("the opted-in scan degrades an unconstructable match to a skip-with-warning");
			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the skipped match is not also reported as a missing dependency");

			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("new global::MyCode.OkPlugin()")
				.Because("the constructable match is still registered");
			await That(source).DoesNotContain("NeedsName")
				.Because("the unconstructable match is dropped from the container");
		}

		[Fact]
		public async Task SkipUnconstructable_KeepsAMatchWithAnEmptyInjectedAwaitedCollectionMember()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IExtension { }
			                                       public interface IPlugin { }
			                                       public sealed class Host : IPlugin
			                                       {
			                                           [Inject]
			                                           public Task<IReadOnlyList<IExtension>> Extensions { get; set; }
			                                       }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), SkipUnconstructable = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			// An awaited collection member is always satisfiable (an unregistered element type yields an empty
			// collection), so it never makes a scanned match unconstructable.
			await That(result.Diagnostics).IsEmpty()
				.Because("an empty awaited collection member does not make a scanned match unconstructable");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

			await That(source).Contains("new global::MyCode.Host()")
				.Because("the match is registered with its awaited collection member filled");
		}

		[Fact]
		public async Task Default_KeepsTheMissingDependencyError()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class NeedsName : IPlugin
			                                       {
			                                           public NeedsName(string name) { }
			                                       }

			                                       [Container]
			                                       [Scan(typeof(IPlugin))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
				.Because("without the opt-in, an unconstructable scan match stays a hard error");
			await That(result.Diagnostics).DoesNotContain("*AWT141*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotSkipAnExplicitlyRegisteredImplementation()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class NeedsName : IPlugin
			                                       {
			                                           public NeedsName(string name) { }
			                                       }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), SkipUnconstructable = true)]
			                                       [Transient<NeedsName>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
				.Because("asking for the type by name makes its missing dependency a real fault again");
			await That(result.Diagnostics).DoesNotContain("*AWT141*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotSkipAMatchAlsoMatchedByAScanWithoutTheOptIn()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IA { }
			                                       public interface IB { }
			                                       public sealed class Both : IA, IB
			                                       {
			                                           public Both(string name) { }
			                                       }

			                                       [Container]
			                                       [Scan(typeof(IA), SkipUnconstructable = true)]
			                                       [Scan(typeof(IB))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
				.Because("the scan without the opt-in pins the shared match to error semantics");
			await That(result.Diagnostics).DoesNotContain("*AWT141*").AsWildcard();
		}

		[Fact]
		public async Task SkipsAMatchOrphanedByAnotherSkippedMatch()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class NeedsName : IPlugin
			                                       {
			                                           public NeedsName(string name) { }
			                                       }
			                                       public sealed class NeedsNeedsName : IPlugin
			                                       {
			                                           public NeedsNeedsName(NeedsName inner) { }
			                                       }
			                                       public sealed class OkPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), SkipUnconstructable = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			// Dropping NeedsName orphans NeedsNeedsName, so the prune iterates to a fixpoint and drops it too
			// (with its own warning) rather than leaving an AWT101.
			await That(result.Diagnostics).Contains("*AWT141*NeedsName*").AsWildcard()
				.And.Contains("*AWT141*NeedsNeedsName*").AsWildcard();
			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard();

			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("new global::MyCode.OkPlugin()");
			await That(source).DoesNotContain("NeedsName");
		}
	}
}
