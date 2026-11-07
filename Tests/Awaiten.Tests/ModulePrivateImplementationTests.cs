using Awaiten.Tests.ContainerSupport;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of an implementation private to its <c>[Module]</c> that the module registers under an internal
///     service. A container in another assembly takes it through its handle, a container in the module's own assembly or
///     in one granted <c>InternalsVisibleTo</c> through the module's internal factory, and each shares, hooks,
///     initializes and disposes it alike.
/// </summary>
public partial class ModulePrivateImplementationTests
{
	[Test]
	public async Task ContainerInAnotherAssembly_SharesHooksInitializesAndDisposesIt()
	{
		IPrivateStoreState store;
		await using (PrivateStoreContainer.Root container = new())
		{
			store = (await container.ResolveAsync<IPrivateStoreGrinder>()).Store;

			await That((await container.ResolveAsync<IPrivateStoreGrinder>()).Store).IsSameAs(store)
				.Because("the singleton is one instance, which its handle holds");
			await That(store.Activated && store.Started).IsTrue()
				.Because("the module's private hook and its convention run on the instance the handle holds");
			await That(store.Released || store.Disposed).IsFalse();
		}

		await That(store.Released && store.Disposed).IsTrue()
			.Because("the container releases the singleton and the handle forwards disposal to it");
	}

	[Test]
	public async Task ContainerInAnAssemblyGrantedInternalsVisibleTo_SharesHooksInitializesAndDisposesIt()
	{
		IFriendPrivateStoreState store;
		await using (FriendPrivateStoreContainer.Root container = new())
		{
			store = (await container.ResolveAsync<IFriendPrivateStoreGrinder>()).Store;

			await That((await container.ResolveAsync<IFriendPrivateStoreGrinder>()).Store).IsSameAs(store)
				.Because("the singleton the internal factory builds is one instance");
			await That(store.Activated && store.Started).IsTrue()
				.Because("the container runs the module's private hook through its internal wrapper and the convention itself");
			await That(store.Released || store.Disposed).IsFalse();
		}

		await That(store.Released && store.Disposed).IsTrue()
			.Because("the container releases and disposes the singleton it built");
	}

	[Test]
	public async Task ContainerInTheModulesAssembly_SharesHooksInitializesAndDisposesIt()
	{
		IFriendPrivateStoreState store;
		await using (FriendPrivateStoreLocalContainer.Root container = new())
		{
			store = (await container.ResolveAsync<IFriendPrivateStoreGrinder>()).Store;

			await That((await container.ResolveAsync<IFriendPrivateStoreGrinder>()).Store).IsSameAs(store)
				.Because("the singleton the internal factory builds is one instance");
			await That(store.Activated && store.Started).IsTrue();
			await That(store.Released || store.Disposed).IsFalse();
		}

		await That(store.Released && store.Disposed).IsTrue()
			.Because("the container releases and disposes the singleton it built");
	}

	[Container]
	[Import(typeof(CrossAssemblyPrivateStoreModule))]
	public static partial class PrivateStoreContainer;

	[Container]
	[Import(typeof(SupportFriendPrivateStoreModule))]
	public static partial class FriendPrivateStoreContainer;
}
