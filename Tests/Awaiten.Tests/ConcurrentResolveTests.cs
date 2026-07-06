using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
// ReSharper disable PartialTypeWithSinglePart

namespace Awaiten.Tests;

/// <summary>
///     Concurrency guarantees for singleton resolution and disposal: a singleton resolved from many
///     threads at once is constructed (and, for the async path, initialized) exactly once, and — because
///     the racing resolutions never double-track it in the disposal list — it is disposed exactly once
///     when the container is torn down. Covered on both the synchronous <c>Resolve</c> path and the
///     asynchronous <c>ResolveAsync</c> path.
/// </summary>
public partial class ConcurrentResolveTests
{
	[Fact]
	public async Task Singleton_ResolvedAsyncConcurrently_IsInitializedOnceAndDisposedOnce()
	{
		AsyncCountedService[] resolved = new AsyncCountedService[ThreadCount];

		using (AsyncContainer.Root container = new())
		{
			await FanOut(async index => resolved[index] = await container.ResolveAsync<AsyncCountedService>(Ct));

			await That(resolved.Distinct().Count()).IsEqualTo(1)
				.Because("every thread observes the one cached, initialized singleton");
			await That(container.Resolve<Counter>().Count).IsEqualTo(1)
				.Because("the singleton is constructed exactly once even when many threads call ResolveAsync at once");
			await That(resolved[0].InitializeCount).IsEqualTo(1)
				.Because("InitializeAsync runs exactly once under the async memoization, never once per racing caller");
			await That(resolved[0].Disposed).IsFalse()
				.Because("the container still owns the singleton");
		}

		await That(resolved[0].DisposeCount).IsEqualTo(1)
			.Because("the concurrently-async-resolved singleton is tracked for disposal exactly once, so container teardown disposes it exactly once");
	}

	[Fact]
	public async Task Singleton_ResolvedConcurrently_IsConstructedOnceAndDisposedOnce()
	{
		CountedService[] resolved = new CountedService[ThreadCount];

		using (SyncContainer.Root container = new())
		{
			await FanOut(index => resolved[index] = container.Resolve<CountedService>());

			await That(resolved.Distinct().Count()).IsEqualTo(1)
				.Because("every thread observes the one cached singleton");
			await That(container.Resolve<Counter>().Count).IsEqualTo(1)
				.Because("the singleton is constructed exactly once under the lock, no matter how many threads race to resolve it");
			await That(resolved[0].Disposed).IsFalse()
				.Because("the container still owns the singleton");
		}

		await That(resolved[0].DisposeCount).IsEqualTo(1)
			.Because("the concurrently-resolved singleton is tracked for disposal exactly once, so container teardown disposes it exactly once; had the racing resolutions double-tracked it, DisposeCount would exceed one");
	}

	[Fact]
	public async Task ScopedService_ResolvedConcurrentlyFromOneScope_IsConstructedOnceAndDisposedOnce()
	{
		ScopedCountedService[] resolved = new ScopedCountedService[ThreadCount];

		using ScopedContainer.Root container = new();
		ScopedCountedService captured;
		using (IAwaitenScope scope = container.CreateScope())
		{
			await FanOut(index => resolved[index] = scope.Resolve<ScopedCountedService>());

			await That(resolved.Distinct().Count()).IsEqualTo(1)
				.Because("every thread resolving from the one scope shares that scope's single instance");
			await That(container.Resolve<Counter>().Count).IsEqualTo(1)
				.Because("the per-scope memoization constructs the scoped service exactly once, no matter how many threads race to resolve it from the scope");
			captured = resolved[0];
			await That(captured.Disposed).IsFalse()
				.Because("the scope still owns the instance");
		}

		await That(captured.DisposeCount).IsEqualTo(1)
			.Because("disposing the scope disposes its scoped instance exactly once");
	}

	[Fact]
	public async Task ScopedService_ResolvedAsyncConcurrentlyFromOneScope_IsInitializedOnceAndDisposedOnce()
	{
		AsyncScopedCountedService[] resolved = new AsyncScopedCountedService[ThreadCount];

		using AsyncScopedContainer.Root container = new();
		AsyncScopedCountedService captured;
		// A synchronously-created scope leaves the async scoped service unwarmed, so the racing ResolveAsync
		// calls genuinely contend to build and initialize it (CreateScopeAsync would have warmed it up front).
		using (IAwaitenScope scope = container.CreateScope())
		{
			await FanOut(async index => resolved[index] = await scope.ResolveAsync<AsyncScopedCountedService>(Ct));

			await That(resolved.Distinct().Count()).IsEqualTo(1)
				.Because("every thread resolving from the one scope shares that scope's single initialized instance");
			await That(container.Resolve<Counter>().Count).IsEqualTo(1)
				.Because("the per-scope async memoization constructs the scoped service exactly once under concurrency");
			captured = resolved[0];
			await That(captured.InitializeCount).IsEqualTo(1)
				.Because("InitializeAsync runs exactly once for the scope's instance, never once per racing caller");
			await That(captured.Disposed).IsFalse()
				.Because("the scope still owns the instance");
		}

		await That(captured.DisposeCount).IsEqualTo(1)
			.Because("disposing the scope disposes its scoped instance exactly once");
	}

	[Fact]
	public async Task ScopedService_ResolvedFromManyConcurrentScopes_IsOneInstancePerScopeAndDisposedOncePerScope()
	{
		IAwaitenScope[] scopes = new IAwaitenScope[ThreadCount];
		ScopedCountedService[] resolved = new ScopedCountedService[ThreadCount];

		using (ScopedContainer.Root container = new())
		{
			// Each thread opens its own scope and resolves from it, so scope creation and resolution race together.
			await FanOut(index =>
			{
				IAwaitenScope scope = container.CreateScope();
				scopes[index] = scope;
				resolved[index] = scope.Resolve<ScopedCountedService>();
			});

			await That(resolved.Distinct().Count()).IsEqualTo(ThreadCount)
				.Because("each scope gets its own scoped instance");
			await That(container.Resolve<Counter>().Count).IsEqualTo(ThreadCount)
				.Because("exactly one construction per scope, none lost or duplicated under the concurrent scope creation");
			await That(resolved.All(r => !r.Disposed)).IsTrue()
				.Because("no scope has been disposed yet");

			// Tear the scopes down concurrently; each must dispose only the one instance it owns, exactly once.
			await FanOut(index => scopes[index].Dispose());

			await That(resolved.All(r => r.DisposeCount == 1)).IsTrue()
				.Because("each scope disposes exactly the one instance it owns, exactly once, even when the scopes are torn down concurrently");
		}

		await That(resolved.All(r => r.DisposeCount == 1)).IsTrue()
			.Because("disposing the container after its scopes were already torn down does not dispose their scoped instances a second time");
	}

	[Fact]
	public async Task ScopedService_ResolvedAsyncFromManyConcurrentScopes_IsInitializedOncePerScopeAndDisposedOncePerScope()
	{
		IAwaitenScope[] scopes = new IAwaitenScope[ThreadCount];
		AsyncScopedCountedService[] resolved = new AsyncScopedCountedService[ThreadCount];

		using (AsyncScopedContainer.Root container = new())
		{
			await FanOut(async index =>
			{
				IAwaitenScope scope = await container.CreateScopeAsync(Ct);
				scopes[index] = scope;
				resolved[index] = await scope.ResolveAsync<AsyncScopedCountedService>(Ct);
			});

			await That(resolved.Distinct().Count()).IsEqualTo(ThreadCount)
				.Because("each scope gets its own initialized scoped instance");
			await That(container.Resolve<Counter>().Count).IsEqualTo(ThreadCount)
				.Because("exactly one construction per scope under the concurrent CreateScopeAsync");
			await That(resolved.All(r => r.InitializeCount == 1)).IsTrue()
				.Because("each scope initializes its own instance exactly once");

			await FanOut(index => scopes[index].Dispose());

			await That(resolved.All(r => r.DisposeCount == 1)).IsTrue()
				.Because("each scope disposes exactly the one instance it owns, exactly once, even when the scopes are torn down concurrently");
		}

		await That(resolved.All(r => r.DisposeCount == 1)).IsTrue()
			.Because("disposing the container after its scopes were already torn down does not dispose their scoped instances a second time");
	}

	[Fact]
	public async Task Singleton_ResolvedFromManyConcurrentScopes_IsSharedAndDisposedOnceByTheRoot()
	{
		IAwaitenScope[] scopes = new IAwaitenScope[ThreadCount];
		CountedService[] resolved = new CountedService[ThreadCount];

		using (SyncContainer.Root container = new())
		{
			await FanOut(index =>
			{
				IAwaitenScope scope = container.CreateScope();
				scopes[index] = scope;
				resolved[index] = scope.Resolve<CountedService>();
			});

			await That(resolved.Distinct().Count()).IsEqualTo(1)
				.Because("a singleton is shared across every concurrently-created scope");
			await That(container.Resolve<Counter>().Count).IsEqualTo(1)
				.Because("the shared singleton is constructed exactly once regardless of how many scopes resolve it");

			// Disposing the child scopes concurrently must not touch the root-owned singleton.
			await FanOut(index => scopes[index].Dispose());

			await That(resolved[0].Disposed).IsFalse()
				.Because("a singleton is owned by the root, so disposing the child scopes leaves it alive");
		}

		await That(resolved[0].DisposeCount).IsEqualTo(1)
			.Because("the root owns the singleton and disposes it exactly once, never once per scope that resolved it");
	}

	[Fact]
	public async Task DifferentSingletons_ResolvedConcurrently_AreEachConstructedOnceAndDisposedOnce()
	{
		HeteroService[] captured;

		using (HeteroContainer.Root container = new())
		{
			HeteroA[] a = new HeteroA[ThreadCount];
			HeteroB[] b = new HeteroB[ThreadCount];
			HeteroC[] c = new HeteroC[ThreadCount];
			HeteroD[] d = new HeteroD[ThreadCount];

			// Every thread resolves all four distinct singleton types at once, so heterogeneous resolutions
			// contend on the shared singleton cache and the shared disposal-tracking list simultaneously.
			await FanOut(index =>
			{
				a[index] = container.Resolve<HeteroA>();
				b[index] = container.Resolve<HeteroB>();
				c[index] = container.Resolve<HeteroC>();
				d[index] = container.Resolve<HeteroD>();
			});

			await That(a.Distinct().Count()).IsEqualTo(1);
			await That(b.Distinct().Count()).IsEqualTo(1);
			await That(c.Distinct().Count()).IsEqualTo(1);
			await That(d.Distinct().Count()).IsEqualTo(1);
			await That(container.Resolve<HeteroCounter>().Count).IsEqualTo(4)
				.Because("each of the four distinct singletons is constructed exactly once, none duplicated under the concurrent heterogeneous load");

			captured = new HeteroService[]
			{
				a[0], b[0], c[0], d[0],
			};
			await That(captured.All(s => !s.Disposed)).IsTrue()
				.Because("the container still owns them");
		}

		await That(captured.All(s => s.DisposeCount == 1)).IsTrue()
			.Because("every distinct singleton is tracked once and disposed exactly once on teardown; a race in the shared disposal list would drop or duplicate a disposal");
	}

	[Fact]
	public async Task DependencyGraph_ResolvedConcurrently_ConstructsEachNodeExactlyOnce()
	{
		GraphTop[] resolved = new GraphTop[ThreadCount];

		using GraphContainer.Root container = new();

		await FanOut(index => resolved[index] = container.Resolve<GraphTop>());

		await That(resolved.Distinct().Count()).IsEqualTo(1)
			.Because("every thread observes the one cached top of the graph");
		// GraphTop needs GraphMid needs GraphLeaf, so all three nodes are constructed at least once; exactly three
		// total constructions therefore means each node was built exactly once, never a duplicate leaf or mid.
		await That(container.Resolve<GraphCounter>().Count).IsEqualTo(3)
			.Because("the construction lock spans the whole transitive build, so each shared node in the graph is built exactly once under concurrency");
	}

	[Fact]
	public async Task DifferentClosedGenerics_ResolvedConcurrently_ConstructEachClosedTypeExactlyOnce()
	{
		ICache<Tag1>[] c1 = new ICache<Tag1>[ThreadCount];
		ICache<Tag2>[] c2 = new ICache<Tag2>[ThreadCount];
		ICache<Tag3>[] c3 = new ICache<Tag3>[ThreadCount];
		ICache<Tag4>[] c4 = new ICache<Tag4>[ThreadCount];

		using GenericContainer.Root container = new();

		// Every thread resolves four different closed generics from the one open registration at once, so the
		// per-closed-type memoization is contended across distinct constructed types simultaneously.
		await FanOut(index =>
		{
			c1[index] = container.Resolve<ICache<Tag1>>();
			c2[index] = container.Resolve<ICache<Tag2>>();
			c3[index] = container.Resolve<ICache<Tag3>>();
			c4[index] = container.Resolve<ICache<Tag4>>();
		});

		await That(c1.Distinct().Count()).IsEqualTo(1);
		await That(c2.Distinct().Count()).IsEqualTo(1);
		await That(c3.Distinct().Count()).IsEqualTo(1);
		await That(c4.Distinct().Count()).IsEqualTo(1);
		await That(container.Resolve<GenericCounter>().Count).IsEqualTo(4)
			.Because("each of the four distinct closed generics is cached and constructed exactly once, so the concurrent expansion of distinct closed types never duplicates one");
	}

	[Fact]
	public async Task KeyedSingletons_ResolvedConcurrently_EachKeyConstructedOnceAndDisposedOnce()
	{
		KeyedServiceBase capturedFast;
		KeyedServiceBase capturedSlow;

		using (KeyedSingletonContainer.Root container = new())
		{
			IKeyedService[] fast = new IKeyedService[ThreadCount];
			IKeyedService[] slow = new IKeyedService[ThreadCount];

			// Both keys are resolved on every thread at once, contending on the keyed dispatch/cache.
			await FanOut(index =>
			{
				fast[index] = container.Resolve<IKeyedService>("fast");
				slow[index] = container.Resolve<IKeyedService>("slow");
			});

			await That(fast.Distinct().Count()).IsEqualTo(1)
				.Because("the 'fast' key resolves to one cached singleton");
			await That(slow.Distinct().Count()).IsEqualTo(1)
				.Because("the 'slow' key resolves to one cached singleton");
			await That((object)fast[0]).IsNotSameAs(slow[0])
				.Because("the two keys resolve to distinct instances");
			await That(container.Resolve<KeyedCounter>().Count).IsEqualTo(2)
				.Because("one instance per key, each constructed exactly once under the concurrent keyed lookups");

			capturedFast = (KeyedServiceBase)fast[0];
			capturedSlow = (KeyedServiceBase)slow[0];
		}

		await That(capturedFast.DisposeCount).IsEqualTo(1);
		await That(capturedSlow.DisposeCount).IsEqualTo(1)
			.Because("each keyed singleton is disposed exactly once on container teardown");
	}

	[Fact]
	public async Task Transients_ResolvedConcurrentlyWithinOneScope_AreEachDisposedExactlyOnceWhenTheScopeCloses()
	{
		const int perThread = 50;
		ConcurrentBag<ScopedTransient> produced = new();
		ScopedTransient[] captured;

		using TransientScopeContainer.Root container = new();
		using (IAwaitenScope scope = container.CreateScope())
		{
			await FanOut(_ =>
			{
				for (int i = 0; i < perThread; i++)
				{
					produced.Add(scope.Resolve<ScopedTransient>());
				}
			});

			captured = produced.ToArray();
			await That(captured.Length).IsEqualTo(ThreadCount * perThread)
				.Because("every resolution returns a fresh transient, none lost by a race in the scope's tracking list");
			await That(captured.All(t => !t.Disposed)).IsTrue()
				.Because("tracked transients live until the scope is disposed");
		}

		await That(captured.All(t => t.DisposeCount == 1)).IsTrue()
			.Because("each concurrently-tracked transient is disposed exactly once when the scope closes");
	}

	private const int ThreadCount = 64;

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	// Releases all worker threads simultaneously so the resolutions genuinely race, then joins them.
	private static async Task FanOut(Action<int> body)
	{
		using ManualResetEventSlim start = new(false);
		Task[] workers = new Task[ThreadCount];
		for (int t = 0; t < ThreadCount; t++)
		{
			int index = t;
			workers[index] = Task.Run(() =>
			{
				start.Wait();
				body(index);
			}, Ct);
		}

		start.Set();
		await Task.WhenAll(workers);
	}

	private static async Task FanOut(Func<int, Task> body)
	{
		using ManualResetEventSlim start = new(false);
		Task[] workers = new Task[ThreadCount];
		for (int t = 0; t < ThreadCount; t++)
		{
			int index = t;
			workers[index] = Task.Run(async () =>
			{
				start.Wait();
				await body(index);
			}, Ct);
		}

		start.Set();
		await Task.WhenAll(workers);
	}

	// A per-container construction counter injected into services so their constructions can be counted
	// without static state shared across test methods.
	public sealed class Counter
	{
		private int _count;

		public int Count => Volatile.Read(ref _count);

		public void Mark() => Interlocked.Increment(ref _count);
	}

	public sealed class CountedService : IDisposable
	{
		private int _disposeCount;

		public CountedService(Counter counter) => counter.Mark();

		public int DisposeCount => Volatile.Read(ref _disposeCount);

		public bool Disposed => DisposeCount != 0;

		public void Dispose() => Interlocked.Increment(ref _disposeCount);
	}

	[Container]
	[Singleton<Counter>]
	[Singleton<CountedService>]
	public static partial class SyncContainer;

	public sealed class AsyncCountedService : IAsyncInitializable, IDisposable
	{
		private int _initializeCount;
		private int _disposeCount;

		public AsyncCountedService(Counter counter) => counter.Mark();

		public int InitializeCount => Volatile.Read(ref _initializeCount);

		public int DisposeCount => Volatile.Read(ref _disposeCount);

		public bool Disposed => DisposeCount != 0;

		public async Task InitializeAsync(CancellationToken cancellationToken)
		{
			// Yield so initialization genuinely runs asynchronously while the other callers are racing.
			await Task.Yield();
			Interlocked.Increment(ref _initializeCount);
		}

		public void Dispose() => Interlocked.Increment(ref _disposeCount);
	}

	[Container]
	[Singleton<Counter>]
	[Singleton<AsyncCountedService>]
	public static partial class AsyncContainer;

	public sealed class ScopedCountedService : IDisposable
	{
		private int _disposeCount;

		public ScopedCountedService(Counter counter) => counter.Mark();

		// Instance-level so each scope's own instance can be asserted to have been disposed exactly once.
		public int DisposeCount => Volatile.Read(ref _disposeCount);

		public bool Disposed => DisposeCount != 0;

		public void Dispose() => Interlocked.Increment(ref _disposeCount);
	}

	[Container]
	[Singleton<Counter>]
	[Scoped<ScopedCountedService>]
	public static partial class ScopedContainer;

	public sealed class AsyncScopedCountedService : IAsyncInitializable, IDisposable
	{
		private int _disposeCount;
		private int _initializeCount;

		public AsyncScopedCountedService(Counter counter) => counter.Mark();

		public int InitializeCount => Volatile.Read(ref _initializeCount);

		public int DisposeCount => Volatile.Read(ref _disposeCount);

		public bool Disposed => DisposeCount != 0;

		public async Task InitializeAsync(CancellationToken cancellationToken)
		{
			// Yield so initialization genuinely runs asynchronously while the other callers are racing.
			await Task.Yield();
			Interlocked.Increment(ref _initializeCount);
		}

		public void Dispose() => Interlocked.Increment(ref _disposeCount);
	}

	[Container]
	[Singleton<Counter>]
	[Scoped<AsyncScopedCountedService>]
	public static partial class AsyncScopedContainer;

	// A per-container counter: injected into services so their constructions can be counted without static state.
	public sealed class HeteroCounter
	{
		private int _count;

		public int Count => Volatile.Read(ref _count);

		public void Mark() => Interlocked.Increment(ref _count);
	}

	public abstract class HeteroService : IDisposable
	{
		private int _disposeCount;

		protected HeteroService(HeteroCounter counter)
		{
			counter.Mark();
		}

		public int DisposeCount => Volatile.Read(ref _disposeCount);

		public bool Disposed => DisposeCount != 0;

		public void Dispose()
		{
			Interlocked.Increment(ref _disposeCount);
			GC.SuppressFinalize(this);
		}
	}

	public sealed class HeteroA(HeteroCounter counter) : HeteroService(counter);

	public sealed class HeteroB(HeteroCounter counter) : HeteroService(counter);

	public sealed class HeteroC(HeteroCounter counter) : HeteroService(counter);

	public sealed class HeteroD(HeteroCounter counter) : HeteroService(counter);

	[Container]
	[Singleton<HeteroCounter>]
	[Singleton<HeteroA>]
	[Singleton<HeteroB>]
	[Singleton<HeteroC>]
	[Singleton<HeteroD>]
	public static partial class HeteroContainer;

	public sealed class GraphCounter
	{
		private int _count;

		public int Count => Volatile.Read(ref _count);

		public void Mark() => Interlocked.Increment(ref _count);
	}

	public sealed class GraphLeaf
	{
		public GraphLeaf(GraphCounter counter)
		{
			counter.Mark();
		}
	}

	public sealed class GraphMid
	{
		// ReSharper disable once UnusedParameter.Local
		public GraphMid(GraphCounter counter, GraphLeaf leaf)
		{
			counter.Mark();
		}
	}

	public sealed class GraphTop
	{
		// ReSharper disable once UnusedParameter.Local
		public GraphTop(GraphCounter counter, GraphMid mid, GraphLeaf leaf)
		{
			counter.Mark();
		}
	}

	[Container]
	[Singleton<GraphCounter>]
	[Singleton<GraphLeaf>]
	[Singleton<GraphMid>]
	[Singleton<GraphTop>]
	public static partial class GraphContainer;

	public sealed class GenericCounter
	{
		private int _count;

		public int Count => Volatile.Read(ref _count);

		public void Mark() => Interlocked.Increment(ref _count);
	}

	public sealed class Tag1;

	public sealed class Tag2;

	public sealed class Tag3;

	public sealed class Tag4;

	public interface ICache<T>;

	public sealed class Cache<T> : ICache<T>
	{
		public Cache(GenericCounter counter)
		{
			counter.Mark();
		}
	}

	// Seeds the closed-generic expansion for the type arguments the tests resolve; never resolved itself.
	public sealed class GenericSeed
	{
		// ReSharper disable once UnusedParameter.Local
		public GenericSeed(ICache<Tag1> a, ICache<Tag2> b, ICache<Tag3> c, ICache<Tag4> d)
		{
		}
	}

	[Container]
	[Singleton<GenericCounter>]
	[Singleton(typeof(Cache<>), typeof(ICache<>))]
	[Singleton<GenericSeed>]
	public static partial class GenericContainer;

	public interface IKeyedService;

	public sealed class KeyedCounter
	{
		private int _count;

		public int Count => Volatile.Read(ref _count);

		public void Mark() => Interlocked.Increment(ref _count);
	}

	public abstract class KeyedServiceBase : IKeyedService, IDisposable
	{
		private int _disposeCount;

		protected KeyedServiceBase(KeyedCounter counter)
		{
			counter.Mark();
		}

		public int DisposeCount => Volatile.Read(ref _disposeCount);

		public bool Disposed => DisposeCount != 0;

		public void Dispose()
		{
			Interlocked.Increment(ref _disposeCount);
			GC.SuppressFinalize(this);
		}
	}

	public sealed class FastKeyed(KeyedCounter counter) : KeyedServiceBase(counter);

	public sealed class SlowKeyed(KeyedCounter counter) : KeyedServiceBase(counter);

	[Container]
	[Singleton<KeyedCounter>]
	[Singleton<FastKeyed, IKeyedService>(Key = "fast")]
	[Singleton<SlowKeyed, IKeyedService>(Key = "slow")]
	public static partial class KeyedSingletonContainer;

	public sealed class ScopedTransient : IDisposable
	{
		private int _disposeCount;

		public int DisposeCount => Volatile.Read(ref _disposeCount);

		public bool Disposed => DisposeCount != 0;

		public void Dispose() => Interlocked.Increment(ref _disposeCount);
	}

	// Loose: the test resolves the disposable transient directly by type and asserts it is tracked by and
	// disposed with its owning scope, the permissive path that strict lifetime safety withholds.
	[Container(LifetimeSafety = LifetimeSafety.Loose)]
	[Transient<ScopedTransient>]
	public static partial class TransientScopeContainer;

#if NET || NETSTANDARD2_1_OR_GREATER
	[Fact]
	public async Task AsyncDisposableSingleton_ResolvedConcurrently_IsInitializedOnceAndAsyncDisposedOnce()
	{
		AsyncDisposableSingleton captured;
		await using (AsyncDisposableContainer.Root container = new())
		{
			AsyncDisposableSingleton[] resolved = new AsyncDisposableSingleton[ThreadCount];
			await FanOut(async index => resolved[index] = await container.ResolveAsync<AsyncDisposableSingleton>(Ct));

			await That(resolved.Distinct().Count()).IsEqualTo(1)
				.Because("every thread observes the one cached, initialized singleton");
			captured = resolved[0];
			await That(captured.InitializeCount).IsEqualTo(1)
				.Because("InitializeAsync runs exactly once under the async memoization, never once per racing caller");
			await That(captured.DisposeAsyncCount).IsEqualTo(0)
				.Because("the container still owns it");
		}

		await That(captured.DisposeAsyncCount).IsEqualTo(1)
			.Because("the concurrently-resolved async-disposable singleton is drained through DisposeAsync exactly once on container teardown");
	}

	public sealed class AsyncDisposableSingleton : IAsyncInitializable, IAsyncDisposable
	{
		private int _initializeCount;
		private int _disposeAsyncCount;

		public int InitializeCount => Volatile.Read(ref _initializeCount);

		public int DisposeAsyncCount => Volatile.Read(ref _disposeAsyncCount);

		public async Task InitializeAsync(CancellationToken cancellationToken)
		{
			// Yield so initialization genuinely runs asynchronously while the other callers are racing.
			await Task.Yield();
			Interlocked.Increment(ref _initializeCount);
		}

		public ValueTask DisposeAsync()
		{
			Interlocked.Increment(ref _disposeAsyncCount);
			return default;
		}
	}

	[Container]
	[Singleton<AsyncDisposableSingleton>]
	public static partial class AsyncDisposableContainer;
#endif
}
