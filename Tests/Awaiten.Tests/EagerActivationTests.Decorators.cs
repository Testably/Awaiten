using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Awaiten.Tests;

public partial class EagerActivationTests
{
	[NotInParallel(nameof(EagerActivationTests))]
	public partial class Decorated
	{
		[Test]
		public async Task DecoratedEagerSingleton_IsConstructedWithItsDecoratorsBeforeAnyResolve()
		{
			Probe.Constructed.Clear();

			using DecoratedContainer.Root container = new();

			await That(Probe.Constructed).IsEqualTo(["Client", "MonitoringClient",])
				.Because("eager activation builds the service as consumers see it, including its decorator");
			IClient resolved = container.Resolve<IClient>();
			await That(resolved).Is<MonitoringClient>();
			await That(container.Resolve<IClient>()).IsSameAs(resolved);
			await That(Probe.Constructed).IsEqualTo(["Client", "MonitoringClient",])
				.Because("the eagerly built chain is cached, so a resolve neither rebuilds the inner service nor the decorator");
		}

		[Test]
		public async Task DecoratedEagerSingleton_IsConstructedInRegistrationOrder()
		{
			Probe.Constructed.Clear();

			using DecoratedOrderContainer.Root container = new();

			await That(Probe.Constructed).IsEqualTo(["Client", "MonitoringClient", "Second",])
				.Because("the decorated eager singleton is declared first, so its whole chain is built before the next eager singleton");
		}

		[Test]
		public async Task DecoratedEagerSingleton_DisposableDecorator_IsDisposedWithTheContainerWithoutAResolve()
		{
			Probe.Constructed.Clear();
			Probe.Disposed.Clear();

			using (DecoratedContainer.Root container = new())
			{
				await That(Probe.Disposed).IsEmpty();
			}

			await That(Probe.Disposed).IsEqualTo(["MonitoringClient",])
				.Because("eager construction alone tracks the decorator for disposal");
		}

		[Test]
		public async Task KeyedDecoratedEagerSingleton_IsConstructedWithItsDecoratorsBeforeAnyResolve()
		{
			Probe.Constructed.Clear();

			using KeyedDecoratedContainer.Root container = new();

			await That(Probe.Constructed).IsEqualTo(["Client", "MonitoringClient",])
				.Because("the decorator of the keyed registration is part of the eagerly built service");
			IClient resolved = container.Resolve<IClient>("primary");
			await That(resolved).Is<MonitoringClient>();
			await That(Probe.Constructed.Count).IsEqualTo(2)
				.Because("the keyed resolve returns the eagerly built chain");
		}

		[Test]
		public async Task DecoratedEagerCollectionMembers_AreEachConstructedWithTheirDecorators()
		{
			Probe.Constructed.Clear();

			using DecoratedMembersContainer.Root container = new();

			await That(Probe.Constructed).IsEqualTo(["Client", "MonitoringClient", "BackupClient", "MonitoringClient",])
				.Because("each decorated collection member gets its own chain, and each eager member builds it");
			IClient[] clients = container.Resolve<IClient[]>();
			await That(clients.All(client => client is MonitoringClient)).IsTrue();
			await That(Probe.Constructed.Count).IsEqualTo(4)
				.Because("the collection hands back the eagerly built chains");
		}

		[Test]
		public async Task DecoratedEagerMember_BehindAComposite_IsConstructedWithItsDecorators()
		{
			Probe.Constructed.Clear();

			using DecoratedCompositeContainer.Root container = new();

			await That(Probe.Constructed).IsEqualTo(["Client", "MonitoringClient",])
				.Because("the eager member is built with its decorator, while the composite keeps its own lazy lifetime");
			IClient resolved = container.Resolve<IClient>();
			await That(resolved).Is<CompositeClient>();
			await That(Probe.Constructed).IsEqualTo(["Client", "MonitoringClient", "CompositeClient",])
				.Because("the composite fans out over the eagerly built chain instead of rebuilding it");
		}

		[Test]
		public async Task PragmaticMode_AsyncInitializedDecorator_IsInitializedBeforeAnyResolve()
		{
			Probe.Constructed.Clear();

			using AsyncDecoratedContainer.Root container = new();

			await That(Probe.Constructed).IsEqualTo(["Client", "AsyncMonitoringClient", "AsyncMonitoringClient:init",])
				.Because("eager activation initializes the async decorator the same way it initializes an async eager service");
			IClient resolved = container.Resolve<IClient>();
			await That(resolved).Is<AsyncMonitoringClient>();
			await That(((AsyncMonitoringClient)resolved).Initialized).IsTrue();
			await That(Probe.Constructed.Count).IsEqualTo(3)
				.Because("the async decorator is constructed and initialized once");
		}

		[Test]
		public async Task DecoratedEagerInstance_IsWrappedByItsDecoratorsBeforeAnyResolve()
		{
			Probe.Constructed.Clear();

			using DecoratedInstanceContainer.Root container = new();

			await That(Probe.Constructed).IsEqualTo(["MonitoringClient",])
				.Because("eager activation of a pre-built instance builds the decorators that wrap it");
			IClient resolved = container.Resolve<IClient>();
			await That(((MonitoringClient)resolved).Inner).IsSameAs(DecoratedInstanceContainer.Shared);
			await That(Probe.Constructed.Count).IsEqualTo(1)
				.Because("the resolve returns the eagerly built decorator");
		}

		public interface IClient;

		public sealed class Client : IClient
		{
			public Client() => Probe.Constructed.Add("Client");
		}

		public sealed class SilentClient : IClient;

		public sealed class BackupClient : IClient
		{
			public BackupClient() => Probe.Constructed.Add("BackupClient");
		}

		public sealed class MonitoringClient : IClient, IDisposable
		{
			public MonitoringClient(IClient inner)
			{
				Inner = inner;
				Probe.Constructed.Add("MonitoringClient");
			}

			public IClient Inner { get; }

			public void Dispose() => Probe.Disposed.Add("MonitoringClient");
		}

		public sealed class AsyncMonitoringClient : IClient, IAsyncInitializable
		{
			public AsyncMonitoringClient(IClient inner)
			{
				Inner = inner;
				Probe.Constructed.Add("AsyncMonitoringClient");
			}

			public IClient Inner { get; }

			public bool Initialized { get; private set; }

			public Task InitializeAsync(CancellationToken cancellationToken)
			{
				Initialized = true;
				Probe.Constructed.Add("AsyncMonitoringClient:init");
				return Task.CompletedTask;
			}
		}

		public sealed class CompositeClient : IClient
		{
			public CompositeClient(IEnumerable<IClient> clients)
			{
				Clients = clients.ToArray();
				Probe.Constructed.Add("CompositeClient");
			}

			public IClient[] Clients { get; }
		}

		[Container]
		[Singleton<Client, IClient>(Eager = true)]
		[Decorate<MonitoringClient, IClient>]
		public static partial class DecoratedContainer;

		[Container]
		[Singleton<Client, IClient>(Eager = true)]
		[Singleton<Second>(Eager = true)]
		[Decorate<MonitoringClient, IClient>]
		public static partial class DecoratedOrderContainer;

		[Container]
		[Singleton<Client, IClient>(Key = "primary", Eager = true)]
		[Decorate<MonitoringClient, IClient>(Keys = new object?[] { "primary", })]
		public static partial class KeyedDecoratedContainer;

		[Container]
		[Singleton<Client, IClient>(Eager = true)]
		[Singleton<BackupClient, IClient>(Eager = true)]
		[Decorate<MonitoringClient, IClient>]
		public static partial class DecoratedMembersContainer;

		[Container]
		[Singleton<Client, IClient>(Eager = true)]
		[Decorate<MonitoringClient, IClient>]
		[Composite<CompositeClient, IClient>(Lifetime = Lifetime.Singleton)]
		public static partial class DecoratedCompositeContainer;

		[Container]
		[Singleton<IClient>(Instance = nameof(Shared), Eager = true)]
		[Decorate<MonitoringClient, IClient>]
		public static partial class DecoratedInstanceContainer
		{
			internal static readonly IClient Shared = new SilentClient();
		}

		[Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
		[Singleton<Client, IClient>(Eager = true)]
		[Decorate<AsyncMonitoringClient, IClient>]
		public static partial class AsyncDecoratedContainer;
	}
}
