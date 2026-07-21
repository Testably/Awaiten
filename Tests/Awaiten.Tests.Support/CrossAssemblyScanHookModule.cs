using System.Collections.Generic;

namespace Awaiten.Tests.Support;

/// <summary>
///     A service the module's <c>internal</c> lifecycle hooks record into, so a cross-assembly consumer can observe
///     that activation and release actually ran across the assembly boundary. Registered by the importing container
///     and resolved into each hook wrapper's parameter from the consumer's own graph.
/// </summary>
public sealed class HookLog
{
	/// <summary>Activation/release entries the module's hooks append, in order.</summary>
	public List<string> Entries { get; } = new();
}

/// <summary>The marker a self-compiled module <c>[Scan]</c> selects the hooked plugins on.</summary>
public interface IHookedPlugin;

/// <summary>The accessible exposure interface the roaster match is registered and resolved under.</summary>
public interface IHookedRoaster;

/// <summary>The accessible exposure interface the grinder match is registered and resolved under.</summary>
public interface IHookedGrinder;

/// <summary>An internal plugin: exposed to a consumer only through <see cref="IHookedRoaster" />, hooks stay internal.</summary>
internal sealed class HookedRoaster : IHookedPlugin, IHookedRoaster;

/// <summary>A second internal plugin, exposed through <see cref="IHookedGrinder" />, sharing the module's hooks.</summary>
internal sealed class HookedGrinder : IHookedPlugin, IHookedGrinder;

// S2326: TViewModel is the scan's open-generic marker input, closed at module build; the interface body never uses it.
#pragma warning disable S2326

/// <summary>An open-generic marker a self-compiled module <c>[Scan]</c> closes a generic hook over.</summary>
public interface IHookedView<TViewModel>;

#pragma warning restore S2326

/// <summary>The (internal) view-model the hooked window closes the marker at; the consumer never names it.</summary>
internal interface IMainViewModel;

/// <summary>The accessible exposure interface the window match is registered and resolved under.</summary>
public interface IHookedWindow;

/// <summary>An internal view whose generic hook the module closes at build over the internal <see cref="IMainViewModel" />.</summary>
internal sealed class HookedWindow : IHookedView<IMainViewModel>, IHookedWindow;

/// <summary>
///     A <c>[Module]</c> in a referenced assembly whose self-compiled <c>[Scan]</c> carries lifecycle hooks. The
///     module resolves and (for the generic hook) closes its own <c>internal</c> hooks at its build and emits public
///     wrappers, so an importing container in another assembly runs them without ever naming the internal
///     implementations, hooks, or the internal view-model the generic hook closes over. Each hook takes a
///     graph-resolved <see cref="HookLog" /> the consumer supplies.
/// </summary>
[Module]
[Scan<IHookedPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(Activate), OnRelease = nameof(Release))]
[Scan(typeof(IHookedView<>), As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(Wire))]
public static partial class HookedPluginModule
{
	internal static void Activate(IHookedPlugin plugin, HookLog log) => log.Entries.Add("activated:" + plugin.GetType().Name);

	internal static void Release(IHookedPlugin plugin, HookLog log) => log.Entries.Add("released:" + plugin.GetType().Name);

	internal static void Wire<TViewModel>(IHookedView<TViewModel> view, HookLog log) => log.Entries.Add("wired:" + view.GetType().Name);
}
