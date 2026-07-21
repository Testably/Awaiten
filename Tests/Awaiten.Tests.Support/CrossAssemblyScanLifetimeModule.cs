namespace Awaiten.Tests.Support;

/// <summary>The marker the lifetime module's singleton <c>[Scan]</c> selects on.</summary>
public interface ISingletonPlugin;

/// <summary>The marker the lifetime module's transient <c>[Scan]</c> selects on.</summary>
public interface ITransientPlugin;

/// <summary>The accessible exposure interface the singleton match is registered and resolved under.</summary>
public interface IScanSingleton;

/// <summary>The accessible exposure interface the transient match is registered and resolved under.</summary>
public interface IScanTransient;

/// <summary>An internal match the module registers as a singleton, exposed only through its interface.</summary>
internal sealed class ScanSingleton : ISingletonPlugin, IScanSingleton;

/// <summary>An internal match the module registers as a transient, exposed only through its interface.</summary>
internal sealed class ScanTransient : ITransientPlugin, IScanTransient;

/// <summary>
///     A <c>[Module]</c> in a referenced assembly whose two self-compiled <c>[Scan]</c>s declare different
///     lifetimes. The scan's <c>Lifetime</c> becomes the generated registration's lifetime, which the importing
///     container honors like any other module registration: nothing about instance ownership moves into the
///     library. A consumer imports this to observe that a singleton match is one instance per <c>Root</c> while a
///     transient match is a new instance per resolve, without naming either internal implementation.
/// </summary>
[Module]
[Scan<ISingletonPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton)]
[Scan<ITransientPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Transient)]
public static partial class ScanLifetimeModule;
