using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Startup-time verification that the external dependencies a generated container expects from the
///     host (its <c>[FromServices]</c> / <c>[ImportServices]</c> dependencies) are actually registered
///     in the provider. This is the runtime analog of the compile-time missing-dependency check
///     (AWT101) across the Microsoft.Extensions.DependencyInjection boundary, surfacing a
///     misconfiguration at startup rather than on first resolution.
/// </summary>
public static class AwaitenVerificationExtensions
{
	/// <summary>
	///     Verifies that every external dependency of <paramref name="container" /> is registered in
	///     <paramref name="provider" />, throwing an <see cref="InvalidOperationException" /> that lists
	///     the gaps when any is missing.
	/// </summary>
	/// <param name="container">The generated container's metadata.</param>
	/// <param name="provider">The provider expected to satisfy the external dependencies.</param>
	public static void VerifyAgainst(this IAwaitenContainerMetadata container, IServiceProvider provider)
	{
		if (container is null)
		{
			throw new ArgumentNullException(nameof(container));
		}

		if (provider is null)
		{
			throw new ArgumentNullException(nameof(provider));
		}

		List<Type> missing = container.ExternalDependencies
			.Where(dependency => !IsRegistered(provider, dependency))
			.ToList();

		if (missing.Count > 0)
		{
			throw new InvalidOperationException(
				"Awaiten: the container's external dependencies are not registered in the provider: "
				+ string.Join(", ", missing.Select(type => type.ToString()))
				+ ". Register them before building the provider, or remove the [FromServices]/[ImportServices] usage.");
		}
	}

	/// <summary>
	///     Verifies every generated container registered in <paramref name="provider" /> (through
	///     <see cref="AwaitenServiceCollectionExtensions.AddGeneratedContainer{TRoot}(Microsoft.Extensions.DependencyInjection.IServiceCollection)" />) against it,
	///     throwing when any container's external dependencies are unsatisfied. Returns the provider so it
	///     can be chained at startup.
	/// </summary>
	/// <param name="provider">The built provider to verify.</param>
	public static IServiceProvider VerifyAwaitenContainers(this IServiceProvider provider)
	{
		if (provider is null)
		{
			throw new ArgumentNullException(nameof(provider));
		}

		foreach (IAwaitenContainerMetadata container in provider.GetServices<IAwaitenContainerMetadata>())
		{
			container.VerifyAgainst(provider);
		}

		return provider;
	}

	private static bool IsRegistered(IServiceProvider provider, Type serviceType)
	{
#if NET6_0_OR_GREATER
		// IServiceProviderIsService gives a registration check without constructing the service; it is
		// absent on net48 / older abstractions, where the synchronous fallback below applies.
		if (provider.GetService(typeof(IServiceProviderIsService)) is IServiceProviderIsService probe)
		{
			return probe.IsService(serviceType);
		}
#endif
		try
		{
			return provider.GetService(serviceType) is not null;
		}
		catch (InvalidOperationException)
		{
			// The service is registered but resolving it from this provider is scope-restricted; it exists.
			return true;
		}
	}
}
