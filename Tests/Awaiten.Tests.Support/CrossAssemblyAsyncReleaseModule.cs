using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>A till whose implementation stays internal and is closed by the module's asynchronous release hook.</summary>
public interface IAsyncReleasedTill
{
	/// <summary>Whether the module's release hook ran.</summary>
	bool Closed { get; }
}

/// <summary>The marker the module's scan selects the asynchronously released plugins on.</summary>
public interface IAsyncReleasedPlugin;

/// <summary>The accessible interface the scanned grinder is registered and resolved under.</summary>
public interface IAsyncReleasedGrinder
{
	/// <summary>Whether the module's asynchronous scan release hook ran.</summary>
	bool Cleaned { get; }
}

internal sealed class AsyncReleasedTill : IAsyncReleasedTill
{
	public bool Closed { get; set; }
}

internal sealed class AsyncReleasedGrinder : IAsyncReleasedPlugin, IAsyncReleasedGrinder
{
	public bool Cleaned { get; set; }
}

/// <summary>
///     A <c>[Module]</c> whose registration and scan name <c>internal</c> asynchronous <c>OnRelease</c> hooks, so an
///     importing container in another assembly awaits them through the public wrappers the module generates.
/// </summary>
[Module]
[Scoped<AsyncReleasedTill, IAsyncReleasedTill>(OnRelease = nameof(Close))]
[Scan<IAsyncReleasedPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Scoped, OnRelease = nameof(Clean))]
public static partial class AsyncReleaseModule
{
	internal static async Task Close(AsyncReleasedTill till)
	{
		await Task.Yield();
		till.Closed = true;
	}

	internal static async ValueTask Clean(IAsyncReleasedPlugin plugin)
	{
		await Task.Yield();
		((AsyncReleasedGrinder)plugin).Cleaned = true;
	}
}
