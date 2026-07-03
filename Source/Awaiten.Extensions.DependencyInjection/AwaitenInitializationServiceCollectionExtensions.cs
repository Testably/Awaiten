using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
	/// <typeparam name="TContainer">The generated Awaiten container root type.</typeparam>
	public static IServiceCollection AddAwaitenInitialization<TContainer>(this IServiceCollection services)
		where TContainer : class, IAwaitenContainerMetadata
	{
		if (services is null)
		{
			throw new ArgumentNullException(nameof(services));
		}

		services.AddSingleton<IHostedService, AwaitenInitializationHostedService<TContainer>>();
		return services;
	}

	private sealed class AwaitenInitializationHostedService<TContainer> : IHostedService
		where TContainer : class, IAwaitenContainerMetadata
	{
		private readonly TContainer _container;

		public AwaitenInitializationHostedService(TContainer container) => _container = container;

		public Task StartAsync(CancellationToken cancellationToken) => _container.InitializeAsync(cancellationToken);

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}
}
