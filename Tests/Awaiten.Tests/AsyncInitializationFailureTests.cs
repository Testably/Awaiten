using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     Failure semantics of asynchronous construction: when a service's own <c>InitializeAsync</c>, or a
///     deferred <c>[Inject(Deferred = true)]</c> member's wiring, throws, the instance that was already
///     constructed must be disposed rather than leaked - and disposed exactly once (it is registered for
///     teardown only on success, so the failure-path cleanup never double-disposes). This holds identically
///     for the transient (fresh) resolver and the memoized (scoped/singleton) resolver. A throw from the
///     instance's own disposal on that failure path must not mask the original failure.
///     <para>
///         The probes are built by the container, and on the failure paths the resolve throws, so the test
///         never receives the instance to inspect. Each probe therefore records its construction and disposal
///         on a <see cref="Recorder" /> injected into it - a per-container singleton the test resolves and
///         reads. Because every test builds its own container, each has its own recorder (no shared state).
///         The containers and services are nested types, so the enclosing class is <c>partial</c>.
///     </para>
/// </summary>
public partial class AsyncInitializationFailureTests
{
	[Fact]
	public async Task Singleton_OwnInitThrows_InstanceIsDisposedOnce()
	{
		using SingletonOwnInitContainer.Root container = new();
		Recorder recorder = container.Resolve<Recorder>();

		await That(() => container.ResolveAsync<InitThrows>(TestContext.Current.CancellationToken))
			.Throws<InvalidOperationException>();

		await That(recorder.Constructed).IsEqualTo(1);
		await That(recorder.Disposed).IsEqualTo(1).Because("a failed instance is disposed, not leaked");
	}

	[Fact]
	public async Task Singleton_DeferredMemberThrows_OwnerIsDisposedOnce()
	{
		using SingletonDeferredContainer.Root container = new();
		Recorder recorder = container.Resolve<Recorder>();

		await That(() => container.ResolveAsync<DeferredOwner>(TestContext.Current.CancellationToken))
			.Throws<InvalidOperationException>();

		await That(recorder.Constructed).IsEqualTo(1);
		await That(recorder.Disposed).IsEqualTo(1).Because("the owner must not leak when deferred wiring throws");
	}

	[Fact]
	public async Task Singleton_FaultedInitIsNotCached_RetryBuildsAFreshInstance()
	{
		using SingletonFlakyContainer.Root container = new();
		Recorder recorder = container.Resolve<Recorder>();

		await That(() => container.ResolveAsync<Flaky>(TestContext.Current.CancellationToken))
			.Throws<InvalidOperationException>();

		Flaky second = await container.ResolveAsync<Flaky>(TestContext.Current.CancellationToken);

		await That(recorder.Constructed).IsEqualTo(2).Because("the faulted task is evicted, so the retry builds a fresh instance");
		await That(second.Initialized).IsTrue();
		await That(recorder.Disposed).IsEqualTo(1).Because("only the first, failed instance was disposed at this point");
	}

	// A disposable transient is withheld from by-type resolution off the Root in strict mode (steered to
	// Owned<T>), so the transient tests resolve from a child scope, where transient disposables are bounded
	// by the scope.
	[Fact]
	public async Task Transient_OwnInitThrows_InstanceIsDisposedOnce()
	{
		using TransientOwnInitContainer.Root container = new();
		Recorder recorder = container.Resolve<Recorder>();
		using IAwaitenScope scope = container.CreateScope();

		await That(() => scope.ResolveAsync<InitThrows>(TestContext.Current.CancellationToken))
			.Throws<InvalidOperationException>();

		await That(recorder.Constructed).IsEqualTo(1);
		await That(recorder.Disposed).IsEqualTo(1).Because("a failed transient is disposed, not leaked");
	}

	[Fact]
	public async Task Transient_DeferredMemberThrows_OwnerIsDisposedOnce()
	{
		using TransientDeferredContainer.Root container = new();
		Recorder recorder = container.Resolve<Recorder>();
		using IAwaitenScope scope = container.CreateScope();

		await That(() => scope.ResolveAsync<DeferredOwner>(TestContext.Current.CancellationToken))
			.Throws<InvalidOperationException>();

		await That(recorder.Constructed).IsEqualTo(1);
		await That(recorder.Disposed).IsEqualTo(1).Because("the transient owner must not leak when deferred wiring throws");
	}

	[Fact]
	public async Task InitThrows_AndDisposeAlsoThrows_OriginalFailurePropagates()
	{
		using DisposeAlsoThrowsContainer.Root container = new();

		// The instance's InitializeAsync throws "init-boom"; its Dispose then throws "dispose-boom" during the
		// failure-path cleanup. The caller must still see the original init failure, not the disposal one.
		await That(() => container.ResolveAsync<InitAndDisposeThrow>(TestContext.Current.CancellationToken))
			.Throws<InvalidOperationException>()
			.WithMessage("*init-boom*").AsWildcard();
	}

	[Fact]
	public async Task Singleton_SuccessfulInit_IsRegisteredAndDisposedAtTeardown()
	{
		using SingletonFlakyContainer.Root container = new();
		Recorder recorder = container.Resolve<Recorder>();

		// Flaky throws on the first init and succeeds on the second, so resolve twice to get a live instance.
		await That(() => container.ResolveAsync<Flaky>(TestContext.Current.CancellationToken))
			.Throws<InvalidOperationException>();
		await container.ResolveAsync<Flaky>(TestContext.Current.CancellationToken);

		await That(recorder.Disposed).IsEqualTo(1).Because("only the failed instance is disposed so far");

		container.Dispose();

		await That(recorder.Disposed).IsEqualTo(2).Because("the live instance is registered on success and disposed at teardown");
	}

	/// <summary>Per-container observation channel: the probes report construction and disposal here.</summary>
	public sealed class Recorder
	{
		public int Constructed { get; set; }

		public int Disposed { get; set; }

		public int Attempts { get; set; }
	}

	public sealed class InitThrows : IAsyncInitializable, IDisposable
	{
		private readonly Recorder _recorder;

		public InitThrows(Recorder recorder)
		{
			_recorder = recorder;
			_recorder.Constructed++;
		}

		public Task InitializeAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("init-boom");

		public void Dispose() => _recorder.Disposed++;
	}

	public sealed class BadDep : IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("dep-boom");
	}

	public sealed class DeferredOwner : IDisposable
	{
		private readonly Recorder _recorder;

		public DeferredOwner(Recorder recorder)
		{
			_recorder = recorder;
			_recorder.Constructed++;
		}

		[Inject(Deferred = true)]
		public BadDep? Dep { get; set; }

		public void Dispose() => _recorder.Disposed++;
	}

	public sealed class Flaky : IAsyncInitializable, IDisposable
	{
		private readonly Recorder _recorder;

		public Flaky(Recorder recorder)
		{
			_recorder = recorder;
			_recorder.Constructed++;
		}

		public bool Initialized { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			_recorder.Attempts++;
			if (_recorder.Attempts == 1)
			{
				throw new InvalidOperationException("first-attempt-boom");
			}

			Initialized = true;
			return Task.CompletedTask;
		}

		public void Dispose() => _recorder.Disposed++;
	}

	public sealed class InitAndDisposeThrow : IAsyncInitializable, IDisposable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("init-boom");
		public void Dispose() => throw new InvalidOperationException("dispose-boom");
	}

	[Container]
	[Singleton<Recorder>]
	[Singleton<InitThrows>]
	public static partial class SingletonOwnInitContainer;

	[Container]
	[Singleton<Recorder>]
	[Singleton<BadDep>]
	[Singleton<DeferredOwner>]
	public static partial class SingletonDeferredContainer;

	[Container]
	[Singleton<Recorder>]
	[Singleton<Flaky>]
	public static partial class SingletonFlakyContainer;

	[Container]
	[Singleton<Recorder>]
	[Transient<InitThrows>]
	public static partial class TransientOwnInitContainer;

	[Container]
	[Singleton<Recorder>]
	[Singleton<BadDep>]
	[Transient<DeferredOwner>]
	public static partial class TransientDeferredContainer;

	[Container]
	[Singleton<InitAndDisposeThrow>]
	public static partial class DisposeAlsoThrowsContainer;
}
