using System;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     An <see cref="IServiceProviderFactory{TContainerBuilder}" /> that builds the application's
///     <see cref="IServiceProvider" /> from a generated Awaiten container root <typeparamref name="TRoot" />
///     (wrapped in an <see cref="AwaitenServiceProvider" />), so the Awaiten container is the single owner of
///     construction and disposal end to end - each resolved instance, and everything built for it, is
///     disposed exactly once when its scope (or the root provider) is disposed.
/// </summary>
/// <remarks>
///     Awaiten is a compile-time container: it resolves only the services its <c>[Container]</c> declares and
///     cannot serve registrations a host adds to the <see cref="IServiceCollection" /> (framework services,
///     or anything registered with <c>AddSingleton</c> / <c>AddScoped</c> / <c>AddTransient</c>). So this
///     factory requires the collection to be empty - the Awaiten container is the whole provider - and throws
///     otherwise rather than silently dropping those registrations. To expose Awaiten services inside a
///     Microsoft.Extensions.DependencyInjection host that also owns other services, project them into the
///     host's collection with <see cref="AwaitenServiceCollectionExtensions.AddGeneratedContainer{TRoot}" />
///     instead.
/// </remarks>
/// <typeparam name="TRoot">The generated Awaiten container root type.</typeparam>
public sealed class AwaitenServiceProviderFactory<TRoot> : IServiceProviderFactory<TRoot>
	where TRoot : class, IAwaitenScope, new()
{
	/// <inheritdoc />
	/// <exception cref="ArgumentNullException"><paramref name="services" /> is <see langword="null" />.</exception>
	/// <exception cref="NotSupportedException">
	///     <paramref name="services" /> is not empty. The Awaiten container is the whole provider here and
	///     cannot resolve services registered in the collection; project it with
	///     <see cref="AwaitenServiceCollectionExtensions.AddGeneratedContainer{TRoot}" /> to coexist with them.
	/// </exception>
	public TRoot CreateBuilder(IServiceCollection services)
	{
		if (services is null)
		{
			throw new ArgumentNullException(nameof(services));
		}

		if (services.Count > 0)
		{
			throw new NotSupportedException(
				$"{nameof(AwaitenServiceProviderFactory<TRoot>)} makes the Awaiten container the whole service " +
				"provider, which cannot resolve the services registered in the IServiceCollection. Leave the " +
				$"collection empty, or project the container with {nameof(AwaitenServiceCollectionExtensions)}." +
				$"{nameof(AwaitenServiceCollectionExtensions.AddGeneratedContainer)} to coexist with other registrations.");
		}

		return new();
	}

	/// <inheritdoc />
	public IServiceProvider CreateServiceProvider(TRoot containerBuilder)
		=> new AwaitenServiceProvider(containerBuilder ?? throw new ArgumentNullException(nameof(containerBuilder)));
}
