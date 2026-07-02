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
	///     Projects the registrations of the generated Awaiten container root <typeparamref name="TRoot" />
	///     (the generated <c>Root</c> type) into the <paramref name="services" /> collection, so a
	///     Microsoft.Extensions.DependencyInjection host can resolve Awaiten-owned services. Awaiten lifetimes
	///     map to the matching <see cref="ServiceLifetime" />, and one <see cref="IAwaitenScope" /> is aligned
	///     to each MS.DI scope.
	/// </summary>
	/// <remarks>
	///     Awaiten owns construction and lifetime; the host owns disposal of the resolved instances (MS.DI
	///     disposes them with the scope they were resolved from - singletons at the root provider, scoped and
	///     transient services at their scope). The container root and the per-scope <see cref="IAwaitenScope" />
	///     are registered as instances / non-disposable holders so MS.DI does not dispose them twice.
	/// </remarks>
	/// <typeparam name="TRoot">The generated Awaiten container root type.</typeparam>
	public static IServiceCollection AddGeneratedContainer<TRoot>(this IServiceCollection services)
		where TRoot : class, IAwaitenContainerMetadata, new()
	{
		if (services is null)
		{
			throw new ArgumentNullException(nameof(services));
		}

		TRoot root = new();

		// The container root and its scopes are registered as instances / non-disposable holders so MS.DI does
		// not dispose them: the relayed instances are disposed once, by the MS.DI scope they belong to.
		services.AddSingleton(root);
		services.AddSingleton<IAwaitenScope>(root);
		services.AddSingleton<IAwaitenContainerMetadata>(root);
		services.AddScoped(_ => new AwaitenScopeHolder(root.CreateScope()));

		foreach (AwaitenRegistration registration in root.Registrations)
		{
			Type serviceType = registration.ServiceType;
			switch (registration.Lifetime)
			{
				case AwaitenLifetime.Singleton:
					// A singleton is owned by the root; the relay hands back the shared instance.
					services.Add(new ServiceDescriptor(serviceType, _ => root.Resolve(serviceType), ServiceLifetime.Singleton));
					break;
				case AwaitenLifetime.Scoped:
					services.Add(new ServiceDescriptor(
						serviceType,
						sp => sp.GetRequiredService<AwaitenScopeHolder>().Scope.Resolve(serviceType),
						ServiceLifetime.Scoped));
					break;
				default:
					services.Add(new ServiceDescriptor(
						serviceType,
						sp => sp.GetRequiredService<AwaitenScopeHolder>().Scope.Resolve(serviceType),
						ServiceLifetime.Transient));
					break;
			}
		}

		return services;
	}
}
