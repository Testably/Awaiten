namespace Awaiten.Tests;

/// <summary>
///     Failure semantics of synchronous deferred wiring: when a <c>[Inject(Deferred = true)]</c> member's wiring
///     throws while a disposable owner is being resolved, the already-constructed owner must be disposed rather
///     than leaked, and disposed exactly once (it is registered for teardown only on success, so the
///     failure-path cleanup never double-disposes). This is the synchronous counterpart of
///     <see cref="AsyncInitializationFailureTests" />, covering the transient (fresh) and memoized
///     (scoped/singleton) synchronous resolvers, including the wiring-episode rollback that spans a deferred
///     cycle. The probes report construction and disposal on a per-container <see cref="Recorder" /> singleton.
/// </summary>
public partial class SyncDeferredWiringFailureTests
{
	[Fact]
	public async Task Singleton_DeferredMemberThrows_OwnerIsDisposedOnce()
	{
		using SingletonContainer.Root container = new();
		Recorder recorder = container.Resolve<Recorder>();

		await That(() => container.Resolve<DeferredOwner>()).Throws<InvalidOperationException>();

		await That(recorder.Constructed).IsEqualTo(1);
		await That(recorder.Disposed).IsEqualTo(1).Because("the owner must not leak when deferred wiring throws");
	}

	[Fact]
	public async Task Singleton_FailedWiringIsRolledBack_RetryBuildsAFreshInstance()
	{
		using SingletonFlakyContainer.Root container = new();
		Recorder recorder = container.Resolve<Recorder>();

		await That(() => container.Resolve<FlakyOwner>()).Throws<InvalidOperationException>();

		FlakyOwner second = container.Resolve<FlakyOwner>();

		await That(recorder.Constructed).IsEqualTo(2).Because("the failed episode was rolled back, so the retry rebuilds the owner");
		await That(second.Dep).IsNotNull().Because("the retry wires the deferred member completely");
		await That(recorder.Disposed).IsEqualTo(1).Because("only the first, failed owner was disposed at this point");
	}

	// A disposable transient is withheld from by-type resolution off the Root in strict mode (steered to
	// Owned<T>), so the transient test resolves from a child scope, where transient disposables are bounded
	// by the scope.
	[Fact]
	public async Task Transient_DeferredMemberThrows_OwnerIsDisposedOnce()
	{
		using TransientContainer.Root container = new();
		Recorder recorder = container.Resolve<Recorder>();
		using IAwaitenScope scope = container.CreateScope();

		await That(() => scope.Resolve<DeferredOwner>()).Throws<InvalidOperationException>();

		await That(recorder.Constructed).IsEqualTo(1);
		await That(recorder.Disposed).IsEqualTo(1).Because("the transient owner must not leak when deferred wiring throws");
	}

	// A peer built and registered in an earlier step of the same failed wiring episode is unpublished by the
	// rollback but must not be disposed there: it stays tracked and is disposed exactly once at teardown, so
	// the per-frame failure cleanup never disposes an already-registered instance twice.
	[Fact]
	public async Task DeferredWiringThrows_AfterAPeerWasRegistered_PeerIsDisposedOnceAtTeardown()
	{
		using PeerContainer.Root container = new();
		Recorder recorder = container.Resolve<Recorder>();

		await That(() => container.Resolve<TwoDeferredOwner>()).Throws<InvalidOperationException>();

		await That(recorder.Disposed).IsEqualTo(1).Because("the failed owner is disposed on the failure path");
		await That(recorder.PeerDisposed).IsEqualTo(0).Because("the registered peer is only unpublished by the rollback, not disposed on the failure path");

		container.Dispose();

		await That(recorder.PeerDisposed).IsEqualTo(1).Because("the peer stays registered and is disposed exactly once at teardown");
		await That(recorder.Disposed).IsEqualTo(1).Because("the owner is not disposed again at teardown");
	}

	/// <summary>Per-container observation channel: the probes report construction and disposal here.</summary>
	public sealed class Recorder
	{
		public int Constructed { get; set; }

		public int Disposed { get; set; }

		public int PeerDisposed { get; set; }

		public int Attempts { get; set; }
	}

	/// <summary>Constructing this throws, so wiring it as a deferred member faults the owner's wiring.</summary>
	public sealed class BadDep
	{
		public BadDep() => throw new InvalidOperationException("dep-boom");
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

	public sealed class FlakyDep
	{
		public FlakyDep(Recorder recorder)
		{
			if (recorder.Attempts++ == 0)
			{
				throw new InvalidOperationException("first-attempt-boom");
			}
		}
	}

	public sealed class FlakyOwner : IDisposable
	{
		private readonly Recorder _recorder;

		public FlakyOwner(Recorder recorder)
		{
			_recorder = recorder;
			_recorder.Constructed++;
		}

		[Inject(Deferred = true)]
		public FlakyDep? Dep { get; set; }

		public void Dispose() => _recorder.Disposed++;
	}

	public sealed class Leaf;

	/// <summary>A disposable, deferred-member peer: built and registered before its sibling's wiring throws.</summary>
	public sealed class Peer : IDisposable
	{
		private readonly Recorder _recorder;

		public Peer(Recorder recorder) => _recorder = recorder;

		[Inject(Deferred = true)]
		public Leaf? Leaf { get; set; }

		public void Dispose() => _recorder.PeerDisposed++;
	}

	public sealed class TwoDeferredOwner : IDisposable
	{
		private readonly Recorder _recorder;

		public TwoDeferredOwner(Recorder recorder)
		{
			_recorder = recorder;
			_recorder.Constructed++;
		}

		[Inject(Deferred = true)]
		public Peer? Peer { get; set; }

		[Inject(Deferred = true)]
		public BadDep? Bad { get; set; }

		public void Dispose() => _recorder.Disposed++;
	}

	[Container]
	[Singleton<Recorder>]
	[Singleton<BadDep>]
	[Singleton<DeferredOwner>]
	public static partial class SingletonContainer;

	[Container]
	[Singleton<Recorder>]
	[Transient<FlakyDep>]
	[Singleton<FlakyOwner>]
	public static partial class SingletonFlakyContainer;

	[Container]
	[Singleton<Recorder>]
	[Singleton<BadDep>]
	[Transient<DeferredOwner>]
	public static partial class TransientContainer;

	[Container]
	[Singleton<Recorder>]
	[Singleton<Leaf>]
	[Singleton<BadDep>]
	[Singleton<Peer>]
	[Singleton<TwoDeferredOwner>]
	public static partial class PeerContainer;
}
