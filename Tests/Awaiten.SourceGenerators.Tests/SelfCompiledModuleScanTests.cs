using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     A <c>[Module]</c> that compiles its own <c>[Scan]</c>: the module's build emits a factory + registration per
///     match, so a consuming container resolves an <c>internal</c> implementation through its accessible interface
///     without ever naming the implementation. The library keeps implementations internal and exposes only interfaces.
/// </summary>
public class SelfCompiledModuleScanTests
{
	/// <summary>
	///     Mirrors the generator's factory naming (<c>ModuleFactoryName</c>): the match's simple name plus an
	///     FNV-1a hash of its fully-qualified name, stable across library versions and scan order.
	/// </summary>
	private static string FactoryName(string fullyQualifiedName)
	{
		uint hash = 2166136261;
		foreach (char character in fullyQualifiedName)
		{
			hash = unchecked((hash ^ character) * 16777619);
		}

		string simpleName = fullyQualifiedName[(fullyQualifiedName.LastIndexOf('.') + 1)..];
		return $"Scan_{simpleName}_{hash:x8}";
	}

	private const string LibrarySource = """
	                                     using Awaiten;

	                                     namespace Lib;

	                                     public interface IClock { }
	                                     public sealed class SystemClock : IClock { }

	                                     public interface IPlugin { }
	                                     public interface IRoaster { }

	                                     internal sealed class Roaster : IPlugin, IRoaster
	                                     {
	                                         public Roaster(IClock clock) { }
	                                     }

	                                     [Module]
	                                     [Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton)]
	                                     public static partial class PluginModule { }
	                                     """;

	[Test]
	public async Task ModuleSelfCompilesItsScanIntoAFactory()
	{
		(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) =
			Generator.RunGenerator(LibrarySource, [], []);
		string module = run.Results
			.SelectMany(r => r.GeneratedSources)
			.Single(s => s.HintName.Contains("ModuleScan"))
			.SourceText.ToString();

		await That(module).Contains("static partial class PluginModule")
			.Because("the module is re-opened as partial to receive the generated factory");
		await That(module).Contains("[global::Awaiten.GeneratedScanExpansionAttribute]")
			.Because("the expansion marker lets a consuming container tell an expanded scan from one built without the generator (AWT154)");
		await That(module).Contains($"global::Awaiten.GeneratedScanRegistrationAttribute<global::Lib.IRoaster>(\"Internals.{FactoryName("global::Lib.Roaster")}\", Lifetime = global::Awaiten.Lifetime.Singleton, Implementation = \"Lib.Roaster\")")
			.Because("the match is registered under its accessible MatchingInterface, as a singleton scan match, naming the implementation so a consumer shares its instance with other registrations of it");
		await That(module).Contains($"public static global::Lib.IRoaster {FactoryName("global::Lib.Roaster")}(global::Lib.IClock @clock) => new global::Lib.Roaster(@clock);")
			.Because("the factory returns the interface but constructs the internal implementation in the library, with verbatim-prefixed parameter names so keyword-named parameters stay legal");
	}

	[Test]
	public async Task NullableConstructorParameterKeepsItsAnnotationOnTheFactory()
	{
		(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator("""
			#nullable enable
			using Awaiten;

			namespace Lib;

			public interface IClock { }
			public interface IPlugin { }
			public interface IRoaster { }

			internal sealed class Roaster : IPlugin, IRoaster
			{
			    public Roaster(IClock? clock) { }
			}

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			public static partial class PluginModule { }
			""", [], []);
		string module = run.Results
			.SelectMany(r => r.GeneratedSources)
			.Single(s => s.HintName.Contains("ModuleScan"))
			.SourceText.ToString();

		await That(module).Contains($"public static global::Lib.IRoaster {FactoryName("global::Lib.Roaster")}(global::Lib.IClock? @clock)")
			.Because("the generated public factory must mirror the scanned constructor exactly, keeping the nullable annotation rather than widening the dependency to non-nullable");
	}

	[Test]
	public async Task ConsumerResolvesTheInternalImplementationThroughItsInterface()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(LibrarySource, """
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof(PluginModule))]
			[Singleton<SystemClock, IClock>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("the internal implementation resolves through its interface with no missing dependency");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains($"global::Lib.PluginModule.Internals.{FactoryName("global::Lib.Roaster")}")
			.Because("the container calls the module's generated factory to build the implementation");
		await That(source).DoesNotContain("new global::Lib.Roaster")
			.Because("the consumer never constructs the internal implementation directly");
		await That(result.Diagnostics).DoesNotContain("*AWT154*").AsWildcard()
			.Because("a [Scan] on a module is now supported, not rejected");
	}

	[Test]
	public async Task ConsumerOverridesTheGeneratedRegistration()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(LibrarySource, """
			using Awaiten;
			using Lib;

			namespace MyCode;

			public sealed class AppRoaster : IRoaster { }

			[Container]
			[Import(typeof(PluginModule))]
			[Singleton<SystemClock, IClock>]
			[Singleton<AppRoaster, IRoaster>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("an explicit registration outranks a scan match, so the container's own IRoaster wins without a conflict");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("global::MyCode.AppRoaster")
			.Because("the container's own registration wins the IRoaster slot");
	}

	[Test]
	public async Task InternalDisposableImplementationIsDisposedThroughTheRuntimeCheck()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			using System;
			using Awaiten;

			namespace Lib;

			public interface IClock { }
			public sealed class SystemClock : IClock { }
			public interface IPlugin { }
			public interface IRoaster { }

			internal sealed class Roaster : IPlugin, IRoaster, IDisposable
			{
			    public Roaster(IClock clock) { }
			    public void Dispose() { }
			}

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton)]
			public static partial class PluginModule { }
			""", """
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof(PluginModule))]
			[Singleton<SystemClock, IClock>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("global::System.IDisposable")
			.Because("a factory returning an interface tracks disposal behind a runtime IDisposable check, so the internal disposable is disposed");
	}

	private const string PluginFamilySource = """
	                                          using Awaiten;

	                                          namespace Lib;

	                                          public interface IPlugin { }

	                                          internal sealed class Alpha : IPlugin { }
	                                          internal sealed class Bravo : IPlugin { }

	                                          [Module]
	                                          [Scan<IPlugin>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
	                                          public static partial class PluginModule { }
	                                          """;

	[Test]
	public async Task MultipleMatchesUnderOneInterfaceResolveAsACollection()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(PluginFamilySource, """
			using System.Collections.Generic;
			using Awaiten;
			using Lib;

			namespace MyCode;

			public sealed class Host
			{
			    public Host(IEnumerable<IPlugin> plugins) { }
			}

			[Container]
			[Import(typeof(PluginModule))]
			[Singleton<Host>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("two internal implementations of one marker interface collect instead of colliding (no AWT111)");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains($"global::Lib.PluginModule.Internals.{FactoryName("global::Lib.Alpha")}")
			.Because("the first match is a member of the IEnumerable<IPlugin> collection");
		await That(source).Contains($"global::Lib.PluginModule.Internals.{FactoryName("global::Lib.Bravo")}")
			.Because("the second match is a member of the IEnumerable<IPlugin> collection too, exactly as a container scan would collect them");
	}

	[Test]
	public async Task MultipleMatchesUnderOneInterfaceDoNotConflict()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(PluginFamilySource, """
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof(PluginModule))]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).DoesNotContain("*AWT111*").AsWildcard()
			.Because("self-compiled scan matches are collection-eligible, not conflicting single-service factories");
	}

	[Test]
	public async Task TwoModulesWithSameNamedMatchesUnderOneInterfaceBothCollect()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			using Awaiten;

			namespace Lib
			{
			    public interface IPlugin { }

			    [Module]
			    [Scan<IPlugin>(As = ScanAs.Marker, NamespacePatterns = new[] { "Lib.A" })]
			    public static partial class ModuleA { }

			    [Module]
			    [Scan<IPlugin>(As = ScanAs.Marker, NamespacePatterns = new[] { "Lib.B" })]
			    public static partial class ModuleB { }
			}

			namespace Lib.A
			{
			    internal sealed class Handler : Lib.IPlugin { }
			}

			namespace Lib.B
			{
			    internal sealed class Handler : Lib.IPlugin { }
			}
			""", """
			using System.Collections.Generic;
			using Awaiten;
			using Lib;

			namespace MyCode;

			public sealed class Host
			{
			    public Host(IEnumerable<IPlugin> plugins) { }
			}

			[Container]
			[Import(typeof(ModuleA))]
			[Import(typeof(ModuleB))]
			[Singleton<Host>]
			public static partial class MyContainer
			{
			}
			""");

		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains($"global::Lib.ModuleA.Internals.{FactoryName("global::Lib.A.Handler")}")
			.Because("module A's match is a member of the collection");
		await That(source).Contains($"global::Lib.ModuleB.Internals.{FactoryName("global::Lib.B.Handler")}")
			.Because("module B's match must not be deduped away by sharing module A's factory name, and the name hash keeps same-named matches distinct");
	}

	[Test]
	public async Task SameCompilationModuleScanIsExpandedByTheContainer()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace Lib;

			public interface IClock { }
			public sealed class SystemClock : IClock { }

			public interface IPlugin { }
			public interface IRoaster { }

			internal sealed class Roaster : IPlugin, IRoaster
			{
			    public Roaster(IClock clock) { }
			}

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			public static partial class PluginModule { }

			[Container]
			[Import(typeof(PluginModule))]
			[Singleton<SystemClock, IClock>]
			public static partial class MyContainer { }
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("a same-compilation module's [Scan] is expanded by the container itself, so nothing is dropped and AWT151 does not misfire");
		await That(result.Diagnostics).DoesNotContain("*AWT134*").AsWildcard()
			.Because("the generated factory's 'new' of the internal implementation sits inside the [Module], which the boundary analyzer already treats as legitimate composition code, so it is not flagged as construction outside a composition root");
		string source = result.Sources["Awaiten.Lib.MyContainer.g.cs"];
		await That(source).Contains("new global::Lib.Roaster(")
			.Because("with no assembly boundary the container constructs the internal match directly, as if the [Scan] were its own");
	}

	[Test]
	public async Task SameCompilationModuleScanReportsScanDiagnosticsOnce()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace Lib;

			public interface IPlugin { }

			[Module]
			[Scan<IPlugin>]
			public static partial class PluginModule { }

			[Container]
			[Import(typeof(PluginModule))]
			public static partial class MyContainer { }
			""");

		await That(result.Diagnostics.Count(diagnostic => diagnostic.Contains("AWT138"))).IsEqualTo(1)
			.Because("the module pipeline and the container both see the same [Scan], but only the module pipeline reports on it");
	}

	[Test]
	public async Task KeywordNamedConstructorParameterCompilesAndResolves()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			using Awaiten;

			namespace Lib;

			public interface IClock { }
			public sealed class SystemClock : IClock { }
			public interface IPlugin { }
			public interface IRoaster { }

			internal sealed class Roaster : IPlugin, IRoaster
			{
			    public Roaster(IClock @event) { }
			}

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			public static partial class PluginModule { }
			""", """
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof(PluginModule))]
			[Singleton<SystemClock, IClock>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("a keyword-named constructor parameter must be emitted verbatim-prefixed, not break the module's build");
	}

	[Test]
	public async Task OverlappingScansOnOneModuleEmitASingleFactoryPerMatch()
	{
		(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator("""
			using Awaiten;

			namespace Lib;

			public interface IPlugin { }
			public interface IRoaster { }
			internal sealed class Roaster : IPlugin, IRoaster { }

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			[Scan<IRoaster>(As = ScanAs.MatchingInterface)]
			public static partial class PluginModule { }
			""", [], []);
		string module = run.Results
			.SelectMany(r => r.GeneratedSources)
			.Single(s => s.HintName.Contains("ModuleScan"))
			.SourceText.ToString();

		await That(module).Contains(FactoryName("global::Lib.Roaster"))
			.Because("the first scan's match is emitted");
		await That(module.Split("public static global::").Length - 1).IsEqualTo(1)
			.Because("the second scan matched the same type under the same exposure, which a container would dedup to one collection member, so only one factory is emitted");
	}

	[Test]
	public async Task SkipUnconstructableMatchIsPrunedAtTheConsumer()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			using Awaiten;

			namespace Lib;

			public interface IExotic { }
			public interface IPlugin { }
			public interface IRoaster { }
			public interface IGrinder { }

			internal sealed class Roaster : IPlugin, IRoaster
			{
			    public Roaster(IExotic exotic) { }
			}

			internal sealed class Grinder : IPlugin, IGrinder { }

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, SkipUnconstructable = true)]
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

		await That(result.Diagnostics).Contains("*AWT141*IRoaster*IExotic*").AsWildcard()
			.Because("the scan's SkipUnconstructable travels on the generated registration, so the consumer prunes the match whose factory parameter it cannot resolve, with the same AWT141 a container scan gives");
		await That(result.Diagnostics).DoesNotContain("*error*").AsWildcard()
			.Because("pruning replaces the missing-dependency error");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains($"global::Lib.PluginModule.Internals.{FactoryName("global::Lib.Grinder")}")
			.Because("the constructable match survives the prune");
	}

	private const string RoasterProviderLibrarySource = """
	                                                    using Awaiten;

	                                                    namespace Lib;

	                                                    public interface IPlugin { }
	                                                    public interface IRoaster { }

	                                                    internal sealed class Roaster : IPlugin, IRoaster { }

	                                                    [Module]
	                                                    [Scan<IPlugin>(As = ScanAs.MatchingInterface)]
	                                                    public static partial class PluginModule { }
	                                                    """;

	private const string ScanningConsumerSource = """
	                                              using Awaiten;
	                                              using Lib;

	                                              namespace MyCode;

	                                              public sealed class AppRoaster : IRoaster { }

	                                              public sealed class Host
	                                              {
	                                                  public Host(IRoaster roaster) { }
	                                              }

	                                              [Container]
	                                              [Import(typeof(PluginModule))]
	                                              [Scan<IRoaster>(As = ScanAs.Marker)]
	                                              [Singleton<Host>]
	                                              public static partial class MyContainer
	                                              {
	                                              }
	                                              """;

	[Test]
	public async Task ContainerScanOutranksACrossAssemblyModuleScanForTheSameService()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(RoasterProviderLibrarySource, ScanningConsumerSource);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("new global::MyCode.Host(ResolveAppRoaster")
			.Because("within the scan tier the container's own [Scan] match wins the IRoaster slot over an imported module's, the container winning ties like everywhere else");
	}

	[Test]
	public async Task ContainerScanOutranksASameCompilationModuleScanForTheSameService()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public interface IPlugin { }
			public interface IRoaster { }

			internal sealed class Roaster : IPlugin, IRoaster { }

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			public static partial class PluginModule { }

			public sealed class AppRoaster : IRoaster { }

			public sealed class Host
			{
			    public Host(IRoaster roaster) { }
			}

			[Container]
			[Import(typeof(PluginModule))]
			[Scan<IRoaster>(As = ScanAs.Marker)]
			[Singleton<Host>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("new global::MyCode.Host(ResolveAppRoaster")
			.Because("a same-compilation module's scan match ranks exactly like a cross-assembly one, below the container's own [Scan], so packaging the module differently never flips the winner");
	}

	[Test]
	public async Task SameCompilationMatchWithoutAccessibleExposureIsSkippedLikeAcrossAssemblies()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public interface IPlugin { }

			internal sealed class Roaster : IPlugin { }

			[Module]
			[Scan<IPlugin>(As = ScanAs.Self)]
			public static partial class PluginModule { }

			[Container]
			[Import(typeof(PluginModule))]
			public static partial class MyContainer { }
			""");

		await That(result.Diagnostics.Count(diagnostic => diagnostic.Contains("AWT196"))).IsEqualTo(1)
			.Because("the match has no exposure accessible outside the module's assembly");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).DoesNotContain("new global::MyCode.Roaster")
			.Because("the AWT196-warned match must actually be skipped, also by a same-compilation container, so the warning and the behavior agree and the module scan registers the same matches wherever the module is imported from");
	}

	[Test]
	public async Task SameCompilationMatchConstructsThroughTheGreediestConstructor()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public interface IExotic { }
			public interface IPlugin { }
			public interface IRoaster { }

			internal sealed class Roaster : IPlugin, IRoaster
			{
			    public Roaster() { }
			    public Roaster(IExotic exotic) { }
			}

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			public static partial class PluginModule { }

			[Container]
			[Import(typeof(PluginModule))]
			public static partial class MyContainer { }
			""");

		await That(result.Diagnostics).Contains("*AWT101*IExotic*").AsWildcard()
			.Because("a same-compilation module-scan match builds through the greediest accessible constructor, the one the generated factory mirrors, instead of quietly falling back to a smaller one the local graph satisfies");
	}

	[Test]
	public async Task CrossAssemblyMatchConstructsThroughTheGreediestConstructor()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			using Awaiten;

			namespace Lib;

			public interface IExotic { }
			public interface IPlugin { }
			public interface IRoaster { }

			internal sealed class Roaster : IPlugin, IRoaster
			{
			    public Roaster() { }
			    public Roaster(IExotic exotic) { }
			}

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			public static partial class PluginModule { }
			""", """
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof(PluginModule))]
			public static partial class MyContainer { }
			""");

		await That(result.Diagnostics).Contains("*AWT101*IExotic*").AsWildcard()
			.Because("the generated factory mirrors the greediest accessible constructor, so the consumer sees the same missing dependency a same-compilation import reports");
	}

	[Test]
	public async Task OverlappingScansWithDifferentLifetimesReportAwt142()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace Lib;

			public interface IPlugin { }
			public interface IRoaster { }
			internal sealed class Roaster : IPlugin, IRoaster { }

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton)]
			[Scan<IRoaster>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Transient)]
			public static partial class PluginModule { }
			""");

		await That(result.Diagnostics).Contains("*AWT142*Roaster*Singleton*Transient*").AsWildcard()
			.Because("two scans of one module registering the same implementation with different lifetimes mirror the container's scan-lifetime conflict");
	}

	private const string LibraryWithUnexposableMatchSource = """
	                                                         using Awaiten;

	                                                         namespace Lib;

	                                                         public interface IPlugin { }
	                                                         public interface IRoaster { }
	                                                         internal interface IGrinder { }

	                                                         internal sealed class Roaster : IPlugin, IRoaster { }
	                                                         internal sealed class Grinder : IPlugin, IGrinder { }

	                                                         [Module]
	                                                         [Scan<IPlugin>(As = ScanAs.MatchingInterface)]
	                                                         public static partial class PluginModule { }
	                                                         """;

	[Test]
	public async Task PerMatchScanDiagnosticsLandInTheLibrarysBuildAndNotTheConsumers()
	{
		(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult libraryRun) =
			Generator.RunGenerator(LibraryWithUnexposableMatchSource, [], [], "ReferencedAssembly");

		Microsoft.CodeAnalysis.Diagnostic exposure =
			libraryRun.Diagnostics.Single(diagnostic => diagnostic.Id == "AWT196");
		int scanLine = LibraryWithUnexposableMatchSource
			[..LibraryWithUnexposableMatchSource.IndexOf("[Scan<IPlugin>", StringComparison.Ordinal)]
			.Count(character => character == '\n');
		await That(exposure.Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(scanLine)
			.Because("evaluating the scan in the module's own compilation anchors the per-match warning on the library's own [Scan], the only build that can act on it");
		await That(exposure.ToString()).Contains("Grinder")
			.Because("the warning names the library's own type, not the module the consumer imported");

		GeneratorResult consumer = Generator.RunWithGeneratedReferencedAssembly(LibraryWithUnexposableMatchSource, """
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof(PluginModule))]
			public static partial class MyContainer
			{
			}
			""");

		await That(consumer.Diagnostics).IsEmpty()
			.Because("the scan is already resolved by the time the consumer imports the module, so it sees a plain module and no per-match warning about types it neither owns nor can fix");
		await That(consumer.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains(FactoryName("global::Lib.Roaster"))
			.Because("only the unexposable match was dropped; the one with an accessible exposure interface still registers");
	}

	public class PrivateNestedMatches
	{
		private const string PrivateLibrarySource = """
		                                            using Awaiten;

		                                            namespace Lib;

		                                            public interface IClock { }
		                                            public sealed class SystemClock : IClock { }
		                                            public interface IPlugin { }

		                                            [Module]
		                                            [Scan<IPlugin>(As = ScanAs.Marker, OnActivated = nameof(Start))]
		                                            public static partial class PluginModule
		                                            {
		                                                private sealed class FakePlugin : IPlugin
		                                                {
		                                                    public FakePlugin(IClock clock) { }
		                                                }

		                                                private static void Start(FakePlugin plugin) { }
		                                            }
		                                            """;

		[Test]
		public async Task ModuleSelfCompilesAMatchNestedPrivatelyInIt()
		{
			(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator(PrivateLibrarySource, [], []);
			string module = run.Results
				.SelectMany(r => r.GeneratedSources)
				.Single(s => s.HintName.Contains("ModuleScan"))
				.SourceText.ToString();

			await That(module).Contains($"public static global::Lib.IPlugin {FactoryName("global::Lib.PluginModule.FakePlugin")}(global::Lib.IClock @clock) => new global::Lib.PluginModule.FakePlugin(@clock);")
				.Because("the factory sits in the module, so it may construct the module's private type and return its accessible marker");
		}

		[Test]
		public async Task SameAssemblyContainer_CallsTheGeneratedFactory()
		{
			GeneratorResult result = Generator.Run(PrivateLibrarySource + """

			                                                              [Container]
			                                                              [Import(typeof(PluginModule))]
			                                                              [Singleton<SystemClock, IClock>]
			                                                              public static partial class Shop
			                                                              {
			                                                              }
			                                                              """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the container builds the module's private match through the factory and runs its private hook through the wrapper");
			string source = result.Sources["Awaiten.Lib.Shop.g.cs"];
			await That(source).Contains($"global::Lib.PluginModule.Internals.{FactoryName("global::Lib.PluginModule.FakePlugin")}(")
				.Because("the container cannot construct a type nested privately in the module, not even in the module's own assembly");
			await That(source).DoesNotContain("new global::Lib.PluginModule.FakePlugin");
		}

		[Test]
		public async Task ContainerInAnotherAssembly_CallsTheGeneratedFactory()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(PrivateLibrarySource, """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(PluginModule))]
				[Singleton<SystemClock, IClock>]
				public static partial class Shop
				{
				}
				""");

			await That(result.Diagnostics).IsEmpty()
				.Because("another assembly reads the generated scan registration, exactly as for an internal match");
			await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains($"global::Lib.PluginModule.Internals.{FactoryName("global::Lib.PluginModule.FakePlugin")}(");
		}

		[Test]
		public async Task GenericHookOfAnOpenMarker_RunsThroughTheWrapper()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IView<T> { }
			                                       public sealed class ViewModel { }

			                                       [Module]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       public static partial class ViewModule
			                                       {
			                                           private sealed class MainView : IView<ViewModel> { }

			                                           private static void Wire<T>(IView<T> view, T model) { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(ViewModule))]
			                                       [Singleton<ViewModel>]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the container binds the generic hook over the match's closed marker form, as the module's build did");
			await That(result.Sources["Awaiten.Lib.Shop.g.cs"]).Contains("global::Lib.ViewModule.Internals.ScanHook_OnActivated_MainView_")
				.Because("the container cannot call a hook that takes the private match, so it runs the generated wrapper");
		}

		[Test]
		public async Task MatchScannedAsSelf_ReportsAwt196()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }

			                                       [Module]
			                                       [Scan<IPlugin>]
			                                       public static partial class PluginModule
			                                       {
			                                           private sealed class FakePlugin : IPlugin { }
			                                       }
			                                       """);

			await That(result.Diagnostics).HasSingle().Matching(diagnostic => diagnostic.Contains("warning AWT196") && diagnostic.Contains("FakePlugin"))
				.Because("a consumer could not name the private type a Self exposure registers, so the match is skipped with a warning, not an error");
		}

		[Test]
		public async Task SkipUnconstructableMatch_IsPrunedThroughTheFactoryItWouldCall()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }
			                                       public interface IPlugin { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.Marker, SkipUnconstructable = true)]
			                                       public static partial class PluginModule
			                                       {
			                                           private sealed class FakePlugin : IPlugin
			                                           {
			                                               public FakePlugin(IClock clock) { }
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(PluginModule))]
			                                       [Singleton<SystemClock, IClock>]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the prune asks about the constructor the module's factory mirrors, which the module can reach although the container cannot");
			await That(result.Sources["Awaiten.Lib.Shop.g.cs"]).Contains(FactoryName("global::Lib.PluginModule.FakePlugin"));
		}

		[Test]
		[Arguments(false)]
		[Arguments(true)]
		public async Task MatchPrivateToAClassContainingTheModule_IsBuiltThroughTheGeneratedFactory(bool twoLevelsOut)
		{
			const string moduleDeclaration = "[Module] [Scan<IPlugin>(As = ScanAs.Marker, OnActivated = nameof(Start))] public static partial class PluginModule { private static void Start(FakePlugin plugin) { } }";
			string declaration = twoLevelsOut ? $"public static partial class Inner {{ {moduleDeclaration} }}" : moduleDeclaration;
			string module = twoLevelsOut ? "Fakes.Inner.PluginModule" : "Fakes.PluginModule";
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace Lib;

			                                         public interface IPlugin { }

			                                         public static partial class Fakes
			                                         {
			                                             private sealed class FakePlugin : IPlugin { }

			                                             {{declaration}}
			                                         }

			                                         [Container]
			                                         [Import(typeof({{module}}))]
			                                         public static partial class Shop
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the module can reach a type private to a class containing it, so its scan matches the type and runs the private hook through the wrapper");
			string source = result.Sources["Awaiten.Lib.Shop.g.cs"];
			await That(source).Contains($"global::Lib.{module}.Internals.{FactoryName("global::Lib.Fakes.FakePlugin")}(")
				.Because("only the module can construct the type, so the container calls the module's generated factory");
			await That(source).DoesNotContain("new global::Lib.Fakes.FakePlugin");
		}

		[Test]
		public async Task MatchPrivateToAClassContainingTheModuleInAnotherAssembly_IsBuiltThroughTheGeneratedFactory()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IPlugin { }

				public static partial class Fakes
				{
				    private sealed class FakePlugin : IPlugin { }

				    [Module]
				    [Scan<IPlugin>(As = ScanAs.Marker)]
				    public static partial class PluginModule { }
				}
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(Fakes.PluginModule))]
				public static partial class Shop
				{
				}
				""");

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains($"global::Lib.Fakes.PluginModule.Internals.{FactoryName("global::Lib.Fakes.FakePlugin")}(")
				.Because("another assembly reads the generated scan registration of a type private to a class containing the module");
		}

		[Test]
		public async Task MatchPrivateToAClassContainingTheModuleThatTheModuleRegisters_YieldsWithAwt193()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IClock { }
			                                       public interface IPrinter { }

			                                       public static partial class Fakes
			                                       {
			                                           private sealed class FakeClock : IClock, IPrinter { }

			                                           [Module]
			                                           [Scan<IPrinter>(As = ScanAs.Marker)]
			                                           [Singleton<FakeClock, IClock>]
			                                           public static partial class ShopModule { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(Fakes.ShopModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).HasSingle().Matching(diagnostic => diagnostic.Contains("warning AWT193")
			                                                                  && diagnostic.Contains("'Lib.Fakes.FakeClock' is private or protected and the module registers it explicitly"))
				.Because("a type private to a class containing the module has the module's single factory too, so the scan match yields to the registration");
			await That(result.Sources["Awaiten.Lib.Shop.g.cs"]).Contains("Registration_FakeClock_");
		}

		[Test]
		public async Task MatchPrivateToAClassContainingANonPartialModule_ReportsAwt194()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IPlugin { }

			                                       public static class Fakes
			                                       {
			                                           private sealed class FakePlugin : IPlugin { }

			                                           [Module]
			                                           [Scan<IPlugin>(As = ScanAs.Marker)]
			                                           public static partial class PluginModule { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT194*'Lib.Fakes.PluginModule'*").AsWildcard()
				.Because("the module's factory for the match is generated into the module, which needs every containing class to be partial");
		}
	}
}
