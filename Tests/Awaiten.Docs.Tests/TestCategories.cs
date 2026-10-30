namespace Awaiten.Docs.Tests;

/// <summary>
///     The test categories that the pipeline runs in a separate leg.
/// </summary>
public static class TestCategories
{
	/// <summary>
	///     Explicit tests that are too slow for the regular run, selected with
	///     <c>--treenode-filter "/*/*/*/*[Category=Slow]"</c>.
	/// </summary>
	public const string Slow = "Slow";
}
