using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Configures how the generated Awaiten container root <typeparamref name="TRoot" /> is hosted, returned by
///     <see cref="AwaitenServiceCollectionExtensions.AddAwaitenContainer{TRoot}(IServiceCollection)" /> and handed
///     to the configure callback of <see cref="AwaitenServiceProviderFactory{TRoot}" />.
/// </summary>
/// <typeparam name="TRoot">The generated Awaiten container root type.</typeparam>
public sealed class AwaitenContainerBuilder<TRoot>
	where TRoot : class, IAwaitenRoot
{
	internal AwaitenContainerBuilder(IServiceCollection services) => Services = services;

	/// <summary>The service collection the container is registered in, for chaining further registrations.</summary>
	public IServiceCollection Services { get; }

	/// <summary>
	///     Adds an <see cref="IHostedService" /> that calls <c>InitializeAsync</c> on the container root on host
	///     startup, warming its async-initialized singletons in dependency order (see
	///     <see cref="IAsyncInitializable" />) before the host begins serving, and honoring the host's startup
	///     <see cref="CancellationToken" />. A container that hosts async-initialized services through MS.DI should
	///     also opt into <c>AsyncSafety.SyncAfterInit</c> so the warmed services can then be resolved synchronously.
	/// </summary>
	/// <remarks>
	///     The warm-up resolves the container root directly rather than through a bridged registration, which
	///     is where a container's external (<c>[ImportService&lt;T&gt;]</c> / <c>[ImportServices]</c>) dependencies are
	///     normally wired to the host's provider. So when the container has external dependencies and its
	///     <see cref="IAwaitenExternalResolverHost.ExternalResolver" /> was not wired explicitly, the hosted service
	///     wires it to the host's (root) provider before warming, so an async-initialized singleton that draws
	///     on an external service can be constructed during startup. Calling this more than once is a no-op.
	/// </remarks>
	/// <returns>This builder, for chaining.</returns>
	public AwaitenContainerBuilder<TRoot> WithInitialization()
	{
		Services.TryAddEnumerable(
			ServiceDescriptor.Singleton<IHostedService, AwaitenInitializationHostedService>());
		return this;
	}

	private sealed class AwaitenInitializationHostedService : IHostedService
	{
		private readonly TRoot _container;
		private readonly IServiceProvider _provider;

		[SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
			Justification = "Instantiated by the dependency-injection container via reflection.")]
		public AwaitenInitializationHostedService(TRoot container, IServiceProvider provider)
		{
			_container = container;
			_provider = provider;
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			// The bridged registrations wire the external resolver lazily on first resolution, but warming
			// resolves the root itself (a pre-built singleton, not a factory), which bypasses that wiring. The
			// provider injected into this singleton hosted service is the root provider, the right scope for
			// singleton external resolution, so wire it here unless a resolver was set explicitly. The resolver is
			// set-once, so this takes the bridge's wiring lock (the probe) to not race a concurrent first bridged
			// resolution into setting a second one.
			if (_container.ExternalDependencies.Count > 0 && _container.ExternalResolver is null)
			{
				lock (_provider.GetRequiredService<AwaitenRootProviderProbe>())
				{
					if (_container.ExternalResolver is null)
					{
						_container.SetExternalResolver(new AwaitenServiceProviderExternalResolver(_provider));
					}
				}
			}

			// Warming builds singletons on the root, so the root provider must own the root even if nothing is
			// resolved through a bridged registration afterwards.
			_provider.GetRequiredService<AwaitenRootOwner<TRoot>>();
			return _container.InitializeAsync(cancellationToken);
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}
}
