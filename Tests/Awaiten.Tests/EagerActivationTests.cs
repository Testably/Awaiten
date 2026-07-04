using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of eager singleton activation (<c>[Singleton&lt;T&gt;(Eager = true)]</c>): an eager
///     singleton is constructed in the generated container root's constructor - before any <c>Resolve</c> - in
///     registration order, it is cached (so a later resolve hands back the same instance), and a disposable
///     eager singleton is tracked for disposal by that eager construction alone (no resolve needed). In
///     pragmatic <c>SyncResolveAfterInit</c> mode an eager <c>IAsyncInitializable</c> singleton is even
///     constructed and initialized at build time through its blocking synchronous resolver. The containers and
///     services are nested types, so the enclosing class is <c>partial</c>. Construction, initialization and
///     disposal are observed through a static probe because the container is a static definition and the usable
///     instance is <c>new …Root()</c>.
/// </summary>
public partial class EagerActivationTests
{
	[Fact]
	public async Task EagerSingleton_IsConstructedBeforeAnyResolve()
	{
		Probe.Constructed.Clear();

		using EagerContainer.Root container = new();

		// Constructed by the root's constructor, before a single Resolve call.
		await That(Probe.Constructed).Contains("Eager");

		// The same instance is handed back on resolve (it was cached, not rebuilt).
		Eager first = container.Resolve<Eager>();
		await That(Probe.Constructed.Count(name => name == "Eager")).IsEqualTo(1)
			.Because("the eager singleton is constructed once and cached");
		await That(container.Resolve<Eager>()).IsSameAs(first);
	}

	[Fact]
	public async Task LazySingleton_IsNotConstructedUntilResolved()
	{
		Probe.Constructed.Clear();

		using MixedContainer.Root container = new();

		// Only the eager one ran in the constructor; the lazy one waits for a resolve.
		await That(Probe.Constructed).Contains("Eager");
		await That(Probe.Constructed).DoesNotContain("Lazy");

		container.Resolve<Lazy>();
		await That(Probe.Constructed).Contains("Lazy");
	}

	[Fact]
	public async Task EagerSingletons_AreConstructedInRegistrationOrder()
	{
		Probe.Constructed.Clear();

		using OrderedContainer.Root container = new();

		// First and Second are declared in that order, so they construct in that order.
		await That(Probe.Constructed.IndexOf("First") < Probe.Constructed.IndexOf("Second")).IsTrue();
	}

	[Fact]
	public async Task DisposableEagerSingleton_IsDisposedWithTheContainer()
	{
		Disposable instance;
		using (DisposableContainer.Root container = new())
		{
			instance = container.Resolve<Disposable>();
			await That(instance.Disposed).IsFalse();
		}

		await That(instance.Disposed).IsTrue();
	}

	[Fact]
	public async Task EagerDisposableSingleton_IsTrackedForDisposalByConstructionAlone()
	{
		Probe.Constructed.Clear();
		Probe.Disposed.Clear();

		using (ProbeDisposableContainer.Root container = new())
		{
			// Constructed eagerly, and tracked for disposal by that eager construction alone - no Resolve here.
			await That(Probe.Constructed).Contains("ProbeDisposable");
			await That(Probe.Disposed).DoesNotContain("ProbeDisposable");
		}

		// Disposing the container disposes the eagerly-constructed singleton, so eager construction (not a
		// resolve) is what registered it for teardown.
		await That(Probe.Disposed).Contains("ProbeDisposable");
	}

	[Fact]
	public async Task PragmaticMode_EagerAsyncSingleton_IsConstructedAndInitializedBeforeAnyResolve()
	{
		Probe.Constructed.Clear();

		using PragmaticEagerContainer.Root container = new();

		// The blocking synchronous resolver ran in the constructor, so the async-initialized singleton was both
		// constructed and initialized at build time - before any Resolve / InitializeAsync.
		await That(Probe.Constructed).Contains("AsyncEager");
		await That(Probe.Constructed).Contains("AsyncEager:init");

		// A later synchronous resolve (allowed by SyncResolveAfterInit) hands back that same, already-initialized
		// instance - constructed exactly once.
		AsyncEager resolved = container.Resolve<AsyncEager>();
		await That(resolved.Initialized).IsTrue();
		await That(Probe.Constructed.Count(name => name == "AsyncEager")).IsEqualTo(1)
			.Because("the eager async singleton is constructed once at build time and cached");
	}

	private static class Probe
	{
		public static readonly List<string> Constructed = new();

		public static readonly List<string> Disposed = new();
	}

	public sealed class Eager
	{
		public Eager() => Probe.Constructed.Add("Eager");
	}

	public sealed class Lazy
	{
		public Lazy() => Probe.Constructed.Add("Lazy");
	}

	public sealed class First
	{
		public First() => Probe.Constructed.Add("First");
	}

	public sealed class Second
	{
		public Second() => Probe.Constructed.Add("Second");
	}

	public sealed class Disposable : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public sealed class ProbeDisposable : IDisposable
	{
		public ProbeDisposable() => Probe.Constructed.Add("ProbeDisposable");

		public void Dispose() => Probe.Disposed.Add("ProbeDisposable");
	}

	public sealed class AsyncEager : IAsyncInitializable
	{
		public AsyncEager() => Probe.Constructed.Add("AsyncEager");

		public bool Initialized { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			Probe.Constructed.Add("AsyncEager:init");
			return Task.CompletedTask;
		}
	}

	[Container]
	[Singleton<Eager>(Eager = true)]
	public static partial class EagerContainer;

	[Container]
	[Singleton<Eager>(Eager = true)]
	[Singleton<Lazy>]
	public static partial class MixedContainer;

	[Container]
	[Singleton<First>(Eager = true)]
	[Singleton<Second>(Eager = true)]
	public static partial class OrderedContainer;

	[Container]
	[Singleton<Disposable>(Eager = true)]
	public static partial class DisposableContainer;

	[Container]
	[Singleton<ProbeDisposable>(Eager = true)]
	public static partial class ProbeDisposableContainer;

	[Container(SyncResolveAfterInit = true)]
	[Singleton<AsyncEager>(Eager = true)]
	public static partial class PragmaticEagerContainer;
}
