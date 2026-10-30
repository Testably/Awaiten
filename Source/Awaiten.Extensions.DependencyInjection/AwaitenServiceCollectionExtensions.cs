using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>Extension methods that bridge a generated Awaiten container to Microsoft.Extensions.DependencyInjection.</summary>
public static class AwaitenServiceCollectionExtensions
{
	/// <summary>
	///     Projects the registrations of the generated Awaiten container root <typeparamref name="TRoot" /> into
	///     the <paramref name="services" /> collection, so a Microsoft.Extensions.DependencyInjection host can
	///     resolve Awaiten-owned services. Awaiten lifetimes map to the matching <see cref="ServiceLifetime" />,
	///     and one <see cref="IAwaitenScope" /> is aligned to each MS.DI scope. A service that requires
	///     asynchronous initialization is projected as a <c>Task&lt;T&gt;</c> (resolve <c>Task&lt;TService&gt;</c>
	///     and await it), since it has no synchronous resolution path.
	/// </summary>
	/// <remarks>
	///     <para>
	///         Singletons resolve from the container root. Scoped services, and transients resolved inside an MS.DI
	///         scope, resolve from the Awaiten scope aligned to that MS.DI scope. A transient resolved from the root
	///         provider resolves from the container root. Awaiten disposes everything it built exactly once, nested
	///         dependencies and instances awaited through the <c>Task&lt;T&gt;</c> projection included: the aligned
	///         Awaiten scope with its MS.DI scope, and the container root with the root provider once the bridge
	///         has used it, unless the caller passed the root in. A pre-built <c>Instance</c> member stays user-owned and is relayed without being
	///         captured for disposal.
	///     </para>
	///     <para>
	///         MS.DI additionally disposes every instance a projected factory hands out, once per service type it
	///         was resolved as, exactly as it does for a factory relay such as
	///         <c>AddSingleton&lt;IWriter&gt;(sp =&gt; sp.GetRequiredService&lt;Ledger&gt;())</c>. That relies on
	///         <see cref="IDisposable.Dispose" /> being idempotent, and MS.DI disposes those instances before the
	///         Awaiten scope or root that built them. Collection resolution (<c>IEnumerable&lt;T&gt;</c>) is not
	///         projected; only the single-resolution winner of each service type is bridged.
	///     </para>
	///     <para>
	///         Under the strict default (<c>LifetimeSafety.Strict</c>), a disposable transient resolved from the
	///         root provider rather than a scope throws the container's guidance. Resolve it from a scope, or use
	///         <c>LifetimeSafety.Loose</c>. Warm a <c>SyncResolveAfterInit</c> container at startup with
	///         <see cref="AwaitenContainerBuilder{TRoot}.WithInitialization" /> before its async-initialized services
	///         are first resolved, otherwise the first resolution blocks synchronously. Do not
	///         dispose a scope or the provider while a resolved <c>Task&lt;T&gt;</c> is still in flight: MS.DI
	///         disposes the captured <c>Task</c>, whose <c>Dispose</c> throws for an incomplete task.
	///     </para>
	/// </remarks>
	/// <typeparam name="TRoot">The generated Awaiten container root type.</typeparam>
	/// <returns>A builder to configure the container's hosting further, for example its startup initialization.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="services" /> is <see langword="null" />.</exception>
	public static AwaitenContainerBuilder<TRoot> AddAwaitenContainer<TRoot>(this IServiceCollection services)
		where TRoot : class, IAwaitenRoot, new()
	{
		if (services is null)
		{
			throw new ArgumentNullException(nameof(services));
		}

		return AddAwaitenContainer(services, new TRoot(), ownsRoot: true);
	}

	/// <summary>
	///     Projects the registrations of the pre-built Awaiten container <paramref name="root" /> into the
	///     <paramref name="services" /> collection, exactly as
	///     <see cref="AddAwaitenContainer{TRoot}(IServiceCollection)" /> does with a root it creates. Use it to
	///     configure the root before handing it to the host, for example to construct it with an external resolver.
	/// </summary>
	/// <remarks>
	///     Following the MS.DI convention for registered instances, <paramref name="root" /> stays owned by the
	///     caller: the provider disposes the scopes and instances it reaches through the bridge, but never the root
	///     itself. Dispose the root after the provider.
	/// </remarks>
	/// <param name="services">The service collection to register the container in.</param>
	/// <param name="root">The container root to project, owned by the caller.</param>
	/// <typeparam name="TRoot">The generated Awaiten container root type.</typeparam>
	/// <returns>A builder to configure the container's hosting further, for example its startup initialization.</returns>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="services" /> or <paramref name="root" /> is <see langword="null" />.
	/// </exception>
	public static AwaitenContainerBuilder<TRoot> AddAwaitenContainer<TRoot>(this IServiceCollection services, TRoot root)
		where TRoot : class, IAwaitenRoot
	{
		if (services is null)
		{
			throw new ArgumentNullException(nameof(services));
		}

		if (root is null)
		{
			throw new ArgumentNullException(nameof(root));
		}

		return AddAwaitenContainer(services, root, ownsRoot: false);
	}

	/// <summary>
	///     Projects <paramref name="root" />, and makes the root provider dispose it when
	///     <paramref name="ownsRoot" /> is set: the bridge or <see cref="AwaitenServiceProviderFactory{TRoot}" />
	///     created it, so nobody else can.
	/// </summary>
	internal static AwaitenContainerBuilder<TRoot> AddAwaitenContainer<TRoot>(IServiceCollection services, TRoot root, bool ownsRoot)
		where TRoot : class, IAwaitenRoot
	{
		// Expose the root under its own type and the two container-facing interfaces so a host can reach it, e.g.
		// to warm a SyncResolveAfterInit container. Registered as a pre-built instance (a constant), which MS.DI
		// never captures for disposal: the factory-registered owner disposes a root the bridge owns instead, at a capture
		// position that precedes everything the root builds.
		services.AddSingleton(root);
		services.AddSingleton<IAwaitenScope>(root);
		services.AddSingleton<IAwaitenRoot>(root);
		services.AddSingleton(_ => new AwaitenRootOwner<TRoot>(root, ownsRoot));
		services.TryAddSingleton(sp => new AwaitenRootProviderProbe(sp));

		// If the container draws on external (host-owned) services, wire its external resolvers to the host's
		// providers on first resolution below: the root to the root provider (so externally-resolved singletons
		// stay valid for the app's lifetime), and each Awaiten scope to its aligned MS.DI scope's provider.
		bool hasExternal = root.ExternalDependencies.Count > 0;

		services.AddScoped(sp =>
		{
			IAwaitenScope scope = sp.GetRequiredService<AwaitenRootOwner<TRoot>>().Root.CreateScope();
			if (hasExternal)
			{
				((IAwaitenExternalResolverHost)scope).ExternalResolver = new ServiceProviderExternalResolver(sp);
			}

			return new AwaitenScopeHolder<TRoot>(scope);
		});

		foreach (AwaitenRegistration registration in root.Registrations)
		{
			ProjectRegistration(services, root, registration, hasExternal);
		}

		return new AwaitenContainerBuilder<TRoot>(services);
	}

	/// <summary>
	///     Projects a single Awaiten registration into the service collection. A user-keyed registration is projected
	///     as a keyed MS.DI descriptor, so <c>[FromKeyedServices]</c> reaches it through the host's
	///     <see cref="IKeyedServiceProvider" />; an unkeyed one is projected as an ordinary descriptor.
	/// </summary>
	private static void ProjectRegistration<TRoot>(IServiceCollection services, TRoot root, AwaitenRegistration registration, bool hasExternal)
		where TRoot : class, IAwaitenRoot
	{
		Type serviceType = registration.ServiceType;
		Lifetime lifetime = registration.Lifetime;
		object? key = registration.Key;

		if (registration.ExternallyOwned)
		{
			// A pre-built container member is user-owned. Registering the resolved instance as a constant
			// relays it without MS.DI capturing it for disposal.
			object instance = root.Resolve(serviceType, key);
			services.Add(key is null
				? new ServiceDescriptor(serviceType, instance)
				: new ServiceDescriptor(serviceType, key, instance));
			return;
		}

		if (registration.RequiresAsync)
		{
			// No synchronous path: expose it as Task<TService>, resolved through ResolveAsync. MS.DI captures
			// only the returned Task, and the Awaiten scope or root that builds the awaited instance disposes it.
			// The closed Task<TService> type and the Task<object>->Task<T> converter are emitted by the generator.
			Type taskType = registration.AsyncTaskType!;
			Func<Task<object>, object> asTypedTask = registration.AsyncTaskConverter!;
			services.Add(key is null
				? new ServiceDescriptor(
					taskType,
					sp =>
					{
						EnsureExternalWired(sp, root, hasExternal);
						return asTypedTask(ScopeFor<TRoot>(sp, lifetime).ResolveAsync(serviceType, key));
					},
					ToServiceLifetime(lifetime))
				: new ServiceDescriptor(
					taskType,
					key,
					(sp, _) =>
					{
						EnsureExternalWired(sp, root, hasExternal);
						return asTypedTask(ScopeFor<TRoot>(sp, lifetime).ResolveAsync(serviceType, key));
					},
					ToServiceLifetime(lifetime)));
			return;
		}

		services.Add(key is null
			? new ServiceDescriptor(
				serviceType,
				sp =>
				{
					EnsureExternalWired(sp, root, hasExternal);
					return ScopeFor<TRoot>(sp, lifetime).Resolve(serviceType, key);
				},
				ToServiceLifetime(lifetime))
			: new ServiceDescriptor(
				serviceType,
				key,
				(sp, _) =>
				{
					EnsureExternalWired(sp, root, hasExternal);
					return ScopeFor<TRoot>(sp, lifetime).Resolve(serviceType, key);
				},
				ToServiceLifetime(lifetime)));
	}

	/// <summary>
	///     The Awaiten scope a service resolves from: singletons from the container root; scoped services from the
	///     Awaiten scope aligned to the current MS.DI scope; transients from that aligned scope too when resolved
	///     inside a scope (so their disposables are bounded by the scope), falling back to the root only when
	///     resolved from the root provider itself. The root is reached through its owner and the aligned scope
	///     through its holder, so MS.DI captures the one that disposes the instance before the instance itself.
	/// </summary>
	private static IAwaitenScope ScopeFor<TRoot>(IServiceProvider provider, Lifetime lifetime)
		where TRoot : class, IAwaitenRoot
		=> lifetime == Lifetime.Singleton || (lifetime == Lifetime.Transient && IsRootProvider(provider))
			? provider.GetRequiredService<AwaitenRootOwner<TRoot>>().Root
			: provider.GetRequiredService<AwaitenScopeHolder<TRoot>>().Scope;

	/// <summary>
	///     The captured probe identifies the root provider: a factory whose current provider is that same instance
	///     is resolving from the root provider, not from a scope.
	/// </summary>
	private static bool IsRootProvider(IServiceProvider provider)
		=> ReferenceEquals(provider.GetRequiredService<AwaitenRootProviderProbe>().RootProvider, provider);

	/// <summary>
	///     Wires the container root's external resolver to the root provider on first use. The root resolves
	///     singletons (and root-provider transients), so their external dependencies stay valid for the app's
	///     lifetime; scoped external resolution is wired per scope on the aligned Awaiten scope (see the scope
	///     factory above). A resolver a caller wired explicitly is left untouched. Guarded by a double-checked lock
	///     on the internal probe singleton so concurrent first resolutions wire it exactly once.
	/// </summary>
	private static void EnsureExternalWired<TRoot>(IServiceProvider provider, TRoot root, bool hasExternal)
		where TRoot : class, IAwaitenRoot
	{
		if (!hasExternal || root.ExternalResolver is not null)
		{
			return;
		}

		AwaitenRootProviderProbe probe = provider.GetRequiredService<AwaitenRootProviderProbe>();
		lock (probe)
		{
			root.ExternalResolver ??= new ServiceProviderExternalResolver(probe.RootProvider);
		}
	}

	private static ServiceLifetime ToServiceLifetime(Lifetime lifetime) => lifetime switch
	{
		Lifetime.Singleton => ServiceLifetime.Singleton,
		Lifetime.Scoped => ServiceLifetime.Scoped,
		_ => ServiceLifetime.Transient,
	};
}
