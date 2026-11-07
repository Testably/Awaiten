using System.Threading.Tasks;

namespace Awaiten.Tests.ContainerSupport;

/// <summary>A till the production container builds from a private implementation and closes asynchronously.</summary>
public interface ISupportAsyncReleasedTill
{
	/// <summary>Whether the base's private release hook ran.</summary>
	bool Closed { get; }
}

/// <summary>A kettle the production container cools through a private asynchronous release hook.</summary>
public sealed class SupportAsyncReleasedKettle
{
	/// <summary>Whether the base's private release hook ran.</summary>
	public bool Cooled { get; set; }
}

/// <summary>
///     A production container in a referenced assembly whose private implementation and public service have private
///     asynchronous release hooks, so a test container in another assembly awaits them through the internal wrappers the
///     base generates.
/// </summary>
[Container]
[Scoped<Till, ISupportAsyncReleasedTill>(OnRelease = nameof(Close))]
[Scoped<SupportAsyncReleasedKettle>(OnRelease = nameof(Cool))]
public static partial class SupportAsyncReleaseBaseContainer
{
	private static async Task Close(Till till)
	{
		await Task.Yield();
		till.Closed = true;
	}

	private static async ValueTask Cool(SupportAsyncReleasedKettle kettle)
	{
		await Task.Yield();
		kettle.Cooled = true;
	}

	private sealed class Till : ISupportAsyncReleasedTill
	{
		public bool Closed { get; set; }
	}
}
