using System;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Adapts a Microsoft.Extensions.DependencyInjection <see cref="IServiceProvider" /> to Awaiten's
///     <see cref="IExternalResolver" /> seam, so a generated container can resolve its
///     <c>[FromServices]</c> / <c>[ImportServices]</c> dependencies from the host's provider.
/// </summary>
public sealed class ServiceProviderExternalResolver : IExternalResolver
{
	private readonly IServiceProvider _provider;

	/// <summary>
	///     Initializes a new instance of the <see cref="ServiceProviderExternalResolver" /> class.
	/// </summary>
	/// <param name="provider">The provider to resolve external dependencies from.</param>
	public ServiceProviderExternalResolver(IServiceProvider provider)
		=> _provider = provider ?? throw new ArgumentNullException(nameof(provider));

	/// <inheritdoc />
	public bool TryResolve(Type serviceType, object? serviceKey, out object? instance)
	{
		if (serviceType is null)
		{
			throw new ArgumentNullException(nameof(serviceType));
		}

		// A keyed [FromKey] dependency resolves through the provider's keyed surface (available on the MS.DI
		// provider); an unkeyed one through the ordinary one. A provider that does not support keyed services
		// simply yields no instance, which surfaces as the container's clear "not available" message.
		instance = serviceKey is null
			? _provider.GetService(serviceType)
			: (_provider as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);
		return instance is not null;
	}
}
