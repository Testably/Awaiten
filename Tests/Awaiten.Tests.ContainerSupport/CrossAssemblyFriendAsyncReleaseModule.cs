using System.Threading.Tasks;

namespace Awaiten.Tests.ContainerSupport;

/// <summary>A till closed by an asynchronous release hook that takes an internal dependency.</summary>
public interface IFriendAsyncReleasedTill
{
	/// <summary>Whether the release hook ran.</summary>
	bool Closed { get; }
}

internal sealed class FriendReleaseLog;

internal sealed class FriendAsyncReleasedTill : IFriendAsyncReleasedTill
{
	public bool Closed { get; set; }
}

/// <summary>
///     A <c>[Module]</c> whose asynchronous release hook takes an internal dependency, which a generated wrapper cannot
///     mirror. This assembly grants Awaiten.Tests <c>InternalsVisibleTo</c>, so the module builds without that wrapper
///     and a container there awaits the hook itself.
/// </summary>
[Module]
[Singleton<FriendReleaseLog>]
[Scoped<FriendAsyncReleasedTill, IFriendAsyncReleasedTill>(OnRelease = nameof(Close))]
public static partial class SupportFriendAsyncReleaseModule
{
	internal static async Task Close(FriendAsyncReleasedTill till, FriendReleaseLog log)
	{
		await Task.Yield();
		till.Closed = true;
	}
}
