using System.Threading;
using Awaiten.Tests.ContainerSupport;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     The async relationships <c>Task&lt;IService&gt;</c>, <c>Func&lt;Task&lt;IService&gt;&gt;</c>,
///     <c>Lazy&lt;Task&lt;IService&gt;&gt;</c> and <c>Func&lt;Task&lt;Owned&lt;IService&gt;&gt;&gt;</c> over an
///     implementation registered under a service it implements. <c>Task&lt;T&gt;</c> is not covariant, so the container
///     adapts the implementation's task to the service's task, still awaiting the initialization.
/// </summary>
public sealed partial class TaskOfServiceTests
{
	public interface IInitializable
	{
		Task InitializeAsync(CancellationToken cancellationToken);
	}

	public interface IStore
	{
		bool Initialized { get; }
	}

	public sealed class Store : IStore, IInitializable
	{
		public bool Initialized { get; private set; }

		public async Task InitializeAsync(CancellationToken cancellationToken)
		{
			await Task.Yield();
			Initialized = true;
		}
	}

	public sealed class PlainStore : IStore
	{
		public bool Initialized => true;
	}

	public sealed class Consumer(Task<IStore> task, Func<Task<IStore>> factory, Lazy<Task<IStore>> lazy, Func<Task<Owned<IStore>>> owned)
	{
		public Task<IStore> Task => task;

		public Func<Task<IStore>> Factory => factory;

		public Lazy<Task<IStore>> Lazy => lazy;

		public Func<Task<Owned<IStore>>> Owned => owned;
	}

	public sealed class NullableConsumer(Task<IStore?> task, Func<Task<IStore?>> factory, Lazy<Task<IStore?>> lazy, Func<Task<Owned<IStore?>>> owned)
	{
		public Task<IStore?> Task => task;

		public Func<Task<IStore?>> Factory => factory;

		public Lazy<Task<IStore?>> Lazy => lazy;

		public Func<Task<Owned<IStore?>>> Owned => owned;
	}

	public sealed class SynchronousNullableConsumer(Lazy<IStore?> lazy, Owned<IStore?> owned, Func<Owned<IStore?>> ownedFactory)
	{
		public Lazy<IStore?> Lazy => lazy;

		public Owned<IStore?> Owned => owned;

		public Func<Owned<IStore?>> OwnedFactory => ownedFactory;
	}

	public sealed class KeyedConsumer([FromKey("main")] Task<IStore> task)
	{
		public Task<IStore> Task => task;
	}

	public sealed class TaskStoreConsumer(Task<ITaskStore> task, Func<Task<ITaskStore>> factory)
	{
		public Task<ITaskStore> Task => task;

		public Func<Task<ITaskStore>> Factory => factory;
	}

	public sealed class TerminalConsumer(Task<IAsyncHookedTerminal> task)
	{
		public Task<IAsyncHookedTerminal> Task => task;
	}

	public sealed class FriendTerminalConsumer(Task<IFriendAsyncTerminal> task)
	{
		public Task<IFriendAsyncTerminal> Task => task;
	}

	public sealed class Lifetimes
	{
		[Test]
		public async Task Singleton_HandsOutTheOneInitializedInstance()
		{
			await using SingletonShop.Root shop = new();

			Consumer consumer = shop.Resolve<Consumer>();
			IStore store = await consumer.Task;

			await That(store.Initialized).IsTrue()
				.Because("the adapted task still awaits the async initializer");
			await That(await consumer.Factory()).IsSameAs(store);
			await That(await consumer.Lazy.Value).IsSameAs(store);
			await That(await shop.ResolveAsync<IStore>()).IsSameAs(store)
				.Because("the singleton is shared with the typed async resolution");
		}

		[Test]
		public async Task Scoped_HandsOutOneInitializedInstancePerScope()
		{
			await using ScopedShop.Root shop = new();
			await using ScopedShop.Scope scope = shop.CreateScope();

			Consumer consumer = scope.Resolve<Consumer>();
			IStore store = await consumer.Task;

			await That(store.Initialized).IsTrue();
			await That(await consumer.Factory()).IsSameAs(store);
			await That(await consumer.Lazy.Value).IsSameAs(store);
			await That(await scope.ResolveAsync<IStore>()).IsSameAs(store);
		}

		[Test]
		public async Task Transient_HandsOutANewInitializedInstanceEachTime()
		{
			await using TransientShop.Root shop = new();

			Consumer consumer = shop.Resolve<Consumer>();
			IStore first = await consumer.Factory();
			IStore second = await consumer.Factory();

			await That(first.Initialized).IsTrue();
			await That(second.Initialized).IsTrue();
			await That(second).IsNotSameAs(first)
				.Because("each call of the factory builds a new transient");
			await That((await consumer.Task).Initialized).IsTrue();
			await That((await consumer.Lazy.Value).Initialized).IsTrue();
		}

		[Test]
		[Arguments(nameof(SingletonShop))]
		[Arguments(nameof(ScopedShop))]
		[Arguments(nameof(TransientShop))]
		public async Task OwnedFactory_HandsOutAnInitializedInstance(string container)
		{
			await using IAwaitenRoot shop = container switch
			{
				nameof(SingletonShop) => new SingletonShop.Root(),
				nameof(ScopedShop) => new ScopedShop.Root(),
				_ => new TransientShop.Root(),
			};
			await using IAwaitenScope scope = shop.CreateScope();

			Consumer consumer = scope.Resolve<Consumer>();
			await using Owned<IStore> owned = await consumer.Owned();

			await That(owned.Value.Initialized).IsTrue();
		}

		[Test]
		public async Task SynchronousImplementation_IsWrappedInACompletedTask()
		{
			await using SynchronousShop.Root shop = new();

			Consumer consumer = shop.Resolve<Consumer>();

			await That(consumer.Task.Status).IsEqualTo(TaskStatus.RanToCompletion);
			await That(await consumer.Task).IsSameAs(await consumer.Factory());
		}
	}

	public sealed class NullableAnnotations
	{
		[Test]
		public async Task AsyncRelationships_HandOutTheInitializedInstance()
		{
			await using NullableShop.Root shop = new();

			NullableConsumer consumer = shop.Resolve<NullableConsumer>();
			IStore? store = await consumer.Task;

			await That(store?.Initialized).IsTrue()
				.Because("a nullable-annotated service still resolves the initialized instance");
			await That(await consumer.Factory()).IsSameAs(store);
			await That(await consumer.Lazy.Value).IsSameAs(store);
			await using Owned<IStore?> owned = await consumer.Owned();
			await That(owned.Value).IsSameAs(store)
				.Because("the singleton is shared with the owned scope");
		}

		[Test]
		public async Task SynchronousRelationships_HandOutTheInstance()
		{
			await using SynchronousNullableShop.Root shop = new();

			SynchronousNullableConsumer consumer = shop.Resolve<SynchronousNullableConsumer>();
			await using Owned<IStore?> injected = consumer.Owned;
			await using Owned<IStore?> produced = consumer.OwnedFactory();

			await That(consumer.Lazy.Value).IsNotNull();
			await That(injected.Value).IsSameAs(consumer.Lazy.Value)
				.Because("the singleton is shared with the owned scope");
			await That(produced.Value).IsSameAs(consumer.Lazy.Value);
		}
	}

	public sealed class Registrations
	{
		[Test]
		public async Task Keyed_HandsOutTheInitializedKeyedInstance()
		{
			await using KeyedShop.Root shop = new();

			KeyedConsumer consumer = shop.Resolve<KeyedConsumer>();
			IStore store = await consumer.Task;

			await That(store.Initialized).IsTrue();
			await That(store).IsSameAs(await shop.ResolveAsync<IStore>("main"));
		}

		[Test]
		public async Task AsyncFactory_HandsOutTheProducedInstance()
		{
			await using AsyncFactoryShop.Root shop = new();

			Consumer consumer = shop.Resolve<Consumer>();

			await That(await consumer.Task).IsSameAs(await consumer.Factory())
				.Because("the singleton the async factory produced is shared");
		}

		[Test]
		public async Task SameAssemblyModule_HandsOutTheInitializedInstance()
		{
			await using ModuleShop.Root shop = new();

			Consumer consumer = shop.Resolve<Consumer>();

			await That((await consumer.Task).Initialized).IsTrue();
		}

		[Test]
		public async Task ModuleOfAnotherAssembly_HandsOutTheInitializedInstance()
		{
			await using CrossAssemblyShop.Root shop = new();
			await using CrossAssemblyShop.Scope scope = shop.CreateScope();

			TaskStoreConsumer consumer = scope.Resolve<TaskStoreConsumer>();
			ITaskStore store = await consumer.Task;

			await That(store.Initialized).IsTrue();
			await That(await consumer.Factory()).IsSameAs(store);
		}

		[Test]
		public async Task InternalImplementationOfAnotherAssembly_HandsOutTheActivatedInstance()
		{
			await using HandleShop.Root shop = new();

			TerminalConsumer consumer = shop.Resolve<TerminalConsumer>();
			IAsyncHookedTerminal terminal = await consumer.Task;

			await That(terminal.Token).IsNotNull()
				.Because("the module's async activation hook ran before the task completed");
		}

		[Test]
		public async Task InternalImplementationOfAFriendAssembly_HandsOutTheActivatedInstance()
		{
			await using FriendShop.Root shop = new();

			FriendTerminalConsumer consumer = shop.Resolve<FriendTerminalConsumer>();
			IFriendAsyncTerminal terminal = await consumer.Task;

			await That(terminal.Token).IsNotNull()
				.Because("the container names the internal implementation and adapts its task to the service's task");
		}
	}

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<Store, IStore>]
	[Transient<Consumer>]
	public static partial class SingletonShop;

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Scoped<Store, IStore>]
	[Transient<Consumer>]
	public static partial class ScopedShop;

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Transient<Store, IStore>]
	[Transient<Consumer>]
	public static partial class TransientShop;

	[Container]
	[Singleton<PlainStore, IStore>]
	[Transient<Consumer>]
	public static partial class SynchronousShop;

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<Store, IStore>]
	[Transient<NullableConsumer>]
	public static partial class NullableShop;

	[Container]
	[Singleton<PlainStore, IStore>]
	[Transient<SynchronousNullableConsumer>]
	public static partial class SynchronousNullableShop;

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Singleton<Store, IStore>(Key = "main")]
	[Transient<KeyedConsumer>]
	public static partial class KeyedShop;

	[Container]
	[Singleton<PlainStore, IStore>(Factory = nameof(CreateAsync))]
	[Transient<Consumer>]
	public static partial class AsyncFactoryShop
	{
		private static async Task<PlainStore> CreateAsync()
		{
			await Task.Yield();
			return new PlainStore();
		}
	}

	[Module]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[Scoped<Store, IStore>]
	[Transient<Consumer>]
	public static partial class StoreModule;

	[Container]
	[Import(typeof(StoreModule))]
	public static partial class ModuleShop;

	[Container]
	[Import(typeof(CrossAssemblyTaskOfServiceModule))]
	[Transient<TaskStoreConsumer>]
	public static partial class CrossAssemblyShop;

	[Container]
	[Import(typeof(AsyncHookModule))]
	[Transient<TerminalConsumer>]
	public static partial class HandleShop;

	[Container]
	[Import(typeof(SupportFriendAsyncHookModule))]
	[Transient<FriendTerminalConsumer>]
	public static partial class FriendShop;
}
