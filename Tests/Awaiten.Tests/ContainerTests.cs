namespace Awaiten.Tests;

public sealed class ContainerTests
{
	[Fact]
	public async Task ContainerAttribute_ShouldBeSealed()
	{
		await That(typeof(ContainerAttribute).IsSealed).IsTrue();
	}
}
