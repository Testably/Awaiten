using System.Collections.Generic;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of lifecycle hooks on a <c>[Module]</c>'s self-compiled <c>[Scan]</c>. The cross-assembly
///     cases import <see cref="HookedPluginModule" /> from the referenced Awaiten.Tests.Support assembly, which
///     compiled the module (its factories, hook wrappers and registration attributes) with the generator: the
///     importing container runs the module's internal <c>OnActivated</c>/<c>OnRelease</c> hooks through the generated
///     public wrappers, never naming the internal implementations or hooks. The same-compilation case declares a
///     module and its importer in this assembly, where the container binds the module's own internal hook directly.
/// </summary>
public partial class ModuleScanHookTests
{
	[Fact]
	public async Task CrossAssembly_NonGenericInternalHook_ActivatesOnResolve_AndReleasesOnDispose()
	{
		HookLog log;
		using (HookedContainer.Root container = new())
		{
			log = container.Resolve<HookLog>();
			container.Resolve<IHookedRoaster>();
			container.Resolve<IHookedGrinder>();

			// Both internal implementations were activated across the assembly boundary through the module's public
			// wrapper, which resolved the HookLog parameter from this container's graph.
			await That(log.Entries).Contains("activated:HookedRoaster");
			await That(log.Entries).Contains("activated:HookedGrinder");
			await That(log.Entries).DoesNotContain("released:HookedRoaster");
		}

		// Released when the singletons' owner (the root) is disposed.
		await That(log.Entries).Contains("released:HookedRoaster");
		await That(log.Entries).Contains("released:HookedGrinder");
	}

	[Fact]
	public async Task CrossAssembly_GenericInternalHook_ClosedAtModuleBuild_FiresWithAGraphDependency()
	{
		using HookedContainer.Root container = new();
		HookLog log = container.Resolve<HookLog>();

		container.Resolve<IHookedWindow>();

		// The generic hook was closed over the module's internal view-model at the module's build, so the consumer
		// sees no generics yet the wrapper still runs the closed hook.
		await That(log.Entries).Contains("wired:HookedWindow");
	}

	[Fact]
	public async Task SameCompilation_BindsTheModulesOwnInternalHookDirectly()
	{
		SameCompLog.Entries.Clear();
		using (SameCompContainer.Root container = new())
		{
			container.Resolve<ISameCompRoaster>();
			await That(SameCompLog.Entries).Contains("activated:SameCompRoaster");
			await That(SameCompLog.Entries).DoesNotContain("released:SameCompRoaster");
		}

		await That(SameCompLog.Entries).Contains("released:SameCompRoaster");
	}

	[Container]
	[Import(typeof(HookedPluginModule))]
	[Singleton<HookLog>]
	public static partial class HookedContainer;

	[Container]
	[Import(typeof(SameCompHookModule))]
	public static partial class SameCompContainer;
}

/// <summary>Where the same-compilation module's internal hooks record, so the test can observe them.</summary>
internal static class SameCompLog
{
	public static readonly List<string> Entries = new();
}

/// <summary>The marker the same-compilation module scans on.</summary>
public interface ISameCompPlugin;

/// <summary>The accessible exposure interface the same-compilation match is resolved under.</summary>
public interface ISameCompRoaster;

/// <summary>An internal implementation the same-compilation module exposes through its interface, with internal hooks.</summary>
internal sealed class SameCompRoaster : ISameCompPlugin, ISameCompRoaster;

/// <summary>
///     A <c>[Module]</c> and its importer share this assembly, so the container expands the module's <c>[Scan]</c>
///     directly and binds the module's own <c>internal</c> hooks (no generated wrapper to cross a boundary).
/// </summary>
[Module]
[Scan<ISameCompPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(Activate), OnRelease = nameof(Release))]
public static partial class SameCompHookModule
{
	internal static void Activate(ISameCompPlugin plugin) => SameCompLog.Entries.Add("activated:" + plugin.GetType().Name);

	internal static void Release(ISameCompPlugin plugin) => SameCompLog.Entries.Add("released:" + plugin.GetType().Name);
}
