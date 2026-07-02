using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Extension methods that bridge a generated Awaiten container to
///     Microsoft.Extensions.DependencyInjection.
/// </summary>
public static class AwaitenServiceCollectionExtensions
{
	// Adapts the container's Task<object> ResolveAsync result to the strongly-typed Task<T> a consumer asks
	// for. Closed over the requested service type once per async registration (MakeGenericMethod), then
	// invoked per resolution.
	private static readonly MethodInfo AsTypedTaskMethod =
		typeof(AwaitenServiceCollectionExtensions).GetMethod(nameof(AsTypedTask), BindingFlags.NonPublic | BindingFlags.Static)!;

	/// <summary>
	///     Projects the registrations of the generated Awaiten container root <typeparamref name="TRoot" />
	///     (the generated <c>Root</c> type) into the <paramref name="services" /> collection, so a
	///     Microsoft.Extensions.DependencyInjection host can resolve Awaiten-owned services. Awaiten lifetimes
	///     map to the matching <see cref="ServiceLifetime" />, and one <see cref="IAwaitenScope" /> is aligned
	///     to each MS.DI scope. A service that requires asynchronous initialization is projected as a
	///     <c>Task&lt;T&gt;</c> (resolve <c>Task&lt;TService&gt;</c> and await it), since it has no synchronous
	///     resolution path.
	/// </summary>
	/// <remarks>
	///     <para>
	///         Awaiten owns construction; the resolved (top-level) instances are handed back through factories,
	///         so <b>MS.DI owns their disposal</b> - it disposes each with the scope it was resolved from
	///         (singletons and transients from the root provider, scoped services from their scope). Singletons
	///         resolve from the container root; transients resolve from the root too (so they are resolvable
	///         from the root provider, and their dependencies are the root's); scoped services resolve from the
	///         Awaiten scope aligned to the current MS.DI scope.
	///     </para>
	///     <para>
	///         Because the aligned Awaiten scope is not itself disposed by the bridge, this projection does
	///         <b>not</b> dispose instances Awaiten builds only as nested dependencies (an MS.DI-captured
	///         top-level instance is disposed; a disposable it depends on that is never resolved on its own is
	///         not), and an implementation exposed under several service types is disposed once per resolved
	///         service type. When the Awaiten container should be the single owner of disposal end to end, make
	///         it the provider with <see cref="AwaitenServiceProviderFactory{TRoot}" /> /
	///         <see cref="AwaitenServiceProvider" /> instead of projecting it here. Collection resolution
	///         (<c>IEnumerable&lt;T&gt;</c> of every registration of a service) is not projected; only the
	///         single-resolution winner of each service type is bridged.
	///     </para>
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
		// not dispose them: the relayed instances are disposed by the MS.DI scope they belong to.
		services.AddSingleton(root);
		services.AddSingleton<IAwaitenScope>(root);
		services.AddSingleton<IAwaitenContainerMetadata>(root);
		services.AddScoped(_ => new AwaitenScopeHolder(root.CreateScope()));

		foreach (AwaitenRegistration registration in root.Registrations)
		{
			Type serviceType = registration.ServiceType;
			AwaitenLifetime lifetime = registration.Lifetime;
			ServiceLifetime serviceLifetime = ToServiceLifetime(lifetime);

			if (registration.RequiresAsync)
			{
				// No synchronous resolution path: expose it as Task<TService>, resolved through ResolveAsync.
				Type taskType = typeof(Task<>).MakeGenericType(serviceType);
				MethodInfo asTypedTask = AsTypedTaskMethod.MakeGenericMethod(serviceType);
				services.Add(new ServiceDescriptor(
					taskType,
					sp => asTypedTask.Invoke(null, new object[] { ScopeFor(sp, lifetime, root).ResolveAsync(serviceType) })!,
					serviceLifetime));
			}
			else
			{
				services.Add(new ServiceDescriptor(
					serviceType,
					sp => ScopeFor(sp, lifetime, root).Resolve(serviceType),
					serviceLifetime));
			}
		}

		return services;
	}

	// The Awaiten scope a service resolves from: scoped services from the Awaiten scope aligned to the current
	// MS.DI scope (so they are shared within it), singletons and transients from the container root (so a
	// transient is resolvable from the root provider, not only from a scope).
	private static IAwaitenScope ScopeFor(IServiceProvider provider, AwaitenLifetime lifetime, IAwaitenScope root)
		=> lifetime == AwaitenLifetime.Scoped
			? provider.GetRequiredService<AwaitenScopeHolder>().Scope
			: root;

	private static ServiceLifetime ToServiceLifetime(AwaitenLifetime lifetime) => lifetime switch
	{
		AwaitenLifetime.Singleton => ServiceLifetime.Singleton,
		AwaitenLifetime.Scoped => ServiceLifetime.Scoped,
		_ => ServiceLifetime.Transient,
	};

	private static async Task<T> AsTypedTask<T>(Task<object> resolution) => (T)await resolution.ConfigureAwait(false);
}
