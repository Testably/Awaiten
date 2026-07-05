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

		await That(() => provider.VerifyAwaitenContainers()).Throws<InvalidOperationException>()
			.WithMessage("*IClock*").AsWildcard();
	}

	[Fact]
	public async Task VerifyAwaitenContainers_PassesWhenScopedExternalDependencyIsRegistered()
	{
		// A scoped external dependency is a registration like any other; the check runs against the root
		// provider and must not report it missing just because it cannot be resolved from the root itself.
		ServiceCollection services = new();
		services.AddScoped<ExternalDependencyTests.IClock>(_ => new ExternalDependencyTests.FixedClock());
		services.AddGeneratedContainer<ExternalDependencyTests.ScopedExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).DoesNotThrow();
	}

	[Fact]
	public async Task VerifyAwaitenContainers_PassesWhenKeyedExternalDependencyIsRegisteredUnderItsKey()
	{
		// Regression: a [FromKey] dependency resolves the keyed registration, so verification must probe the
		// keyed surface. An unkeyed IsService check would wrongly report the keyed registration missing.
		ServiceCollection services = new();
		services.AddKeyedSingleton<ExternalDependencyTests.IClock>("utc", new ExternalDependencyTests.FixedClock());
		services.AddGeneratedContainer<ExternalDependencyTests.KeyedExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).DoesNotThrow();
	}

	[Fact]
	public async Task VerifyAwaitenContainers_ThrowsWhenKeyedExternalDependencyIsRegisteredUnderTheWrongKey()
	{
		// An unkeyed registration and a differently-keyed one both leave the requested "utc" key unsatisfied.
		// Verification must not be fooled into passing (the false-negative direction of the same bug).
		ServiceCollection services = new();
		services.AddSingleton<ExternalDependencyTests.IClock>(new ExternalDependencyTests.FixedClock());
		services.AddKeyedSingleton<ExternalDependencyTests.IClock>("local", new ExternalDependencyTests.FixedClock());
		services.AddGeneratedContainer<ExternalDependencyTests.KeyedExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).Throws<InvalidOperationException>()
			.WithMessage("*IClock (key: utc)*").AsWildcard();
	}

	[Fact]
	public async Task VerifyAgainst_ThrowsListingTheMissingDependency()
	{
		ServiceCollection services = new();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
		using ExternalDependencyTests.ExternalContainer.Root container = new();

		await That(() => ((IAwaitenContainerMetadata)container).VerifyAgainst(provider))
			.Throws<InvalidOperationException>()
			.WithMessage(
				"Awaiten: the container's external dependencies are not registered in the provider: "
				+ "*IClock. Register them before building the provider, or remove the "
				+ "[FromServices]/[ImportServices] usage.")
			.AsWildcard();
	}

	[Fact]
	public async Task VerifyAgainst_ListsTheKeyOfAMissingKeyedDependency()
	{
		ServiceCollection services = new();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
		using ExternalDependencyTests.KeyedExternalContainer.Root container = new();

		await That(() => ((IAwaitenContainerMetadata)container).VerifyAgainst(provider))
			.Throws<InvalidOperationException>()
			.WithMessage("*IClock (key: utc)*").AsWildcard();
	}
}
