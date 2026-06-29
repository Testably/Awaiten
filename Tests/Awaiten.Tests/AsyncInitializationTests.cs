using System.Linq;
using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of async initialization: <see cref="IAsyncInitializable" /> services are
///     constructed and initialized through <c>ResolveAsync</c> / <c>InitializeAsync</c> /
///     <c>CreateScopeAsync</c>, exactly once, in dependency order, and thread-safely. In the strict
///     default an async-tainted service is reachable only asynchronously; <c>SyncResolveAfterInit</c>
///     relaxes that. The containers and services are nested types, so the enclosing class is
///     <c>partial</c>.
/// </summary>
public partial class AsyncInitializationTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ResolveAsync_ConstructsAndInitializesTheService()
	{
		using AsyncContainer.Root container = new();

		Connection connection = await container.ResolveAsync<Connection>(Ct);

		await That(connection.Initialized).IsTrue();
	}

	[Fact]
	public async Task ResolveAsync_InitializesASingletonExactlyOnceAndReturnsTheSameInstance()
	{
		using AsyncContainer.Root container = new();

		Connection first = await container.ResolveAsync<Connection>(Ct);
		Connection second = await container.ResolveAsync<Connection>(Ct);

		await That(first).IsSameAs(second);
		await That(first.InitializeCount).IsEqualTo(1);
	}

	[Fact]
	public async Task ResolveAsync_ByType_ResolvesThroughTheNeutralInterface()
	{
		using AsyncContainer.Root container = new();
		IAwaitenResolver neutral = container;

		object connection = await neutral.ResolveAsync(typeof(Connection), Ct);

		await That(connection).Is<Connection>();
	}

	[Fact]
	public async Task ResolveAsync_OfANonAsyncService_CompletesAndReturnsIt()
	{
		using AsyncContainer.Root container = new();

		// A service with no async dependency is resolvable through ResolveAsync too; it completes synchronously.
		Plain plain = await container.ResolveAsync<Plain>(Ct);

		await That(plain).IsNotNull();
	}

	[Fact]
	public async Task ResolveAsync_OfATransient_BuildsAndInitializesAFreshInstanceEachCall()
	{
		using AsyncContainer.Root container = new();

		Worker first = await container.ResolveAsync<Worker>(Ct);
		Worker second = await container.ResolveAsync<Worker>(Ct);

		await That(first).IsNotSameAs(second);
		await That(first.Initialized).IsTrue();
		await That(second.Initialized).IsTrue();
	}

	[Fact]
	public async Task InitializeAsync_WarmsEverySingletonInDependencyOrder()
	{
		using OrderedContainer.Root container = new();

		await container.InitializeAsync(Ct);

		Repository repo = await container.ResolveAsync<Repository>(Ct);
		Database db = await container.ResolveAsync<Database>(Ct);

		// The dependency was initialized before the dependent's own initialization ran.
		await That(db.Ready).IsTrue();
		await That(repo.DatabaseWasReadyAtInit).IsTrue();
	}

	[Fact]
	public async Task InitializeAsync_IsIdempotent()
	{
		using OrderedContainer.Root container = new();

		await container.InitializeAsync(Ct);
		await container.InitializeAsync(Ct);

		Database db = await container.ResolveAsync<Database>(Ct);
		await That(db.InitializeCount).IsEqualTo(1);
	}

	[Fact]
	public async Task ResolveAsync_AwaitsDependencyInitializationBeforeConstructingTheDependent()
	{
		using OrderedContainer.Root container = new();

		Repository repo = await container.ResolveAsync<Repository>(Ct);

		await That(repo.DatabaseWasReadyAtInit).IsTrue();
	}

	[Fact]
	public async Task CreateScopeAsync_WarmsScopedAsyncServices()
	{
		using AsyncContainer.Root container = new();
		using IAwaitenScope scope = await container.CreateScopeAsync(Ct);

		// CreateScopeAsync already constructed and initialized the scoped service; ResolveAsync returns that
		// same warmed instance (memoized per scope).
		ScopedConnection connection = await scope.ResolveAsync<ScopedConnection>(Ct);

		await That(connection.Initialized).IsTrue();
	}

	[Fact]
	public async Task CreateScopeAsync_GivesOneScopedInstancePerScope()
	{
		using AsyncContainer.Root container = new();
		using IAwaitenScope scope1 = await container.CreateScopeAsync(Ct);
		using IAwaitenScope scope2 = await container.CreateScopeAsync(Ct);

		ScopedConnection a = await scope1.ResolveAsync<ScopedConnection>(Ct);
		ScopedConnection b = await scope2.ResolveAsync<ScopedConnection>(Ct);

		await That(ReferenceEquals(a, b)).IsFalse();
	}

	[Fact]
	public async Task ResolveAsync_IsThreadSafe_InitializesOnceUnderConcurrency()
	{
		using AsyncContainer.Root container = new();

		Task<Connection>[] tasks = Enumerable.Range(0, 64)
			.Select(_ => Task.Run(() => container.ResolveAsync<Connection>(Ct)))
			.ToArray();
		Connection[] resolved = await Task.WhenAll(tasks);

		await That(resolved.Distinct().Count()).IsEqualTo(1);
		await That(resolved[0].InitializeCount).IsEqualTo(1);
	}

	[Fact]
	public async Task SyncResolve_OfAnAsyncTaintedService_IsNotExposedThroughSyncResolution()
	{
		using AsyncContainer.Root container = new();

		// In the strict default an async-tainted service is reachable only through ResolveAsync, so the
		// synchronous dispatch reports it as unregistered rather than handing back an uninitialized one.
		await That(container.TryResolve(typeof(Connection), out _)).IsFalse();
		await That(() => container.Resolve<Connection>()).Throws<InvalidOperationException>();
	}

	[Fact]
	public async Task PragmaticMode_AllowsSyncResolutionOfAWarmedSingleton()
	{
		using PragmaticContainer.Root container = new();

		await container.InitializeAsync(Ct);

		// SyncResolveAfterInit = true: once warmed, the singleton is also resolvable synchronously and
		// returns the very instance that ResolveAsync initialized.
		Connection sync = container.Resolve<Connection>();
		Connection async = await container.ResolveAsync<Connection>(Ct);

		await That(sync).IsSameAs(async);
		await That(sync.Initialized).IsTrue();
	}

	[Fact]
	public async Task TransitivelyTaintedService_IsInitializedThroughItsAsyncDependency()
	{
		using OrderedContainer.Root container = new();

		// Consumer is not itself IAsyncInitializable, but it depends on Repository (which is). Resolving it
		// asynchronously initializes the whole chain; resolving it synchronously is withheld in strict mode.
		Consumer consumer = await container.ResolveAsync<Consumer>(Ct);

		await That(consumer.Repository.DatabaseWasReadyAtInit).IsTrue();
		await That(container.TryResolve(typeof(Consumer), out _)).IsFalse();
	}

	public sealed class Connection : IAsyncInitializable
	{
		public bool Initialized { get; private set; }

		public int InitializeCount { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			InitializeCount++;
			return Task.CompletedTask;
		}
	}

	public sealed class ScopedConnection : IAsyncInitializable
	{
		public bool Initialized { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}
	}

	public sealed class Worker : IAsyncInitializable
	{
		public bool Initialized { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}
	}

	public sealed class Plain;

	[Container]
	[Singleton<Connection>]
	[Scoped<ScopedConnection>]
	[Transient<Worker>]
	[Singleton<Plain>]
	public static partial class AsyncContainer;

	[Container(SyncResolveAfterInit = true)]
	[Singleton<Connection>]
	public static partial class PragmaticContainer;

	public sealed class Database : IAsyncInitializable
	{
		public bool Ready { get; private set; }

		public int InitializeCount { get; private set; }

		public async Task InitializeAsync(CancellationToken cancellationToken)
		{
			// Yield so initialization genuinely runs asynchronously before the dependent observes it.
			await Task.Yield();
			Ready = true;
			InitializeCount++;
		}
	}

	public sealed class Repository : IAsyncInitializable
	{
		private readonly Database _database;

		public Repository(Database database) => _database = database;

		public bool DatabaseWasReadyAtInit { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			DatabaseWasReadyAtInit = _database.Ready;
			return Task.CompletedTask;
		}
	}

	// Not itself async-initializable, but async-tainted through its Repository dependency.
	public sealed class Consumer
	{
		public Consumer(Repository repository) => Repository = repository;

		public Repository Repository { get; }
	}

	[Container]
	[Singleton<Database>]
	[Singleton<Repository>]
	[Singleton<Consumer>]
	public static partial class OrderedContainer;
}
