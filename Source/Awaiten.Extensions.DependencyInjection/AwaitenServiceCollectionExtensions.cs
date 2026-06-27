using System;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Extension methods that bridge a generated Awaiten container to
///     Microsoft.Extensions.DependencyInjection.
/// </summary>
public static class AwaitenServiceCollectionExtensions
{
	/// <summary>
	///     Projects the registrations of the generated Awaiten container
	///     <typeparamref name="TContainer" /> into the <paramref name="services" /> collection,
	///     so a Microsoft.Extensions.DependencyInjection host can resolve Awaiten-owned services.
	/// </summary>
	public static IServiceCollection AddGeneratedContainer<TContainer>(this IServiceCollection services)
		where TContainer : IAwaitenContainer, new()
	{
		if (services is null)
		{
			throw new ArgumentNullException(nameof(services));
		}

		TContainer container = new();
		services.AddSingleton<IAwaitenContainer>(container);

		// TODO: project TContainer.Registrations into the service collection.
		return services;
	}
}
