using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Lifecycle hooks on a <c>[Module]</c>'s self-compiled <c>[Scan]</c>: the module resolves and (for a generic
///     hook) closes its own - possibly <c>internal</c> - <c>OnActivated</c>/<c>OnRelease</c> hook at its build and
///     emits a <c>public static void</c> wrapper beside each factory, naming it on the
///     <c>[GeneratedScanRegistration]</c>. A cross-assembly consumer reads that name and runs the wrapper through
///     the ordinary hook pipeline; a same-compilation container binds the module's own hook directly.
/// </summary>
public class SelfCompiledModuleScanHookTests
{
	private static string ReadModule(string source)
	{
		(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator(source, [], []);
		return run.Results
			.SelectMany(r => r.GeneratedSources)
			.Single(s => s.HintName.Contains("ModuleScan"))
			.SourceText.ToString();
	}

	[Fact]
	public async Task ModuleEmitsAPublicWrapperForANonGenericInternalHook()
	{
		string module = ReadModule("""
			using Awaiten;

			namespace Lib;

			public interface IPlugin { }
			public interface IRoaster { }

			internal sealed class Roaster : IPlugin, IRoaster { }

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, OnActivated = nameof(Wire))]
			public static partial class PluginModule
			{
			    internal static void Wire(Roaster roaster) { }
			}
			""");

		await That(module).Contains("OnActivated = \"Awaiten__ScanHook_OnActivated_Roaster_").AsWildcard()
			.Because("the registration names the generated wrapper so a consumer runs it through the ordinary hook pipeline");
		await That(module).Contains("public static void Awaiten__ScanHook_OnActivated_Roaster_").AsWildcard()
			.Because("the wrapper is public so a cross-assembly consumer can call it");
		await That(module).Contains("(global::Lib.IRoaster @awaiten__instance) => Wire((global::Lib.Roaster)@awaiten__instance);").AsWildcard()
			.Because("the wrapper takes the accessible exposure interface, casts it back to the internal implementation inside the module, and forwards to the internal hook");
	}

	[Fact]
	public async Task ModuleClosesAGenericHookAtBuildAndEmitsANonGenericWrapper()
	{
		string module = ReadModule("""
			using Awaiten;

			namespace Lib;

			public interface IView<TViewModel> { }
			internal interface IMainViewModel { }
			public interface IMainWindow { }

			internal sealed class MainWindow : IView<IMainViewModel>, IMainWindow { }

			[Module]
			[Scan(typeof(IView<>), As = ScanAs.MatchingInterface, OnActivated = nameof(Wire))]
			public static partial class ViewModule
			{
			    internal static void Wire<TViewModel>(IView<TViewModel> view) { }
			}
			""");

		// The consumer only ever sees the public exposure interface; the closed marker form (over an internal
		// view-model) and the closed generic call live entirely inside the module.
		await That(module).Contains("public static void Awaiten__ScanHook_OnActivated_MainWindow_").AsWildcard();
		await That(module).Contains("(global::Lib.IMainWindow @awaiten__instance) => Wire<global::Lib.IMainViewModel>((global::Lib.IView<global::Lib.IMainViewModel>)@awaiten__instance);").AsWildcard()
			.Because("the generic hook is closed at module build, so the wrapper is non-generic and the consumer sees no generics even though the marker closes over an internal view-model");
	}

	[Fact]
	public async Task ModuleMirrorsAGraphResolvedHookDependencyOntoTheWrapper()
	{
		string module = ReadModule("""
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
			    internal static void Wire(Roaster roaster, IClock clock) { }
			}
			""");

		await That(module).Contains("(global::Lib.IRoaster @awaiten__instance, global::Lib.IClock @clock) => Wire((global::Lib.Roaster)@awaiten__instance, @clock);").AsWildcard()
			.Because("the hook's parameter after the instance is mirrored onto the public wrapper, resolved from the consumer's graph like a factory parameter");
	}

	[Fact]
	public async Task CrossAssemblyConsumerRunsTheGeneratedHookWrapper()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			using Awaiten;

			namespace Lib;

			public interface IClock { }
			public sealed class SystemClock : IClock { }
			public interface IPlugin { }
			public interface IRoaster { }

			internal sealed class Roaster : IPlugin, IRoaster { }

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(Wire))]
			public static partial class PluginModule
			{
			    internal static void Wire(Roaster roaster, IClock clock) { }
			}
			""", """
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof(PluginModule))]
			[Singleton<SystemClock, IClock>]
			public static partial class MyContainer { }
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("the wrapper is a plain public static method, so the consumer binds it and resolves its IClock parameter from the graph");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("global::Lib.PluginModule.Awaiten__ScanHook_OnActivated_Roaster_").AsWildcard()
			.Because("the container invokes the module's generated hook wrapper qualified on the module type");
	}

	[Fact]
	public async Task SameCompilationContainerBindsTheModulesOwnHookDirectly()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace Lib;

			public interface IClock { }
			public sealed class SystemClock : IClock { }
			public interface IPlugin { }
			public interface IRoaster { }

			internal sealed class Roaster : IPlugin, IRoaster { }

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(Wire))]
			public static partial class PluginModule
			{
			    internal static void Wire(Roaster roaster, IClock clock) { }
			}

			[Container]
			[Import(typeof(PluginModule))]
			[Singleton<SystemClock, IClock>]
			public static partial class MyContainer { }
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("with no assembly boundary the container binds the module's own internal hook directly");
		string source = result.Sources["Awaiten.Lib.MyContainer.g.cs"];
		await That(source).Contains("global::Lib.PluginModule.Wire(").AsWildcard()
			.Because("the container calls the module's own hook, qualified on the module, rather than the wrapper it cannot see in the same compilation");
	}

	[Fact]
	public async Task AHookParameterNamedLikeTheInstanceDoesNotCollideOnTheWrapper()
	{
		string module = ReadModule("""
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
			    internal static void Wire(Roaster roaster, IClock instance) { }
			}
			""");

		await That(module).Contains("(global::Lib.IRoaster @awaiten__instance, global::Lib.IClock @instance) => Wire((global::Lib.Roaster)@awaiten__instance, @instance);").AsWildcard()
			.Because("the synthetic instance parameter carries a reserved name so a hook parameter literally named 'instance' does not become a duplicate parameter on the wrapper");
	}

	[Fact]
	public async Task Awt164_ReportsAPrivateHook()
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
			    private static void Wire(Roaster roaster) { }
			}
			""");

		await That(result.Diagnostics).Contains("*AWT164*Wire*").AsWildcard()
			.Because("a private hook is not usable: the wrapper could call it, but a same-compilation container binding it directly could not, so it is rejected in both to stay consistent");
	}

	[Fact]
	public async Task Awt164_ReportsWhenTheHookNamesNoUsableMethod()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace Lib;

			public interface IPlugin { }
			public interface IRoaster { }
			internal sealed class Roaster : IPlugin, IRoaster { }

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, OnActivated = "Missing")]
			public static partial class PluginModule { }
			""");

		await That(result.Diagnostics).Contains("*AWT164*Missing*").AsWildcard()
			.Because("the module has no usable static void method 'Missing' accepting the match");
	}

	[Fact]
	public async Task Awt189_ReportsAnArgHookParameter()
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
			.Because("a hook parameter cannot be a runtime [Arg]; the wrapper has no call site to supply one");
	}

	[Fact]
	public async Task Awt190_ReportsAnOverloadedHook()
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
			    internal static void Wire(Roaster roaster) { }
			    internal static void Wire(Roaster roaster, IClock clock) { }
			}
			""");

		await That(result.Diagnostics).Contains("*AWT190*Wire*").AsWildcard()
			.Because("two module methods named Wire accept the match, so the hook is ambiguous");
	}

	[Fact]
	public async Task Awt191_ReportsADeferredReleaseHookParameter()
	{
		GeneratorResult result = Generator.Run("""
			using System;
			using Awaiten;

			namespace Lib;

			public interface IClock { }
			public interface IPlugin { }
			public interface IRoaster { }
			internal sealed class Roaster : IPlugin, IRoaster { }

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, OnRelease = nameof(Release))]
			public static partial class PluginModule
			{
			    internal static void Release(Roaster roaster, Func<IClock> clock) { }
			}
			""");

		await That(result.Diagnostics).Contains("*AWT191*clock*").AsWildcard()
			.Because("a release hook's Func/Lazy parameter is dead by the owner's teardown");
	}

	[Fact]
	public async Task Awt198_ReportsAGenericHookThatCouldBindTwoClosedMarkerForms()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace Lib;

			public interface IView<TViewModel> { }
			public interface IVmA { }
			public interface IVmB { }
			public interface IDualView { }

			internal sealed class DualView : IView<IVmA>, IView<IVmB>, IDualView { }

			[Module]
			[Scan(typeof(IView<>), As = ScanAs.MatchingInterface, OnActivated = nameof(Wire))]
			public static partial class ViewModule
			{
			    internal static void Wire<TViewModel>(IView<TViewModel> view) { }
			}
			""");

		await That(result.Diagnostics).Contains("*AWT198*Wire*DualView*").AsWildcard()
			.Because("the generic hook could bind the match through IView<IVmA> or IView<IVmB>, so its type arguments are ambiguous");
	}

	[Fact]
	public async Task Awt203_ReportsAnInaccessibleHookDependencyParameter()
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

	[Fact]
	public async Task Awt204_ReportsAFromKeyHookDependencyParameter()
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
	public async Task Awt164_ReportsAHookWithAByRefParameter()
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
			    internal static void Wire(Roaster roaster, ref IClock clock) { }
			}
			""");

		await That(result.Diagnostics).Contains("*AWT164*Wire*").AsWildcard()
			.Because("a by-ref parameter cannot be mirrored onto the wrapper or supplied by the container's hook invocation, so the method is not a usable hook");
	}

	[Fact]
	public async Task AHookParameterNamedLikeTheSyntheticInstanceSuffixesTheSyntheticName()
	{
		string module = ReadModule("""
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
			    internal static void Wire(Roaster roaster, IClock awaiten__instance) { }
			}
			""");

		await That(module).Contains("(global::Lib.IRoaster @awaiten__instance_, global::Lib.IClock @awaiten__instance) => Wire((global::Lib.Roaster)@awaiten__instance_, @awaiten__instance);").AsWildcard()
			.Because("a user hook parameter literally named awaiten__instance must not become a duplicate parameter on the wrapper, so the synthetic name is suffixed away from it");
	}

	[Fact]
	public async Task TwoOverlappingScansFillEachOthersHookSlots()
	{
		string module = ReadModule("""
			using Awaiten;

			namespace Lib;

			public interface IMarkerA { }
			public interface IMarkerB { }
			public interface IWorker { }

			internal sealed class Worker : IMarkerA, IMarkerB, IWorker { }

			[Module]
			[Scan<IMarkerA>(As = ScanAs.MatchingInterface, OnActivated = nameof(Activate))]
			[Scan<IMarkerB>(As = ScanAs.MatchingInterface, OnRelease = nameof(Release))]
			public static partial class WorkerModule
			{
			    internal static void Activate(Worker worker) { }
			    internal static void Release(Worker worker) { }
			}
			""");

		await That(module).Contains("OnActivated = \"Awaiten__ScanHook_OnActivated_Worker_").AsWildcard()
			.Because("the first scan's OnActivated stays on the single registration");
		await That(module).Contains("OnRelease = \"Awaiten__ScanHook_OnRelease_Worker_").AsWildcard()
			.Because("a second scan matching the same type fills the hook slot the first left empty, exactly like two container scans merging");
		await That(module).Contains("public static void Awaiten__ScanHook_OnRelease_Worker_").AsWildcard()
			.Because("the filled slot emits its wrapper beside the first scan's factory");
	}

	[Fact]
	public async Task OverlappingScansNamingTheSameFailingHookReportItOnce()
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
			.Because("the second overlapping scan re-resolves the same failed hook for the slot merge, but the first scan already reported it");
	}

	[Fact]
	public async Task Awt199_ReportsOverlappingScansNamingDifferentHooksForOneSlot()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace Lib;

			public interface IMarkerA { }
			public interface IMarkerB { }
			public interface IWorker { }

			internal sealed class Worker : IMarkerA, IMarkerB, IWorker { }

			[Module]
			[Scan<IMarkerA>(As = ScanAs.MatchingInterface, OnActivated = nameof(Activate))]
			[Scan<IMarkerB>(As = ScanAs.MatchingInterface, OnActivated = nameof(Other))]
			public static partial class WorkerModule
			{
			    internal static void Activate(Worker worker) { }
			    internal static void Other(Worker worker) { }
			}
			""");

		await That(result.Diagnostics).Contains("*AWT199*Worker*Activate*Other*").AsWildcard()
			.Because("two scans naming different methods for one slot is order-dependent, so it is surfaced like the container form surfaces it");
	}

	[Fact]
	public async Task SameCompilationContainerScanAndModuleScanSharingAMatchResolveTheHookAgainstTheModule()
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
			    internal static void Wire(Roaster roaster) { }
			}

			[Container]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			[Import(typeof(PluginModule))]
			public static partial class MyContainer { }
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("the hook name is owner-relative: it resolves against the module that named it, not the container the first (hookless) scan registration fixed as the implementation's origin");
		string source = result.Sources["Awaiten.Lib.MyContainer.g.cs"];
		await That(source).Contains("global::Lib.PluginModule.Wire(").AsWildcard()
			.Because("the hook is emitted qualified on the module even though the container's own scan registered the match first");
	}

	[Fact]
	public async Task Awt199_ReportsAModuleScanHookConflictingWithASameNamedContainerScanHook()
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
			    internal static void Wire(Roaster roaster) { }
			}

			[Container]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, OnActivated = nameof(Wire))]
			[Import(typeof(PluginModule))]
			public static partial class MyContainer
			{
			    internal static void Wire(Roaster roaster) { }
			}
			""");

		await That(result.Diagnostics).Contains("*AWT199*Lib.PluginModule.Wire*").AsWildcard()
			.Because("the same hook name on a different origin is a different method, so merging it silently would run whichever origin registered first; the conflict names the module-owned loser qualified");
	}
}
