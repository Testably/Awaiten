using System.Collections.Generic;
using System.Linq;

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

	public sealed class Alpha;

	public sealed class Beta;

	public sealed class Tracked : IDisposable
	{
		public void Dispose() => Probe.Log.Add("disposed:Tracked");
	}

	private static class Probe
	{
		public static readonly List<string> Log = new();
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
}
