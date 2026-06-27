namespace Awaiten.Tests;

public sealed class ContainerTests
{
	[Fact]
	public async Task ContainerAttribute_ShouldBeSealed()
	{
		Type sut = typeof(ContainerAttribute);

		await That(sut.IsSealed).IsTrue();
	}

	[Fact]
	public async Task SyncResolveAfterInit_ShouldInitializeToFalse()
	{
		ContainerAttribute sut = new();

		await That(sut.SyncResolveAfterInit).IsFalse();
	}
}
