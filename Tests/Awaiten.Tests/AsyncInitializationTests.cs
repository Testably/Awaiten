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
		// The neutral interface is the subject under test: resolve through it (not the concrete Root) to pin
		// that the by-type async surface is reachable from the dependency-free seam, so CA1859's "use the
		// concrete type for performance" nudge does not apply here.
#pragma warning disable CA1859
		IAwaitenAsyncResolver neutral = container;
#pragma warning restore CA1859

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

	[Fact]
	public async Task SyncResolve_OfAnAsyncTaintedService_ThrowsGuidancePointingAtResolveAsync()
	{
		using AsyncContainer.Root container = new();

		// The service IS registered; resolving it synchronously surfaces guidance toward ResolveAsync rather
		// than the generic "no registration" message (which would wrongly suggest it was never registered).
		// Resolve<T> over an async-tainted service falls through to the by-type Resolve, so it surfaces the
		// same guidance.
		await That(() => container.Resolve<Connection>()).Throws<InvalidOperationException>()
			.WithMessage("*ResolveAsync*").AsWildcard()
			.Because("an async-tainted service is registered but reachable only asynchronously");
	}

	[Fact]
	public async Task CreateScopeAsync_DisposesTheScope_WhenScopedInitializationThrows()
	{
		using FailingScopeContainer.Root container = new();
		FailingScoped.Reset();

		Func<Task> createScope = () => container.CreateScopeAsync(Ct);
		await That(createScope).Throws<InvalidOperationException>();

		// The scoped instance was constructed (and tracked for disposal) before its InitializeAsync threw; the
		// failed CreateScopeAsync disposed the scope rather than leaking it, so that instance was disposed.
		await That(FailingScoped.Disposed).IsTrue();
	}

	[Fact]
	public async Task PragmaticMode_SyncResolveBeforeWarmUp_StillReturnsAnInitializedSingleton()
	{
		using PragmaticContainer.Root container = new();

		// No InitializeAsync first: the synchronous resolver delegates to the (memoizing) async path, so it
		// returns a fully initialized instance built exactly once - never a second, uninitialized one.
		Connection first = container.Resolve<Connection>();

		await That(first.Initialized).IsTrue();
		await That(first.InitializeCount).IsEqualTo(1);
		await That(container.Resolve<Connection>()).IsSameAs(first);
	}

	[Fact]
	public async Task PragmaticMode_TryResolveOfAnAsyncTaintedService_ReturnsTrueWithAnInitializedInstance()
	{
		using PragmaticContainer.Root container = new();

		// The complement of the strict default (where TryResolve reports an async-tainted service as false):
		// pragmatic mode exposes it to synchronous resolution, so TryResolve reports it and hands back a fully
		// initialized instance.
		bool resolved = container.TryResolve(typeof(Connection), out object? instance);

		await That(resolved).IsTrue();
		await That(instance).Is<Connection>();
		await That(((Connection)instance!).Initialized).IsTrue();
	}

	[Fact]
	public async Task PragmaticMode_SyncResolveOfATransient_ReturnsAnInitializedInstance()
	{
		using PragmaticContainer.Root container = new();

		// A transient is never warmed by InitializeAsync, so the synchronous resolver must still initialize it.
		Worker worker = container.Resolve<Worker>();

		await That(worker.Initialized).IsTrue();
	}

	[Fact]
	public async Task PragmaticMode_ConcurrentSyncAndAsyncResolution_ConstructsOneInitializedSingleton()
	{
		using PragmaticContainer.Root container = new();

		Task<Connection>[] asyncTasks = Enumerable.Range(0, 32)
			.Select(_ => Task.Run(() => container.ResolveAsync<Connection>(Ct))).ToArray();
		Task<Connection>[] syncTasks = Enumerable.Range(0, 32)
			.Select(_ => Task.Run(() => container.Resolve<Connection>())).ToArray();

		Connection[] resolved = (await Task.WhenAll(asyncTasks)).Concat(await Task.WhenAll(syncTasks)).ToArray();

		// The synchronous and asynchronous paths share one construction-and-initialization, so even under
		// concurrency there is a single instance initialized exactly once.
		await That(resolved.Distinct().Count()).IsEqualTo(1);
		await That(resolved[0].InitializeCount).IsEqualTo(1);
	}

	[Fact]
	public async Task ResolveAsync_AfterAFailedInitialization_RetriesRatherThanCachingTheFailure()
	{
		using FlakyContainer.Root container = new();
		FlakyConnection.Reset();

		Func<Task> resolveFlaky = () => container.ResolveAsync<FlakyConnection>(Ct);
		await That(resolveFlaky).Throws<InvalidOperationException>();

		// The faulted task is not memoized, so a second resolve builds and initializes a fresh instance.
		FlakyConnection second = await container.ResolveAsync<FlakyConnection>(Ct);

		await That(second.Initialized).IsTrue();
		await That(FlakyConnection.Attempts).IsEqualTo(2);
	}

	[Fact]
	public async Task ResolveAsync_AfterACanceledInitialization_RetriesWithAFreshToken()
	{
		using SlowContainer.Root container = new();
		SlowConnection.Reset();
		using CancellationTokenSource cts = new();

		Task<SlowConnection> first = container.ResolveAsync<SlowConnection>(cts.Token);
		cts.Cancel();
		Func<Task> awaitFirst = () => first;
		await That(awaitFirst).Throws<OperationCanceledException>();

		// One caller's cancellation does not poison the shared singleton: a later resolve with a live token
		// initializes successfully.
		SlowConnection second = await container.ResolveAsync<SlowConnection>(Ct);

		await That(second.Initialized).IsTrue();
	}

	[Fact]
	public async Task Dispose_DisposesAnAsyncInitializedSingleton()
	{
		DisposableConnection.Reset();

		using (DisposableAsyncContainer.Root container = new())
		{
			DisposableConnection resolved = await container.ResolveAsync<DisposableConnection>(Ct);
			await That(resolved.Initialized).IsTrue();
		}

		await That(DisposableConnection.DisposeCount).IsEqualTo(1);
	}

	[Fact]
	public async Task ResolveAsync_OfAFactoryHidingAnAsyncDisposable_InitializesAndDisposesItExactlyOnce()
	{
		// The factory is declared to return a non-async, non-disposable interface yet builds a concrete type
		// that is both IAsyncInitializable and IDisposable. The async-taint follows the concrete return type
		// (so the container drives InitializeAsync), and disposal is tracked by the generated runtime
		// `is IDisposable` check on the realized instance - exercising the async creator path that the
		// synchronous hidden-disposable tests do not reach.
		AsyncHiddenDisposable hidden;
		using (AsyncHiddenDisposableContainer.Root container = new())
		{
			hidden = (AsyncHiddenDisposable)await container.ResolveAsync<IAsyncHidden>(Ct);
			await That(hidden.Initialized).IsTrue()
				.Because("the factory's concrete return type is IAsyncInitializable, so the container drives its initialization");
			await That(hidden.DisposeCount).IsEqualTo(0)
				.Because("the container still owns the instance");
		}

		await That(hidden.DisposeCount).IsEqualTo(1)
			.Because("a hidden async-disposable singleton is disposed exactly once on container teardown (not double-tracked by the async registration's runtime check)");
	}

	[Fact]
	public async Task ResolveAsync_OfADisposableAsyncTransient_IsWithheldOnTheRoot()
	{
		using DisposableWorkerContainer.Root container = new();

		// Mirrors the synchronous strict withholding: resolving a disposable transient by type on the Root would
		// track a fresh disposable on the root for the container's lifetime, so ResolveAsync refuses it there and
		// steers toward a child scope (whose disposal bounds the instance) rather than leaking.
		Func<Task> resolveOnRoot = () => container.ResolveAsync<DisposableWorker>(Ct);
		await That(resolveOnRoot).Throws<InvalidOperationException>()
			.WithMessage("*child scope*").AsWildcard()
			.Because("a disposable async transient accumulates on the root and is withheld from ResolveAsync there");
	}

	[Fact]
	public async Task ResolveAsync_OfADisposableAsyncTransient_ResolvesFromAChildScopeAndIsDisposedWithIt()
	{
		using DisposableWorkerContainer.Root container = new();

		DisposableWorker worker;
		using (IAwaitenScope scope = await container.CreateScopeAsync(Ct))
		{
			// From a child scope it resolves normally - the instance it builds is owned by the scope, so its
			// lifetime is bounded rather than accumulating on the root.
			worker = await scope.ResolveAsync<DisposableWorker>(Ct);
			await That(worker.Initialized).IsTrue();
			await That(worker.Disposed).IsFalse();
		}

		await That(worker.Disposed).IsTrue();
	}

	[Fact]
	public async Task DisposedContainer_AsyncEntryPoints_ThrowSynchronouslyRatherThanReturningAFaultedTask()
	{
		AsyncContainer.Root container = new();
		container.Dispose();

		// The disposed-guard is eager state validation: the async entry points throw on the call itself (an
		// Action that invokes and discards the task still throws), not only when the returned task is awaited.
		await That(() => { _ = container.InitializeAsync(Ct); }).Throws<ObjectDisposedException>();
		await That(() => { _ = container.CreateScopeAsync(Ct); }).Throws<ObjectDisposedException>();
		await That(() => { _ = container.ResolveAsync<Connection>(Ct); }).Throws<ObjectDisposedException>();
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
	[Transient<Worker>]
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

	// A scoped service whose initialization throws, so CreateScopeAsync must dispose the half-built scope.
	public sealed class FailingScoped : IAsyncInitializable, IDisposable
	{
		public static bool Disposed { get; private set; }

		public static void Reset() => Disposed = false;

		public Task InitializeAsync(CancellationToken cancellationToken)
			=> throw new InvalidOperationException("scoped initialization failed");

		public void Dispose() => Disposed = true;
	}

	[Container]
	[Scoped<FailingScoped>]
	public static partial class FailingScopeContainer;

	// Fails its first initialization, then succeeds - to prove a faulted task is not memoized.
	public sealed class FlakyConnection : IAsyncInitializable
	{
		public static int Attempts { get; private set; }

		public bool Initialized { get; private set; }

		public static void Reset() => Attempts = 0;

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Attempts++;
			if (Attempts == 1)
			{
				throw new InvalidOperationException("transient initialization failure");
			}

			Initialized = true;
			return Task.CompletedTask;
		}
	}

	[Container]
	[Singleton<FlakyConnection>]
	public static partial class FlakyContainer;

	// Blocks (honoring cancellation) on its first initialization, then succeeds - to prove a canceled task is
	// not memoized and a later caller with a live token can still initialize it.
	public sealed class SlowConnection : IAsyncInitializable
	{
		public static int Attempts { get; private set; }

		public bool Initialized { get; private set; }

		public static void Reset() => Attempts = 0;

		public async Task InitializeAsync(CancellationToken cancellationToken)
		{
			Attempts++;
			if (Attempts == 1)
			{
				await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
			}

			Initialized = true;
		}
	}

	[Container]
	[Singleton<SlowConnection>]
	public static partial class SlowContainer;

	public sealed class DisposableConnection : IAsyncInitializable, IDisposable
	{
		public static int DisposeCount { get; private set; }

		public bool Initialized { get; private set; }

		public static void Reset() => DisposeCount = 0;

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}

		public void Dispose() => DisposeCount++;
	}

	[Container]
	[Singleton<DisposableConnection>]
	public static partial class DisposableAsyncContainer;

	// An async-but-not-disposable service interface: the factory's declared return type, so async-taint is
	// statically visible (it extends IAsyncInitializable) while the concrete IDisposable stays hidden behind
	// it - exactly the split that drives the async creator yet needs the runtime disposal check.
	public interface IAsyncHidden : IAsyncInitializable;

	// A concrete type that is both async-initialized (through IAsyncHidden) and IDisposable. A factory
	// declared to return IAsyncHidden still both drives its async initialization and disposes it -
	// DisposeCount (not a bool) proves disposal happens exactly once.
	public sealed class AsyncHiddenDisposable : IAsyncHidden, IDisposable
	{
		public bool Initialized { get; private set; }

		public int DisposeCount { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}

		public void Dispose() => DisposeCount++;
	}

	[Container]
	[Singleton<IAsyncHidden>(Factory = nameof(MakeAsyncHidden))]
	public static partial class AsyncHiddenDisposableContainer
	{
#pragma warning disable CA1859
		private static IAsyncHidden MakeAsyncHidden() => new AsyncHiddenDisposable();
#pragma warning restore CA1859
	}

	// A disposable async transient: withheld from ResolveAsync on the Root (it would accumulate there), but
	// resolvable from a child scope, which bounds and disposes it.
	public sealed class DisposableWorker : IAsyncInitializable, IDisposable
	{
		public bool Initialized { get; private set; }

		public bool Disposed { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}

		public void Dispose() => Disposed = true;
	}

	[Container]
	[Transient<DisposableWorker>]
	public static partial class DisposableWorkerContainer;

	[Fact]
	public async Task AsyncFactory_Singleton_AwaitsTheFactoryAndReturnsTheBuiltInstance()
	{
		AsyncFactoryService.Reset();
		using AsyncFactoryContainer.Root container = new();

		IAsyncFactoryService service = await container.ResolveAsync<IAsyncFactoryService>(Ct);

		await That(service).IsNotNull();
		await That(AsyncFactoryService.BuildCount).IsEqualTo(1);
	}

	[Fact]
	public async Task AsyncFactory_Singleton_IsBuiltExactlyOnceAndReturnsTheSameInstance()
	{
		AsyncFactoryService.Reset();
		using AsyncFactoryContainer.Root container = new();

		IAsyncFactoryService first = await container.ResolveAsync<IAsyncFactoryService>(Ct);
		IAsyncFactoryService second = await container.ResolveAsync<IAsyncFactoryService>(Ct);

		await That(first).IsSameAs(second);
		await That(AsyncFactoryService.BuildCount).IsEqualTo(1);
	}

	[Fact]
	public async Task AsyncFactory_Singleton_IsWithheldFromSynchronousResolution()
	{
		AsyncFactoryService.Reset();
		using AsyncFactoryContainer.Root container = new();

		await That(container.TryResolve(typeof(IAsyncFactoryService), out _)).IsFalse()
			.Because("an async-factory service is async-tainted: in the strict default the synchronous path cannot unwrap the Task, so it is not exposed there (it would otherwise hand back an unawaited instance)");
		await That(() => container.Resolve<IAsyncFactoryService>()).Throws<InvalidOperationException>()
			.Because("synchronous by-type resolution of an async-factory service is withheld in the strict default");
	}

	[Fact]
	public async Task AsyncFactory_Singleton_WarmedByInitializeAsync_IsSyncResolvableUnderSyncResolveAfterInit()
	{
		PragmaticAsyncFactoryService.Reset();
		using PragmaticAsyncFactoryContainer.Root container = new();

		await container.InitializeAsync(Ct);

		PragmaticAsyncFactoryService sync = container.Resolve<PragmaticAsyncFactoryService>();
		PragmaticAsyncFactoryService async = await container.ResolveAsync<PragmaticAsyncFactoryService>(Ct);

		await That(sync).IsSameAs(async)
			.Because("SyncResolveAfterInit = true: once warmed, the async-factory singleton is also resolvable synchronously and returns the very instance ResolveAsync built");
		await That(PragmaticAsyncFactoryService.BuildCount).IsEqualTo(1)
			.Because("warm-up and the two resolutions share the single memoized instance, so the factory ran once");
	}

	[Fact]
	public async Task AsyncFactory_Transient_BuildsAFreshInstanceEachCall_AndIsAsyncOnly()
	{
		using TransientAsyncFactoryContainer.Root container = new();

		TransientAsyncFactoryService first = await container.ResolveAsync<TransientAsyncFactoryService>(Ct);
		TransientAsyncFactoryService second = await container.ResolveAsync<TransientAsyncFactoryService>(Ct);

		await That(first).IsNotSameAs(second)
			.Because("an async-factory transient builds a fresh instance on each ResolveAsync");
		await That(container.TryResolve(typeof(TransientAsyncFactoryService), out _)).IsFalse()
			.Because("an async-factory transient is async-only forever (never warmed by InitializeAsync, so the strict default has no synchronous path to it)");
	}

	[Fact]
	public async Task AsyncFactory_WhoseResultIsAsyncInitializable_AwaitsBothTheFactoryAndInitializeAsync()
	{
		using InitializingAsyncFactoryContainer.Root container = new();

		InitializingAsyncFactoryService service = await container.ResolveAsync<InitializingAsyncFactoryService>(Ct);

		await That(service.Initialized).IsTrue()
			.Because("the factory built it asynchronously and the container additionally awaited its InitializeAsync");
		await That(service.InitializeCount).IsEqualTo(1)
			.Because("InitializeAsync runs exactly once after the factory completes - no double-initialization");
	}

	[Fact]
	public async Task AsyncFactory_MayAwaitInternalInitializationItself_WithoutImplementingIAsyncInitializable()
	{
		using SelfInitializingFactoryContainer.Root container = new();

		SelfInitializingService service = await container.ResolveAsync<SelfInitializingService>(Ct);

		await That(service.Initialized).IsTrue()
			.Because("the factory body did its own async work; the container did not call InitializeAsync (the type is not IAsyncInitializable), yet the instance is fully initialized");
	}

	[Fact]
	public async Task AsyncFactory_DisposableHiddenBehindTheServiceInterface_IsDisposedOnContainerTeardown()
	{
		HiddenDisposableFactoryService hidden;
		using (HiddenDisposableAsyncFactoryContainer.Root container = new())
		{
			hidden = (HiddenDisposableFactoryService)await container.ResolveAsync<IHiddenDisposableFactoryService>(Ct);
			await That(hidden.DisposeCount).IsEqualTo(0)
				.Because("the container still owns the instance");
		}

		await That(hidden.DisposeCount).IsEqualTo(1)
			.Because("the disposable produced behind Task<> and a non-disposable interface is tracked by the generated runtime is-IDisposable check on the awaited result, so container teardown disposes it exactly once");
	}

	[Fact]
	public async Task AsyncFactory_WithCancellationTokenParameter_ReceivesTheResolveTimeToken()
	{
		using TokenForwardingContainer.Root container = new();
		using CancellationTokenSource cts = new();

		TokenAwareService service = await container.ResolveAsync<TokenAwareService>(cts.Token);

		await That(service.ReceivedToken).IsEqualTo(cts.Token)
			.Because("the container forwards the exact resolve-time token into the factory's CancellationToken parameter");
	}

	public interface IAsyncFactoryService;

	public sealed class AsyncFactoryService : IAsyncFactoryService
	{
		public static int BuildCount { get; private set; }

		public static void Reset() => BuildCount = 0;

		public static void Built() => BuildCount++;
	}

	[Container]
	[Singleton<AsyncFactoryService, IAsyncFactoryService>(Factory = nameof(CreateAsync))]
	public static partial class AsyncFactoryContainer
	{
		private static async Task<AsyncFactoryService> CreateAsync()
		{
			await Task.Yield();
			AsyncFactoryService.Built();
			return new AsyncFactoryService();
		}
	}

	public sealed class PragmaticAsyncFactoryService
	{
		public static int BuildCount { get; private set; }

		public static void Reset() => BuildCount = 0;

		public static void Built() => BuildCount++;
	}

	[Container(SyncResolveAfterInit = true)]
	[Singleton<PragmaticAsyncFactoryService>(Factory = nameof(CreateAsync))]
	public static partial class PragmaticAsyncFactoryContainer
	{
		private static async Task<PragmaticAsyncFactoryService> CreateAsync()
		{
			await Task.Yield();
			PragmaticAsyncFactoryService.Built();
			return new PragmaticAsyncFactoryService();
		}
	}

	public sealed class TransientAsyncFactoryService;

	[Container]
	[Transient<TransientAsyncFactoryService>(Factory = nameof(CreateAsync))]
	public static partial class TransientAsyncFactoryContainer
	{
		private static Task<TransientAsyncFactoryService> CreateAsync() => Task.FromResult(new TransientAsyncFactoryService());
	}

	// The factory result is itself IAsyncInitializable: the container awaits the factory, then awaits
	// InitializeAsync - so it is built and initialized exactly once each, with no double-initialization.
	public sealed class InitializingAsyncFactoryService : IAsyncInitializable
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

	[Container]
	[Singleton<InitializingAsyncFactoryService>(Factory = nameof(CreateAsync))]
	public static partial class InitializingAsyncFactoryContainer
	{
		private static Task<InitializingAsyncFactoryService> CreateAsync(CancellationToken _)
			=> Task.FromResult(new InitializingAsyncFactoryService());
	}

	// The factory does its own async initialization without the type implementing IAsyncInitializable.
	public sealed class SelfInitializingService
	{
		public bool Initialized { get; private set; }

		public void MarkInitialized() => Initialized = true;
	}

	[Container]
	[Singleton<SelfInitializingService>(Factory = nameof(CreateAsync))]
	public static partial class SelfInitializingFactoryContainer
	{
		private static async Task<SelfInitializingService> CreateAsync()
		{
			SelfInitializingService service = new();
			await Task.Yield();
			service.MarkInitialized();
			return service;
		}
	}

	// A non-disposable service interface hiding a concrete IDisposable, produced by an async Task<T> factory.
	// Async-taint comes purely from the factory being asynchronous (the type is not IAsyncInitializable), and
	// the disposable stays hidden behind the interface and the Task - so disposal can only be tracked by the
	// generated runtime is-IDisposable check on the awaited result. DisposeCount (not a bool) proves exactly once.
	public interface IHiddenDisposableFactoryService;

	public sealed class HiddenDisposableFactoryService : IHiddenDisposableFactoryService, IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	[Container]
	[Singleton<IHiddenDisposableFactoryService>(Factory = nameof(CreateAsync))]
	public static partial class HiddenDisposableAsyncFactoryContainer
	{
		private static Task<IHiddenDisposableFactoryService> CreateAsync()
			=> Task.FromResult<IHiddenDisposableFactoryService>(new HiddenDisposableFactoryService());
	}

	// A factory that takes a CancellationToken: the container forwards the resolve-time token, which the
	// service captures so the test can assert the exact token flowed through.
	public sealed class TokenAwareService(CancellationToken token)
	{
		public CancellationToken ReceivedToken { get; } = token;
	}

	[Container]
	[Singleton<TokenAwareService>(Factory = nameof(CreateAsync))]
	public static partial class TokenForwardingContainer
	{
		private static Task<TokenAwareService> CreateAsync(CancellationToken cancellationToken)
			=> Task.FromResult(new TokenAwareService(cancellationToken));
	}
}
