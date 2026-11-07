using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>A terminal whose implementation stays internal and is connected by the module's asynchronous hook.</summary>
public interface IAsyncHookedTerminal
{
	/// <summary>The token the module's hook received, or <see langword="null" /> before it ran.</summary>
	CancellationToken? Token { get; }
}

/// <summary>The marker the module's scan selects the asynchronously hooked plugins on.</summary>
public interface IAsyncHookedPlugin;

/// <summary>The accessible interface the scanned printer is registered and resolved under.</summary>
public interface IAsyncHookedPrinter
{
	/// <summary>Whether the module's asynchronous scan hook ran.</summary>
	bool Warmed { get; }
}

internal sealed class AsyncHookedTerminal : IAsyncHookedTerminal
{
	public CancellationToken? Token { get; set; }
}

internal sealed class AsyncHookedPrinter : IAsyncHookedPlugin, IAsyncHookedPrinter
{
	public bool Warmed { get; set; }
}

/// <summary>
///     A <c>[Module]</c> whose registration and scan name <c>internal</c> asynchronous <c>OnActivated</c> hooks, so an
///     importing container in another assembly awaits them through the public wrappers the module generates.
/// </summary>
[Module]
[Singleton<AsyncHookedTerminal, IAsyncHookedTerminal>(OnActivated = nameof(Connect))]
[Scan<IAsyncHookedPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton, OnActivated = nameof(Warm))]
public static partial class AsyncHookModule
{
	internal static async Task Connect(AsyncHookedTerminal terminal, CancellationToken cancellationToken)
	{
		await Task.Yield();
		terminal.Token = cancellationToken;
	}

	internal static async ValueTask Warm(IAsyncHookedPlugin plugin)
	{
		await Task.Yield();
		((AsyncHookedPrinter)plugin).Warmed = true;
	}
}
