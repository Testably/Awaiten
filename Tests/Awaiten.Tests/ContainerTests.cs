namespace Awaiten.Tests;

public sealed class ContainerTests
{
	[Test]
	public async Task ContainerAttribute_ShouldBeSealed()
	{
		Type sut = typeof(ContainerAttribute);

		await That(sut.IsSealed).IsTrue();
	}

	[Test]
	public async Task AsyncSafety_ShouldInitializeToStrict()
	{
		ContainerAttribute sut = new();

		await That(sut.AsyncSafety).IsEqualTo(AsyncSafety.Strict);
	}
}
