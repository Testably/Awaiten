namespace Awaiten.ExampleTests;

/// <summary>
///     End-to-end usage examples that double as documentation. Real examples
///     (declaring a <c>[Container]</c>, resolving services, async init) are added
///     once the generator lands.
/// </summary>
public sealed class ExampleTests
{
	[Fact]
	public async Task Placeholder_ShouldPass()
	{
		await That(true).IsTrue();
	}
}
