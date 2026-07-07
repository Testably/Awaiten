using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Startup-time verification that the external dependencies a generated container expects from the
///     host (its <c>[ImportService&lt;T&gt;]</c> / <c>[ImportServices]</c> dependencies) are actually registered
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

		List<AwaitenExternalDependency> missing = container.ExternalDependencies
			.Where(dependency => !IsRegistered(provider, dependency))
			.ToList();

		if (missing.Count > 0)
		{
			throw new InvalidOperationException(
				"Awaiten: the container's external dependencies are not registered in the provider: "
				+ string.Join(", ", missing.Select(Describe))
				+ ". Register them before building the provider, or remove the [ImportService<T>]/[ImportServices] usage.");
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

	private static bool IsRegistered(IServiceProvider provider, AwaitenExternalDependency dependency)
	{
		// A registration check without constructing the service: keyed dependencies probe the keyed surface
		// (a [FromKey] parameter resolves the registration under that key, not the unkeyed one), unkeyed ones
		// the ordinary surface. IServiceProviderIs(Keyed)Service is available on the modern abstractions this
		// package references; a provider that predates it (or does not implement it) falls through to the
		// resolution-based check below.
		if (dependency.Key is null)
		{
			if (provider.GetService(typeof(IServiceProviderIsService)) is IServiceProviderIsService probe)
			{
				return probe.IsService(dependency.ServiceType);
			}
		}
		else if (provider.GetService(typeof(IServiceProviderIsKeyedService)) is IServiceProviderIsKeyedService keyedProbe)
		{
			return keyedProbe.IsKeyedService(dependency.ServiceType, dependency.Key);
		}

		try
		{
			object? instance = dependency.Key is null
				? provider.GetService(dependency.ServiceType)
				: (provider as IKeyedServiceProvider)?.GetKeyedService(dependency.ServiceType, dependency.Key);
			return instance is not null;
		}
		catch (InvalidOperationException)
		{
			// The service is registered but resolving it from this provider is scope-restricted; it exists.
			return true;
		}
	}

	private static string Describe(AwaitenExternalDependency dependency)
		=> dependency.Key is null
			? dependency.ServiceType.ToString()
			: dependency.ServiceType + " (key: " + dependency.Key + ")";
}
