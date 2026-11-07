using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.ContainerSupport;

/// <summary>A terminal started by the internal async initializer convention of its module.</summary>
public interface IFriendConventionTerminal
{
	/// <summary>The token the convention received, or <see langword="null" /> before it ran.</summary>
	CancellationToken? Token { get; }
}

/// <summary>The marker of a scanned plugin started by the internal async initializer convention of its module.</summary>
public interface IFriendConventionPlugin;

/// <summary>The matching interface of the scanned printer.</summary>
public interface IFriendConventionPrinter
{
	/// <summary>Whether the convention ran.</summary>
	bool Started { get; }
}

internal interface IFriendStartable
{
	Task StartAsync(CancellationToken cancellationToken);
}

internal sealed class FriendConventionTerminal : IFriendConventionTerminal, IFriendStartable
{
	public CancellationToken? Token { get; private set; }

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await Task.Yield();
		Token = cancellationToken;
	}
}

internal sealed class FriendConventionPrinter : IFriendConventionPlugin, IFriendConventionPrinter, IFriendStartable
{
	public bool Started { get; private set; }

	Task IFriendStartable.StartAsync(CancellationToken cancellationToken)
	{
		Started = true;
		return Task.CompletedTask;
	}
}

/// <summary>
///     A <c>[Module]</c> whose async initializer convention is internal, which a consumer in another assembly can call
///     only with <c>InternalsVisibleTo</c>. This assembly grants it to Awaiten.Tests, so the module builds without the
///     factories and a container there constructs and initializes the implementations itself.
/// </summary>
[Module]
[AsyncInitializer<IFriendStartable>(nameof(IFriendStartable.StartAsync))]
[Singleton<FriendConventionTerminal, IFriendConventionTerminal>]
[Scan<IFriendConventionPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton)]
public static partial class SupportFriendConventionModule;
