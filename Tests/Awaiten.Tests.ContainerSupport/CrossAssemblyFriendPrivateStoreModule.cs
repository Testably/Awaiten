using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Awaiten.Tests.ContainerSupport;

/// <summary>What happened to a bean store private to its module, which the module registers under an internal service.</summary>
public interface IFriendPrivateStoreState
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
public interface IFriendPrivateStoreGrinder
{
	/// <summary>The bean store the grinder was constructed with.</summary>
	IFriendPrivateStoreState Store { get; }
}

internal interface IFriendPrivateBeanStore : IFriendPrivateStoreState;

internal interface IFriendPrivateStoreStartable
{
	Task StartAsync();
}

internal sealed class FriendPrivateStoreGrinder(IFriendPrivateBeanStore store) : IFriendPrivateStoreGrinder
{
	public IFriendPrivateStoreState Store => store;
}

/// <summary>
///     A <c>[Module]</c> that registers a bean store private to it under an internal service, with hooks, an async
///     initializer convention and disposal. This assembly grants Awaiten.Tests <c>InternalsVisibleTo</c>, so a container
///     there, like one in this assembly, builds the store through the module's internal factory.
/// </summary>
[Module]
[AsyncInitializer<IFriendPrivateStoreStartable>(nameof(IFriendPrivateStoreStartable.StartAsync))]
[Singleton<SupportFriendPrivateStoreModule.BeanStore, IFriendPrivateBeanStore>(OnActivated = nameof(Activate), OnRelease = nameof(Release))]
[Transient<FriendPrivateStoreGrinder, IFriendPrivateStoreGrinder>]
public static partial class SupportFriendPrivateStoreModule
{
	private static void Activate(BeanStore store) => store.Activated = true;

	private static void Release(BeanStore store) => store.Released = true;

	private sealed class BeanStore : IFriendPrivateBeanStore, IFriendPrivateStoreStartable, IDisposable
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

/// <summary>A container in the module's own assembly that imports <see cref="SupportFriendPrivateStoreModule" />.</summary>
[Container]
[Import(typeof(SupportFriendPrivateStoreModule))]
public static partial class FriendPrivateStoreLocalContainer;

/// <summary>A shelf of internal bean stores that a composite private to its module fronts.</summary>
public interface IFriendShelfState
{
	/// <summary>How many stores the shelf fronts.</summary>
	int Count { get; }

	/// <summary>Whether the container disposed the shelf.</summary>
	bool Disposed { get; }
}

/// <summary>A barista that takes the internal shelf store, which the private composite fronts.</summary>
public interface IFriendShelfBarista
{
	/// <summary>The shelf store the barista was constructed with.</summary>
	IFriendShelfState Shelf { get; }
}

internal interface IFriendShelfStore : IFriendShelfState;

internal sealed class FriendFirstShelfStore : IFriendShelfStore
{
	public int Count => 1;

	public bool Disposed => false;
}

internal sealed class FriendSecondShelfStore : IFriendShelfStore
{
	public int Count => 1;

	public bool Disposed => false;
}

internal sealed class FriendShelfBarista(IFriendShelfStore shelf) : IFriendShelfBarista
{
	public IFriendShelfState Shelf => shelf;
}

/// <summary>
///     A <c>[Module]</c> with a disposable composite private to it over an internal service. A container in this
///     assembly, or in Awaiten.Tests, which this assembly grants <c>InternalsVisibleTo</c>, builds it through the
///     module's internal factory.
/// </summary>
[Module]
[Singleton<FriendFirstShelfStore, IFriendShelfStore>]
[Singleton<FriendSecondShelfStore, IFriendShelfStore>]
[Singleton<FriendShelfBarista, IFriendShelfBarista>]
[Composite<SupportFriendPrivateShelfModule.AllStores, IFriendShelfStore>(Lifetime = Lifetime.Singleton)]
public static partial class SupportFriendPrivateShelfModule
{
	private sealed class AllStores(IEnumerable<IFriendShelfStore> stores) : IFriendShelfStore, IDisposable
	{
		public int Count => stores.Sum(store => store.Count);

		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}
}

/// <summary>A container in the module's own assembly that imports <see cref="SupportFriendPrivateShelfModule" />.</summary>
[Container]
[Import(typeof(SupportFriendPrivateShelfModule))]
public static partial class FriendPrivateShelfLocalContainer;
