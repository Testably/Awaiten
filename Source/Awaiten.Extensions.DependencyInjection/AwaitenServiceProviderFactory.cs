using System;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     An <see cref="IServiceProviderFactory{TContainerBuilder}" /> that hosts a generated Awaiten container root
///     <typeparamref name="TRoot" /> inside a Microsoft.Extensions.DependencyInjection host.
///     <see cref="CreateServiceProvider" /> projects the container into the host's
///     <see cref="IServiceCollection" /> (through
///     <see cref="AwaitenServiceCollectionExtensions.AddGeneratedContainer{TRoot}(IServiceCollection)" />) and
///     builds a standard MS.DI provider, so the host's framework registrations and the Awaiten-owned services
///     resolve side by side, including Awaiten services injected into host services. Hand it to
///     <c>UseServiceProviderFactory(new AwaitenServiceProviderFactory&lt;MyContainer.Root&gt;())</c>.
/// </summary>
/// <remarks>
///     The projection's ownership rules apply; see the remarks on
///     <see cref="AwaitenServiceCollectionExtensions.AddGeneratedContainer{TRoot}(IServiceCollection)" />. The
///     built provider owns the root that <see cref="CreateBuilder" /> created and disposes it with itself. To make
///     the Awaiten container the single owner of construction and disposal end to end, with no host registrations,
///     wrap it in an <see cref="AwaitenServiceProvider" /> directly; that scenario needs no factory.
/// </remarks>
/// <typeparam name="TRoot">The generated Awaiten container root type.</typeparam>
public sealed class AwaitenServiceProviderFactory<TRoot> : IServiceProviderFactory<TRoot>
	where TRoot : class, IAwaitenContainerMetadata, new()
{
	private IServiceCollection? _services;

	/// <inheritdoc />
	/// <exception cref="ArgumentNullException"><paramref name="services" /> is <see langword="null" />.</exception>
	public TRoot CreateBuilder(IServiceCollection services)
	{
		_services = services ?? throw new ArgumentNullException(nameof(services));
		return new();
	}

	/// <inheritdoc />
	/// <exception cref="ArgumentNullException"><paramref name="containerBuilder" /> is <see langword="null" />.</exception>
	/// <exception cref="InvalidOperationException"><see cref="CreateBuilder" /> has not been called.</exception>
	public IServiceProvider CreateServiceProvider(TRoot containerBuilder)
	{
		if (containerBuilder is null)
		{
			throw new ArgumentNullException(nameof(containerBuilder));
		}

		if (_services is null)
		{
			throw new InvalidOperationException(
				$"{nameof(CreateBuilder)} must be called before {nameof(CreateServiceProvider)}.");
		}

		ServiceProvider provider = AwaitenServiceCollectionExtensions.AddGeneratedContainer(_services, containerBuilder)
			.BuildServiceProvider();

		// Resolving the owner up front makes the provider own the root even when the host only ever reaches the
		// root directly, and puts it first in MS.DI's capture order, so the root is disposed last.
		provider.GetRequiredService<AwaitenRootOwner<TRoot>>();
		return provider;
	}
}
