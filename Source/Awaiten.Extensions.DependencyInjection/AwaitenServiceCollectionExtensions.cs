using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
	///     to each MS.DI scope. A service that requires asynchronous initialization is projected as a
	///     <c>Task&lt;T&gt;</c> (resolve <c>Task&lt;TService&gt;</c> and await it), since it has no synchronous
	///     resolution path.
	/// </summary>
	/// <remarks>
	///     <para>
	///         Awaiten owns construction; the resolved (top-level) instances are handed back through factories,
	///         so <b>MS.DI owns their disposal</b> - it disposes each with the scope it was resolved from.
	///         Singletons resolve from the container root. Scoped services - and transients resolved inside an
	///         MS.DI scope - resolve from the Awaiten scope aligned to that MS.DI scope, so a transient shares
	///         its scoped dependencies with the scope it is resolved in and the disposables built for it are
	///         bounded by the scope; a transient resolved from the root provider resolves from the container
	///         root (mirroring where MS.DI tracks a native transient). An instance awaited through the
	///         <c>Task&lt;T&gt;</c> projection is disposed by the bridge with the matching scope (or with the
	///         root provider), since MS.DI captures only the <c>Task</c> wrapper. A pre-built <c>Instance</c>
	///         member stays user-owned: it is relayed without being captured for disposal.
	///     </para>
	///     <para>
	///         Because the aligned Awaiten scope is not itself disposed by the bridge, this projection does
	///         <b>not</b> dispose instances Awaiten builds only as nested dependencies (an MS.DI-captured
	///         top-level instance is disposed; a disposable it depends on that is never resolved on its own is
	///         not), and an implementation exposed under several service types is disposed once per resolved
	///         service type. When the Awaiten container should be the single owner of disposal end to end, make
	///         it the provider with <see cref="AwaitenServiceProvider" /> instead of projecting it here.
	///         Collection resolution
	///         (<c>IEnumerable&lt;T&gt;</c> of every registration of a service) is not projected; only the
	///         single-resolution winner of each service type is bridged.
	///     </para>
	///     <para>
	///         Under the strict default (<c>LifetimeSafety.Strict</c>), a disposable transient resolved from
	///         the <b>root provider</b> (not from a scope) throws the container's guidance, exactly as resolving
	///         it on the container root would; resolve it from a scope instead, or use
	///         <c>LifetimeSafety.Loose</c>. A container using <c>SyncResolveAfterInit</c> should be warmed at
	///         startup (resolve <see cref="IAwaitenContainerMetadata" /> and await <c>InitializeAsync</c>)
	///         before its async-initialized services are first resolved; otherwise the first resolution blocks
	///         synchronously on initialization. Do not dispose a scope or the provider while a resolved
	///         <c>Task&lt;T&gt;</c> is still in flight: MS.DI disposes the captured <c>Task</c>, whose
	///         <c>Dispose</c> throws for an incomplete task (the awaited instance itself is still disposed
	///         when the resolution completes).
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

		return AddGeneratedContainer(services, new TRoot());
	}

	// The projection core, taking an existing root so AwaitenServiceProviderFactory can project the root it
	// handed to the host's ConfigureContainer callbacks instead of a fresh one.
	internal static IServiceCollection AddGeneratedContainer<TRoot>(IServiceCollection services, TRoot root)
		where TRoot : class, IAwaitenContainerMetadata, new()
	{
		// The container root and the per-scope holders are registered so MS.DI does not dispose the relayed
		// synchronous instances twice: those are disposed by the MS.DI scope they were resolved from. An
		// instance awaited through the Task<T> projection is disposed by the transient slot captured by
		// MS.DI at the position of its resolution.
		services.AddSingleton(root);
		services.AddSingleton<IAwaitenScope>(root);
		services.AddSingleton<IAwaitenContainerMetadata>(root);
		services.AddScoped(_ => new AwaitenScopeHolder<TRoot>(root.CreateScope()));
		services.TryAddTransient<AwaitenAsyncDisposalSlot>();
		services.TryAddSingleton(sp => new AwaitenRootProviderProbe(sp));

		foreach (AwaitenRegistration registration in root.Registrations)
		{
			Type serviceType = registration.ServiceType;
			AwaitenLifetime lifetime = registration.Lifetime;

			if (registration.ExternallyOwned)
			{
				// A pre-built container member is user-owned: registering the resolved instance itself relays
				// it without MS.DI capturing it for disposal (the container does not own it either).
				services.Add(new ServiceDescriptor(serviceType, root.Resolve(serviceType)));
			}
			else if (registration.RequiresAsync)
			{
				// No synchronous resolution path: expose it as Task<TService>, resolved through ResolveAsync.
				// MS.DI captures only the returned Task, so the awaited instance is handed to a transient
				// slot resolved alongside it - captured by MS.DI at the same position, and therefore disposed
				// in the same reverse order as a natively registered instance.
				Type taskType = typeof(Task<>).MakeGenericType(serviceType);
				Func<Task<object>, object> asTypedTask = AwaitenTaskConverter.For(serviceType);
				services.Add(new ServiceDescriptor(
					taskType,
					sp =>
					{
						AwaitenAsyncDisposalSlot slot = sp.GetRequiredService<AwaitenAsyncDisposalSlot>();
						return asTypedTask(FillSlot(ScopeFor<TRoot>(sp, lifetime, root).ResolveAsync(serviceType), slot));
					},
					ToServiceLifetime(lifetime)));
			}
			else
			{
				services.Add(new ServiceDescriptor(
					serviceType,
					sp => ScopeFor<TRoot>(sp, lifetime, root).Resolve(serviceType),
					ToServiceLifetime(lifetime)));
			}
		}

		return services;
	}

	// The Awaiten scope a service resolves from: singletons from the container root; scoped services from the
	// Awaiten scope aligned to the current MS.DI scope (so they are shared within it); transients from that
	// aligned scope too when resolved inside a scope (their scoped dependencies are then the scope's, and the
	// disposables built for them are bounded by the scope instead of accumulating on the root), falling back
	// to the root only when resolved from the root provider itself.
	private static IAwaitenScope ScopeFor<TRoot>(IServiceProvider provider, AwaitenLifetime lifetime, TRoot root)
		where TRoot : class, IAwaitenContainerMetadata, new()
		=> lifetime == AwaitenLifetime.Singleton || (lifetime == AwaitenLifetime.Transient && IsRootProvider(provider))
			? root
			: provider.GetRequiredService<AwaitenScopeHolder<TRoot>>().Scope;

	// Singleton factories run against the root provider, so the captured probe identifies it: a factory whose
	// current provider is that same instance is resolving from the root provider, not from a scope.
	private static bool IsRootProvider(IServiceProvider provider)
		=> ReferenceEquals(provider.GetRequiredService<AwaitenRootProviderProbe>().RootProvider, provider);

	private static ServiceLifetime ToServiceLifetime(AwaitenLifetime lifetime) => lifetime switch
	{
		AwaitenLifetime.Singleton => ServiceLifetime.Singleton,
		AwaitenLifetime.Scoped => ServiceLifetime.Scoped,
		_ => ServiceLifetime.Transient,
	};

	private static async Task<object> FillSlot(Task<object> resolution, AwaitenAsyncDisposalSlot slot)
	{
		object instance = await resolution.ConfigureAwait(false);
		await slot.Fill(instance).ConfigureAwait(false);
		return instance;
	}
}
