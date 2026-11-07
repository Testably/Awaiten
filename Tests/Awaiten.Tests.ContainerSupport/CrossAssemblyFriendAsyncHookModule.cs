using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.ContainerSupport;

/// <summary>A terminal connected by an asynchronous activation hook that takes an internal dependency.</summary>
public interface IFriendAsyncTerminal
{
	/// <summary>The token the activation hook received, or <see langword="null" /> before it ran.</summary>
	CancellationToken? Token { get; }
}

/// <summary>The marker of a scanned plugin warmed by an asynchronous activation hook that takes an internal dependency.</summary>
public interface IFriendAsyncPlugin;

/// <summary>The matching interface of the scanned printer.</summary>
public interface IFriendAsyncPrinter
{
	/// <summary>The token the activation hook received, or <see langword="null" /> before it ran.</summary>
	CancellationToken? Token { get; }
}

internal sealed class FriendAsyncLog;

internal sealed class FriendAsyncTerminal : IFriendAsyncTerminal
{
	public CancellationToken? Token { get; set; }
}

internal sealed class FriendAsyncPrinter : IFriendAsyncPlugin, IFriendAsyncPrinter
{
	public CancellationToken? Token { get; set; }
}

/// <summary>
///     A <c>[Module]</c> whose asynchronous hooks take an internal dependency, which a generated wrapper cannot mirror.
///     This assembly grants Awaiten.Tests <c>InternalsVisibleTo</c>, so the module builds without those wrappers and a
///     container there awaits the hooks itself.
/// </summary>
[Module]
[Singleton<FriendAsyncLog>]
[Singleton<FriendAsyncTerminal, IFriendAsyncTerminal>(OnActivated = nameof(Connect))]
[Scan<IFriendAsyncPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton, OnActivated = nameof(Warm))]
public static partial class SupportFriendAsyncHookModule
{
	internal static async Task Connect(FriendAsyncTerminal terminal, FriendAsyncLog log, CancellationToken cancellationToken)
	{
		await Task.Yield();
		terminal.Token = cancellationToken;
	}

	internal static async ValueTask Warm(FriendAsyncPrinter printer, FriendAsyncLog log, CancellationToken cancellationToken)
	{
		await Task.Yield();
		printer.Token = cancellationToken;
	}
}
