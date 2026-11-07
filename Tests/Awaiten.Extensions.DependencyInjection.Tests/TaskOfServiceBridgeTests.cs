using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed partial class TaskOfServiceBridgeTests
{
	public interface IStore
	{
		bool Initialized { get; }
	}

	public sealed class Store : IStore
	{
		public bool Initialized { get; set; }
	}

	public sealed class Consumer(Task<IStore> task, Func<Task<IStore>> factory)
	{
		public Task<IStore> Task => task;

		public Func<Task<IStore>> Factory => factory;
	}

	[Container]
	[Scoped<Store, IStore>(OnActivated = nameof(Initialize))]
	[Transient<Consumer>]
	public static partial class Shop
	{
		private static async Task Initialize(Store store, CancellationToken cancellationToken)
		{
			await Task.Yield();
			store.Initialized = true;
		}
	}

	[Test]
	public async Task AddAwaitenContainer_ResolvesAConsumerOfTheServiceTask()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<Shop.Root>();
		await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
		await using AsyncServiceScope scope = provider.CreateAsyncScope();

		Consumer consumer = scope.ServiceProvider.GetRequiredService<Consumer>();
		IStore store = await consumer.Task;

		await That(store.Initialized).IsTrue()
			.Because("the consumer's task adapts the initialized store to the task of its service");
		await That(await consumer.Factory()).IsSameAs(store);
		await That(await scope.ServiceProvider.GetRequiredService<Task<IStore>>()).IsSameAs(store)
			.Because("the host's projection of the service task resolves the same scoped instance");
	}
}
