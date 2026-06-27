namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed class AwaitenServiceCollectionExtensionsTests
{
	[Fact]
	public async Task AddGeneratedContainerExtensions_ShouldBeAStaticClass()
	{
		Type type = typeof(AwaitenServiceCollectionExtensions);

		await That(type.IsAbstract && type.IsSealed).IsTrue();
	}
}
