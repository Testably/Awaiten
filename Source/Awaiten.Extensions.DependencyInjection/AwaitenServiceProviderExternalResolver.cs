using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Adapts a Microsoft.Extensions.DependencyInjection <see cref="IServiceProvider" /> to Awaiten's
///     <see cref="IExternalResolver" /> seam, so a generated container can resolve its
///     <c>[ImportService&lt;T&gt;]</c> / <c>[ImportServices]</c> dependencies from the host's provider.
/// </summary>
public sealed class AwaitenServiceProviderExternalResolver : IExternalResolver
{
	private readonly IServiceProvider _provider;

	/// <param name="provider">The provider to resolve external dependencies from.</param>
	public AwaitenServiceProviderExternalResolver(IServiceProvider provider)
		=> _provider = provider ?? throw new ArgumentNullException(nameof(provider));

	/// <inheritdoc />
	public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
	{
		if (serviceType is null)
		{
			throw new ArgumentNullException(nameof(serviceType));
		}

		// A keyed [FromKey] dependency resolves through the provider's keyed surface, an unkeyed one through the
		// ordinary one. A provider without keyed support simply yields no instance, which surfaces as the
		// container's clear "not available" message.
		instance = key is null
			? _provider.GetService(serviceType)
			: (_provider as IKeyedServiceProvider)?.GetKeyedService(serviceType, key);
		return instance is not null;
	}
}
