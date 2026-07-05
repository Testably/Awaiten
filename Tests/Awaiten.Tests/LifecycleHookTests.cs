using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of lifecycle hooks (<c>OnActivated</c> / <c>OnRelease</c>): a container's
///     <c>OnActivated</c> method runs once an instance is constructed, and its <c>OnRelease</c> method runs when
///     the owning container or scope is disposed - in reverse creation order and before the instance's own
///     disposal, for a non-disposable instance too. A <c>[Container]</c> is a static definition, so the hooks
///     are static methods that record into a static probe and the usable instance is <c>new …Root()</c>;
///     services and containers are nested types, so the enclosing class is <c>partial</c>.
/// </summary>
public partial class LifecycleHookTests
{
	[Fact]
	public async Task OnActivated_RunsAfterConstruction_AndOnRelease_RunsOnDisposalInReverseOrder()
	{
		Probe.Log.Clear();
		using (HookContainer.Root container = new())
		{
			container.Resolve<Alpha>();
			container.Resolve<Beta>();

			// Both were activated once constructed, and nothing has been released while the container is alive.
			await That(Probe.Log).Contains("activated:Alpha");
			await That(Probe.Log).Contains("activated:Beta");
			await That(Probe.Log).DoesNotContain("released:Alpha");
		}

		// Released on disposal, in reverse creation order (Beta constructed after Alpha, so released before it).
		await That(Probe.Log.IndexOf("released:Beta") < Probe.Log.IndexOf("released:Alpha")).IsTrue();
	}

	[Fact]
	public async Task OnRelease_RunsBeforeTheInstancesOwnDisposal()
	{
		Probe.Log.Clear();
		using (DisposableHookContainer.Root container = new())
		{
			container.Resolve<Tracked>();
		}

		// The release hook runs while the instance is still alive, ahead of its own Dispose.
		await That(Probe.Log.IndexOf("released:Tracked") < Probe.Log.IndexOf("disposed:Tracked")).IsTrue();
	}

	[Fact]
	public async Task TransientHooks_RunOncePerConstructedInstance()
	{
		Probe.Log.Clear();
		using (TransientHookContainer.Root container = new())
		{
			container.Resolve<Alpha>();
			container.Resolve<Alpha>();

			// Two transient instances, so the activation hook ran twice.
			await That(Probe.Log.Count(entry => entry == "activated:Alpha")).IsEqualTo(2);
		}

		// Each transient the container built is released with it.
		await That(Probe.Log.Count(entry => entry == "released:Alpha")).IsEqualTo(2);
	}

	[Fact]
	public async Task ScopedReleaseHook_RunsWhenTheScopeIsDisposed_NotTheRoot()
	{
		Probe.Log.Clear();
		using ScopedHookContainer.Root container = new();
		using (var scope = container.CreateScope())
		{
			scope.Resolve<Alpha>();
			await That(Probe.Log).DoesNotContain("released:Alpha");
		}

		// The scoped instance is released with its own scope, not withheld until the root is disposed.
		await That(Probe.Log).Contains("released:Alpha");
	}

	[Fact]
	public async Task OnActivated_Throwing_UnpublishesTheSingleton_SoResolveRetries_AndReleasesOnlyTheActivatedInstance()
	{
		Probe.Reset();
		using (ThrowingActivationContainer.Root container = new())
		{
			// The first activation throws; the resolve fails and must not leave a cached, half-activated instance.
			await That(() => container.Resolve<Flaky>()).Throws<InvalidOperationException>();

			// A later resolve rebuilds (rather than serving the un-activated instance from the fast path) and
			// activates - proving the throwing activation unpublished the cache field.
			Flaky rebuilt = container.Resolve<Flaky>();
			await That(rebuilt).IsNotNull();
			await That(Probe.Constructions).IsEqualTo(2);
			await That(Probe.Log).Contains("activated:Flaky");
		}

		// OnRelease ran exactly once - only for the successfully activated instance, not the one whose activation
		// threw (a release is queued only after activation succeeds).
		await That(Probe.Log.Count(entry => entry == "released:Flaky")).IsEqualTo(1);

		// The instance whose activation threw was still constructed, so it is disposed with the container rather
		// than leaked: two constructed instances, two disposals.
		await That(Probe.Log.Count(entry => entry == "disposed:Flaky")).IsEqualTo(2);
	}

	[Fact]
	public async Task TransientOnActivated_Throwing_DoesNotQueueARelease_ButTheConstructedInstanceIsStillDisposed()
	{
		Probe.Reset();
		using (ThrowingTransientContainer.Root container = new())
		{
			// A disposable transient is owned by the resolving scope (strict lifetime safety withholds it from the
			// root), so resolve it there; the scope's disposal then releases and disposes what it built.
			using (var scope = container.CreateScope())
			{
				// The first transient's activation throws; the resolve fails, but the instance was already
				// constructed and registered for disposal (registration precedes activation).
				await That(() => scope.Resolve<Flaky>()).Throws<InvalidOperationException>();

				// A transient is built fresh per call, so the next resolve activates successfully.
				scope.Resolve<Flaky>();
				await That(Probe.Log).Contains("activated:Flaky");
			}

			// OnRelease ran only for the successfully activated transient - a failed activation queues no release.
			await That(Probe.Log.Count(entry => entry == "released:Flaky")).IsEqualTo(1);

			// Both constructed transients are disposed (the failed one is tracked before activation), so neither
			// leaks.
			await That(Probe.Log.Count(entry => entry == "disposed:Flaky")).IsEqualTo(2);
		}
	}

	[Fact]
	public async Task AsyncOnActivated_Throwing_RetriesAndReleasesOnlyTheActivatedInstance()
	{
		Probe.Reset();
		using (ThrowingAsyncContainer.Root container = new())
		{
			// The first activation throws; the faulted task is evicted rather than caching a half-activated instance.
			Func<Task> resolve = () => container.ResolveAsync<AsyncFlaky>(TestContext.Current.CancellationToken);
			await That(resolve).Throws<InvalidOperationException>();

			// A later resolve rebuilds and activates successfully.
			AsyncFlaky rebuilt = await container.ResolveAsync<AsyncFlaky>(TestContext.Current.CancellationToken);
			await That(rebuilt).IsNotNull();
			await That(Probe.Log).Contains("activated:AsyncFlaky");
		}

		// OnRelease ran once - only for the successfully activated instance, not the one whose activation threw.
		await That(Probe.Log.Count(entry => entry == "released:AsyncFlaky")).IsEqualTo(1);
	}

	[Fact]
	public async Task AsyncHooks_ActivationRunsBeforeAsyncInitialization_AndReleaseRunsOnDisposal()
	{
		Probe.Reset();
		using (AsyncHookContainer.Root container = new())
		{
			await container.ResolveAsync<AsyncService>(TestContext.Current.CancellationToken);

			// OnActivated runs post-construction, before the instance drives its own asynchronous initialization.
			await That(Probe.Log.IndexOf("activated:AsyncService") < Probe.Log.IndexOf("initialized:AsyncService")).IsTrue();
		}

		await That(Probe.Log).Contains("released:AsyncService");
	}

	[Fact]
	public async Task DeferredMemberHooks_ActivationRunsAfterWiring_AndReleaseRunsOnDisposal()
	{
		Probe.Reset();
		using (DeferredHookContainer.Root container = new())
		{
			Widget widget = container.Resolve<Widget>();

			// The deferred member is wired and the activation hook ran once the instance was constructed (the
			// caching resolver's deferred wiring-episode branch).
			await That(widget.Gadget).IsNotNull();
			await That(Probe.Log).Contains("activated:Widget");
			await That(Probe.Log).DoesNotContain("released:Widget");
		}

		await That(Probe.Log).Contains("released:Widget");
	}

	[Fact]
	public async Task ConcurrentResolve_NeverObservesTheSingletonBeforeOnActivatedHasRun()
	{
		Probe.Reset();
		using ConcurrentActivationContainer.Root container = new();

		const int workers = 16;
		using Barrier gate = new(workers);
		bool[] sawActivated = new bool[workers];
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		Task[] tasks = new Task[workers];
		for (int i = 0; i < workers; i++)
		{
			int index = i;
			tasks[index] = Task.Run(
				() =>
				{
					// Release all workers at once so they race the lock-free fast path while the winner is still
					// inside the (deliberately slow) activation hook.
					gate.SignalAndWait(cancellationToken);
					Slow instance = container.Resolve<Slow>();
					sawActivated[index] = instance.Activated;
				},
				cancellationToken);
		}

		await Task.WhenAll(tasks);

		// Every concurrent caller received a fully-activated singleton: the lock-free fast path never handed out
		// the published-but-not-yet-activated instance. Publishing the cache field before activation would let a
		// racing reader observe Activated == false here.
		await That(sawActivated.All(seen => seen)).IsTrue()
			.Because("no caller may observe the singleton before its OnActivated hook has run");

		// The singleton was constructed and activated exactly once, regardless of the concurrent contention.
		await That(Probe.Constructions).IsEqualTo(1);
		await That(Probe.Log.Count(entry => entry == "activated:Slow")).IsEqualTo(1);
	}

	public sealed class Alpha;

	public sealed class Beta;

	public sealed class Tracked : IDisposable
	{
		public void Dispose() => Probe.Log.Add("disposed:Tracked");
	}

	public sealed class Flaky : IDisposable
	{
		public Flaky() => Probe.Constructions++;

		public void Dispose() => Probe.Log.Add("disposed:Flaky");
	}

	public sealed class AsyncService : IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Probe.Log.Add("initialized:AsyncService");
			return Task.CompletedTask;
		}
	}

	public sealed class AsyncFlaky : IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	public sealed class Gadget;

	public sealed class Widget
	{
		[Inject(Deferred = true)]
		public Gadget? Gadget { get; set; }
	}

	public sealed class Slow
	{
		public Slow() => Probe.Constructions++;

		// Volatile so a racing resolver thread cannot cache a stale read of the flag the activation hook sets.
		public volatile bool Activated;
	}

	private static class Probe
	{
		public static readonly List<string> Log = new();

		public static int Constructions;

		public static int Activations;

		public static void Reset()
		{
			Log.Clear();
			Constructions = 0;
			Activations = 0;
		}
	}

	[Container]
	[Singleton<Alpha>(OnActivated = nameof(Activated), OnRelease = nameof(Released))]
	[Singleton<Beta>(OnActivated = nameof(Activated), OnRelease = nameof(Released))]
	public static partial class HookContainer
	{
		private static void Activated(object instance) => Probe.Log.Add("activated:" + instance.GetType().Name);

		private static void Released(object instance) => Probe.Log.Add("released:" + instance.GetType().Name);
	}

	[Container]
	[Singleton<Tracked>(OnRelease = nameof(Release))]
	public static partial class DisposableHookContainer
	{
		private static void Release(Tracked tracked) => Probe.Log.Add("released:Tracked");
	}

	[Container]
	[Transient<Alpha>(OnActivated = nameof(Activated), OnRelease = nameof(Released))]
	public static partial class TransientHookContainer
	{
		private static void Activated(Alpha instance) => Probe.Log.Add("activated:Alpha");

		private static void Released(Alpha instance) => Probe.Log.Add("released:Alpha");
	}

	[Container]
	[Scoped<Alpha>(OnRelease = nameof(Released))]
	public static partial class ScopedHookContainer
	{
		private static void Released(Alpha instance) => Probe.Log.Add("released:Alpha");
	}

	[Container]
	[Singleton<Flaky>(OnActivated = nameof(Activate), OnRelease = nameof(Release))]
	public static partial class ThrowingActivationContainer
	{
		private static void Activate(Flaky flaky)
		{
			// Fail only the first activation, so a later resolve can prove the container rebuilds rather than
			// serving a cached, un-activated instance.
			if (++Probe.Activations == 1)
			{
				throw new InvalidOperationException("activation failed");
			}

			Probe.Log.Add("activated:Flaky");
		}

		private static void Release(Flaky flaky) => Probe.Log.Add("released:Flaky");
	}

	[Container]
	[Transient<Flaky>(OnActivated = nameof(Activate), OnRelease = nameof(Release))]
	public static partial class ThrowingTransientContainer
	{
		private static void Activate(Flaky flaky)
		{
			if (++Probe.Activations == 1)
			{
				throw new InvalidOperationException("activation failed");
			}

			Probe.Log.Add("activated:Flaky");
		}

		private static void Release(Flaky flaky) => Probe.Log.Add("released:Flaky");
	}

	[Container]
	[Singleton<AsyncFlaky>(OnActivated = nameof(Activate), OnRelease = nameof(Release))]
	public static partial class ThrowingAsyncContainer
	{
		private static void Activate(AsyncFlaky instance)
		{
			if (++Probe.Activations == 1)
			{
				throw new InvalidOperationException("activation failed");
			}

			Probe.Log.Add("activated:AsyncFlaky");
		}

		private static void Release(AsyncFlaky instance) => Probe.Log.Add("released:AsyncFlaky");
	}

	[Container]
	[Singleton<AsyncService>(OnActivated = nameof(Activated), OnRelease = nameof(Released))]
	public static partial class AsyncHookContainer
	{
		private static void Activated(AsyncService service) => Probe.Log.Add("activated:AsyncService");

		private static void Released(AsyncService service) => Probe.Log.Add("released:AsyncService");
	}

	[Container]
	[Singleton<Gadget>]
	[Singleton<Widget>(OnActivated = nameof(Activated), OnRelease = nameof(Released))]
	public static partial class DeferredHookContainer
	{
		private static void Activated(Widget instance) => Probe.Log.Add("activated:Widget");

		private static void Released(Widget instance) => Probe.Log.Add("released:Widget");
	}

	[Container]
	[Singleton<Slow>(OnActivated = nameof(Activate))]
	public static partial class ConcurrentActivationContainer
	{
		private static void Activate(Slow slow)
		{
			// Widen the window between publishing the cache field and finishing activation: if the field were
			// published first, a concurrent fast-path reader would observe this instance before Activated is set.
			Thread.Sleep(30);
			slow.Activated = true;
			Probe.Log.Add("activated:Slow");
		}
	}
}
