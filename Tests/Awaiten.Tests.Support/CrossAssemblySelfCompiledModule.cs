using System;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>A clock the self-compiled module registers as an overridable default.</summary>
public interface ISelfCompiledClock
{
	/// <summary>Which side registered the clock, so a test sees whether an importer replaced it.</summary>
	string Origin { get; }
}

/// <summary>A greeter whose implementation stays internal to this assembly.</summary>
public interface ISelfCompiledGreeter
{
	/// <summary>The clock the greeter was constructed with, resolved from the importing container's graph.</summary>
	ISelfCompiledClock Clock { get; }

	/// <summary>Whether the module's internal activation hook ran.</summary>
	bool Activated { get; }
}

/// <summary>A scoped service whose disposable implementation stays internal.</summary>
public interface ISelfCompiledSession
{
	/// <summary>Whether the container disposed the session.</summary>
	bool IsDisposed { get; }
}

/// <summary>A singleton whose async-only disposable implementation stays internal.</summary>
public interface ISelfCompiledAsyncResource
{
	/// <summary>Whether the container disposed the resource asynchronously.</summary>
	bool IsDisposed { get; }
}

/// <summary>A domain interface the modules of this assembly name as their async initializer convention.</summary>
public interface ISelfCompiledStartable
{
	/// <summary>Starts the instance asynchronously.</summary>
	/// <param name="cancellationToken">The token of the resolve that builds the instance.</param>
	Task StartAsync(CancellationToken cancellationToken);
}

/// <summary>A singleton whose async-initialized implementation stays internal.</summary>
public interface ISelfCompiledWarmup
{
	/// <summary>Whether the container initialized the warmup.</summary>
	bool IsInitialized { get; }
}

/// <summary>A service two internal implementations contribute to a collection.</summary>
public interface ISelfCompiledTool;

/// <summary>The first service of a public implementation with an internal constructor.</summary>
public interface ISelfCompiledFront;

/// <summary>The second service of a public implementation with an internal constructor.</summary>
public interface ISelfCompiledBack;

/// <summary>A disposable singleton whose disposal the module suppresses.</summary>
public interface ISelfCompiledKept
{
	/// <summary>Whether anything disposed the instance.</summary>
	bool IsDisposed { get; }
}

/// <summary>An eager singleton whose implementation stays internal.</summary>
public interface ISelfCompiledEager;

/// <summary>Counts the constructions of the internal eager singleton, so a test sees when it is built.</summary>
public static class SelfCompiledEagerCounter
{
	private static int _constructed;

	/// <summary>How often the internal eager singleton was constructed.</summary>
	public static int Constructed => Volatile.Read(ref _constructed);

	internal static void Increment() => Interlocked.Increment(ref _constructed);
}

/// <summary>A public implementation another assembly cannot construct, because its constructor is internal.</summary>
public sealed class SelfCompiledShared : ISelfCompiledFront, ISelfCompiledBack
{
	internal SelfCompiledShared()
	{
	}
}

internal sealed class SelfCompiledClock : ISelfCompiledClock
{
	public string Origin => "library";
}

internal sealed class SelfCompiledGreeter(ISelfCompiledClock clock) : ISelfCompiledGreeter
{
	public ISelfCompiledClock Clock => clock;

	public bool Activated { get; set; }
}

internal sealed class SelfCompiledSession : ISelfCompiledSession, IDisposable
{
	public bool IsDisposed { get; private set; }

	public void Dispose() => IsDisposed = true;
}

internal sealed class SelfCompiledAsyncResource : ISelfCompiledAsyncResource, IAsyncDisposable
{
	public bool IsDisposed { get; private set; }

	public ValueTask DisposeAsync()
	{
		IsDisposed = true;
		return default;
	}
}

internal sealed class SelfCompiledWarmup : ISelfCompiledWarmup, ISelfCompiledStartable
{
	public bool IsInitialized { get; private set; }

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await Task.Yield();
		IsInitialized = true;
	}
}

internal sealed class SelfCompiledHammer : ISelfCompiledTool;

internal sealed class SelfCompiledSaw : ISelfCompiledTool;

internal sealed class SelfCompiledKept : ISelfCompiledKept, IDisposable
{
	public bool IsDisposed { get; private set; }

	public void Dispose() => IsDisposed = true;
}

internal sealed class SelfCompiledEager : ISelfCompiledEager
{
	public SelfCompiledEager() => SelfCompiledEagerCounter.Increment();
}

/// <summary>
///     A <c>[Module]</c> whose explicit registrations name implementations another assembly cannot construct. Its
///     build self-compiles a factory per implementation, so a container in Awaiten.Tests imports it without this
///     assembly granting <c>InternalsVisibleTo</c>, and still shares, initializes, disposes and hooks each
///     implementation like a type it constructs itself.
/// </summary>
[Module]
[AsyncInitializer<ISelfCompiledStartable>(nameof(ISelfCompiledStartable.StartAsync))]
[Singleton<SelfCompiledClock, ISelfCompiledClock>(Fallback = Fallback.Warn)]
[Singleton<SelfCompiledGreeter, ISelfCompiledGreeter>(OnActivated = nameof(Activate))]
[Singleton<SelfCompiledGreeter, ISelfCompiledGreeter>(Key = "formal", OnActivated = nameof(Activate))]
[Scoped<SelfCompiledSession, ISelfCompiledSession>]
[Singleton<SelfCompiledWarmup, ISelfCompiledWarmup>]
[Transient<SelfCompiledHammer, ISelfCompiledTool>]
[Transient<SelfCompiledSaw, ISelfCompiledTool>]
[Singleton<SelfCompiledShared, ISelfCompiledFront>]
[Singleton<SelfCompiledShared, ISelfCompiledBack>]
[Singleton<SelfCompiledKept, ISelfCompiledKept>(SuppressDisposal = true)]
[Singleton<SelfCompiledEager, ISelfCompiledEager>(Eager = true)]
public static partial class SelfCompiledRegistrationModule
{
	internal static void Activate(SelfCompiledGreeter greeter) => greeter.Activated = true;
}

/// <summary>A <c>[Module]</c> registering an internal async-only disposable, apart so the other importers dispose synchronously.</summary>
[Module]
[Singleton<SelfCompiledAsyncResource, ISelfCompiledAsyncResource>]
public static partial class SelfCompiledAsyncResourceModule;

/// <summary>The marker a self-compiled module <c>[Scan]</c> selects the async-initialized match on.</summary>
public interface ISelfCompiledScanPlugin;

/// <summary>The accessible interface the async-initialized scan match is registered and resolved under.</summary>
public interface ISelfCompiledScanWarmup
{
	/// <summary>Whether the container initialized the match.</summary>
	bool IsInitialized { get; }
}

internal sealed class SelfCompiledScanWarmup : ISelfCompiledScanPlugin, ISelfCompiledScanWarmup, ISelfCompiledStartable
{
	public bool IsInitialized { get; private set; }

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await Task.Yield();
		IsInitialized = true;
	}
}

/// <summary>
///     A <c>[Module]</c> whose self-compiled <c>[Scan]</c> matches an internal async-initialized implementation, which
///     an importing container must initialize although it never sees the implementation.
/// </summary>
[Module]
[AsyncInitializer<ISelfCompiledStartable>(nameof(ISelfCompiledStartable.StartAsync))]
[Scan<ISelfCompiledScanPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton)]
public static partial class SelfCompiledScanWarmupModule;
