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
	///     Projects the registrations of the generated Awaiten container root
	///     <typeparamref name="TRoot" /> (the generated <c>Root</c> type) into the
	///     <paramref name="services" /> collection, so a Microsoft.Extensions.DependencyInjection host can
	///     resolve Awaiten-owned services.
	/// </summary>
	public static IServiceCollection AddGeneratedContainer<TRoot>(this IServiceCollection services)
		where TRoot : class, IAwaitenScope, new()
	{
		if (services is null)
		{
			throw new ArgumentNullException(nameof(services));
		}

		TRoot root = new();
		services.AddSingleton<IAwaitenScope>(root);

		// TODO: project TRoot's registrations into the service collection.
		return services;
	}
}
