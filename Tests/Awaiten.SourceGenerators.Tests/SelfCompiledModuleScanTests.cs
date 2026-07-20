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
		return $"Awaiten__Scan_{simpleName}_{hash:x8}";
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
	                                     [Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton)]
	                                     public static partial class PluginModule { }
	                                     """;

	[Fact]
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
		await That(module).Contains($"global::Awaiten.GeneratedScanRegistrationAttribute<global::Lib.IRoaster>(\"{FactoryName("global::Lib.Roaster")}\", Lifetime = global::Awaiten.AwaitenLifetime.Singleton)")
			.Because("the match is registered under its accessible MatchingInterface, as a singleton scan match");
		await That(module).Contains($"public static global::Lib.IRoaster {FactoryName("global::Lib.Roaster")}(global::Lib.IClock @clock) => new global::Lib.Roaster(@clock);")
			.Because("the factory returns the interface but constructs the internal implementation in the library, with verbatim-prefixed parameter names so keyword-named parameters stay legal");
	}

	[Fact]
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

	[Fact]
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

		await That(source).Contains($"global::Lib.PluginModule.{FactoryName("global::Lib.Roaster")}")
			.Because("the container calls the module's generated factory to build the implementation");
		await That(source).DoesNotContain("new global::Lib.Roaster")
			.Because("the consumer never constructs the internal implementation directly");
		await That(result.Diagnostics).DoesNotContain("*AWT154*").AsWildcard()
			.Because("a [Scan] on a module is now supported, not rejected");
	}

	[Fact]
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

	[Fact]
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
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton)]
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
	                                          [Scan<IPlugin>(As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]
	                                          public static partial class PluginModule { }
	                                          """;

	[Fact]
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
		await That(source).Contains($"global::Lib.PluginModule.{FactoryName("global::Lib.Alpha")}")
			.Because("the first match is a member of the IEnumerable<IPlugin> collection");
		await That(source).Contains($"global::Lib.PluginModule.{FactoryName("global::Lib.Bravo")}")
			.Because("the second match is a member of the IEnumerable<IPlugin> collection too, exactly as a container scan would collect them");
	}

	[Fact]
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

	[Fact]
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
		await That(source).Contains($"global::Lib.ModuleA.{FactoryName("global::Lib.A.Handler")}")
			.Because("module A's match is a member of the collection");
		await That(source).Contains($"global::Lib.ModuleB.{FactoryName("global::Lib.B.Handler")}")
			.Because("module B's match must not be deduped away by sharing module A's factory name, and the name hash keeps same-named matches distinct");
	}

	[Fact]
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
		string source = result.Sources["Awaiten.Lib.MyContainer.g.cs"];
		await That(source).Contains("new global::Lib.Roaster(")
			.Because("with no assembly boundary the container constructs the internal match directly, as if the [Scan] were its own");
	}

	[Fact]
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

	[Fact]
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

	[Fact]
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
		await That(module.Split("public static").Length - 1).IsEqualTo(1)
			.Because("the second scan matched the same type under the same exposure, which a container would dedup to one collection member, so only one factory is emitted");
	}

	[Fact]
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
		await That(source).Contains($"global::Lib.PluginModule.{FactoryName("global::Lib.Grinder")}")
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

	[Fact]
	public async Task ContainerScanOutranksACrossAssemblyModuleScanForTheSameService()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(RoasterProviderLibrarySource, ScanningConsumerSource);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("new global::MyCode.Host(ResolveAppRoaster")
			.Because("within the scan tier the container's own [Scan] match wins the IRoaster slot over an imported module's, the container winning ties like everywhere else");
	}

	[Fact]
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

	[Fact]
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

	[Fact]
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

	[Fact]
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

	[Fact]
	public async Task OverlappingScansWithDifferentLifetimesReportAwt142()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace Lib;

			public interface IPlugin { }
			public interface IRoaster { }
			internal sealed class Roaster : IPlugin, IRoaster { }

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton)]
			[Scan<IRoaster>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Transient)]
			public static partial class PluginModule { }
			""");

		await That(result.Diagnostics).Contains("*AWT142*Roaster*Singleton*Transient*").AsWildcard()
			.Because("two scans of one module registering the same implementation with different lifetimes mirror the container's scan-lifetime conflict");
	}
}
