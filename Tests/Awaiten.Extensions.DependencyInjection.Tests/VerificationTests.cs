using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed class VerificationTests
{
	[Fact]
	public async Task VerifyAwaitenContainers_PassesWhenExternalDependencyIsRegistered()
	{
		ServiceCollection services = new();
		services.AddSingleton<ExternalDependencyTests.IClock>(new ExternalDependencyTests.FixedClock());
		services.AddGeneratedContainer<ExternalDependencyTests.ExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).DoesNotThrow();
	}

	[Fact]
	public async Task VerifyAwaitenContainers_ThrowsWhenExternalDependencyIsMissing()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<ExternalDependencyTests.ExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).Throws<InvalidOperationException>();
	}

	[Fact]
	public async Task VerifyAgainst_ThrowsListingTheMissingDependency()
	{
		ServiceCollection services = new();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
		using ExternalDependencyTests.ExternalContainer.Root container = new();

		await That(() => ((IAwaitenContainerMetadata)container).VerifyAgainst(provider))
			.Throws<InvalidOperationException>();
	}
}
