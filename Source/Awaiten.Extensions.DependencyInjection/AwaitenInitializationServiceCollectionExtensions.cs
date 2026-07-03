using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Registers a hosted service that initializes a generated Awaiten container on application startup,
///     warming its async-initialized singletons in dependency order (see <see cref="IAsyncInitializable" />)
///     before the host begins serving.
/// </summary>
public static class AwaitenInitializationServiceCollectionExtensions
{
	/// <summary>
	///     Adds an <see cref="IHostedService" /> that calls <c>InitializeAsync</c> on the
	///     <typeparamref name="TContainer" /> registered with
	///     <see cref="AwaitenServiceCollectionExtensions.AddGeneratedContainer{TRoot}(IServiceCollection)" />,
	///     honoring the host's startup <see cref="CancellationToken" />. Register the container first; a
	///     container that hosts async-initialized services through MS.DI should also opt into
	///     <c>SyncResolveAfterInit</c> so the warmed services can then be resolved synchronously.
	/// </summary>
	/// <remarks>
	///     The warm-up resolves the container root directly rather than through a bridged registration, which
	///     is where a container's external (<c>[FromServices]</c> / <c>[ImportServices]</c>) dependencies are
	///     normally wired to the host's provider. So when the container has external dependencies and its
	///     <see cref="IExternalResolverHost.ExternalResolver" /> was not wired explicitly, the hosted service
	///     wires it to the host's (root) provider before warming, so an async-initialized singleton that draws
	///     on an external service can be constructed during startup. Registering the same container's
	///     initialization more than once is a no-op.
	/// </remarks>
	/// <typeparam name="TContainer">The generated Awaiten container root type.</typeparam>
	public static IServiceCollection AddAwaitenInitialization<TContainer>(this IServiceCollection services)
		where TContainer : class, IAwaitenContainerMetadata
	{
		if (services is null)
		{
			throw new ArgumentNullException(nameof(services));
		}

		services.TryAddEnumerable(
			ServiceDescriptor.Singleton<IHostedService, AwaitenInitializationHostedService<TContainer>>());
		return services;
	}

	private sealed class AwaitenInitializationHostedService<TContainer> : IHostedService
		where TContainer : class, IAwaitenContainerMetadata
	{
		private readonly TContainer _container;
		private readonly IServiceProvider _provider;

		[SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
			Justification = "Instantiated by the dependency-injection container via reflection.")]
		public AwaitenInitializationHostedService(TContainer container, IServiceProvider provider)
		{
			_container = container;
			_provider = provider;
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			// The bridged registrations wire the external resolver lazily on first resolution, but warming
			// resolves the root itself (a pre-built singleton, not a factory), which bypasses that wiring.
			// The provider injected into this singleton hosted service is the root provider - the right scope
			// for singleton external resolution - so wire it here unless a resolver was set explicitly.
			if (_container.ExternalDependencies.Count > 0 && _container.ExternalResolver is null)
			{
				_container.ExternalResolver = new ServiceProviderExternalResolver(_provider);
			}

			return _container.InitializeAsync(cancellationToken);
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}
}
