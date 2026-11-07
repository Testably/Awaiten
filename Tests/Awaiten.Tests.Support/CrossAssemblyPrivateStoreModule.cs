using System;
using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>What happened to a bean store private to its module, which the module registers under an internal service.</summary>
public interface IPrivateStoreState
{
	/// <summary>Whether the module's activation hook ran on the store.</summary>
	bool Activated { get; }

	/// <summary>Whether the module's release hook ran on the store.</summary>
	bool Released { get; }

	/// <summary>Whether the module's async initializer convention ran on the store.</summary>
	bool Started { get; }

	/// <summary>Whether the container disposed the store.</summary>
	bool Disposed { get; }
}

/// <summary>A grinder that takes the private bean store through its internal service.</summary>
public interface IPrivateStoreGrinder
{
	/// <summary>The bean store the grinder was constructed with.</summary>
	IPrivateStoreState Store { get; }
}

internal interface IPrivateBeanStore : IPrivateStoreState;

internal interface IPrivateStoreStartable
{
	Task StartAsync();
}

internal sealed class PrivateStoreGrinder(IPrivateBeanStore store) : IPrivateStoreGrinder
{
	public IPrivateStoreState Store => store;
}

/// <summary>
///     A <c>[Module]</c> that registers a bean store private to it under an internal service, with hooks, an async
///     initializer convention and disposal. Its build emits no public member that names either type, so a container in
///     another assembly takes the store through its handle.
/// </summary>
[Module]
[AsyncInitializer<IPrivateStoreStartable>(nameof(IPrivateStoreStartable.StartAsync))]
[Singleton<CrossAssemblyPrivateStoreModule.BeanStore, IPrivateBeanStore>(OnActivated = nameof(Activate), OnRelease = nameof(Release))]
[Transient<PrivateStoreGrinder, IPrivateStoreGrinder>]
public static partial class CrossAssemblyPrivateStoreModule
{
	private static void Activate(BeanStore store) => store.Activated = true;

	private static void Release(BeanStore store) => store.Released = true;

	private sealed class BeanStore : IPrivateBeanStore, IPrivateStoreStartable, IDisposable
	{
		public bool Activated { get; set; }

		public bool Released { get; set; }

		public bool Started { get; private set; }

		public bool Disposed { get; private set; }

		public Task StartAsync()
		{
			Started = true;
			return Task.CompletedTask;
		}

		public void Dispose() => Disposed = true;
	}
}
