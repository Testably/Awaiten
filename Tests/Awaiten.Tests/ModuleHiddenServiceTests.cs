using System.Linq;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of services a <c>[Module]</c> registers under internal types. <see cref="CrossAssemblyHiddenServiceModule" />
///     lives in the referenced Awaiten.Tests.Support assembly, which grants this assembly no <c>InternalsVisibleTo</c>.
///     Its build emitted a public handle per internal service, so the containers here depend on the handles and still
///     share, initialize, hook and dispose each internal service like one they construct.
/// </summary>
public class ModuleHiddenServiceTests
{
	[Test]
	public async Task InternalSingleton_IsSharedByItsDependents()
	{
		using HiddenServiceContainer.Root container = new();

		IHiddenGrinder grinder = container.Resolve<IHiddenGrinder>();

		await That(grinder.Store.GetType().Name).IsEqualTo("HiddenBeanStore");
		await That(container.Resolve<IHiddenRoaster>().Store).IsSameAs(grinder.Store)
			.Because("the handle of an internal singleton is a singleton, so both dependents receive the one instance it holds");
	}

	[Test]
	public async Task InternalChain_IsBuiltThroughHandles()
	{
		using HiddenServiceContainer.Root container = new();

		await That(container.Resolve<IHiddenGrinder>().Silo.GetType().Name).IsEqualTo("HiddenSilo")
			.Because("the internal bean store takes the internal silo through its handle too");
	}

	[Test]
	public async Task InternalAndPublicService_ShareOneInstance()
	{
		using HiddenServiceContainer.Root container = new();

		await That(container.Resolve<IHiddenStock>()).IsSameAs(container.Resolve<IHiddenGrinder>().Store)
			.Because("the public service is an alias of the handle that owns the instance");
	}

	[Test]
	public async Task SeveralPublicServicesWithoutACoveringType_ShareOneInstance()
	{
		using HiddenServiceContainer.Root container = new();

		await That(container.Resolve<IHiddenFront>()).IsSameAs(container.Resolve<IHiddenBack>())
			.Because("one handle owns the instance and both services are aliases of it");
	}

	[Test]
	public async Task Hooks_RunThroughTheHandle()
	{
		IHiddenGrinder grinder;
		using (HiddenServiceContainer.Root container = new())
		{
			grinder = container.Resolve<IHiddenGrinder>();
			await That(grinder.StoreActivated).IsTrue()
				.Because("the module's internal activation hook runs on the instance the handle holds");
			await That(grinder.StoreReleased).IsFalse();
		}

		await That(grinder.StoreReleased).IsTrue()
			.Because("the module's internal release hook runs when the container is disposed");
		await That(grinder.StoreDisposed).IsTrue()
			.Because("the handle forwards disposal to the internal instance");
	}

	[Test]
	public async Task SuppressDisposal_LeavesTheInternalInstanceUndisposed()
	{
		IHiddenLogUser user;
		using (HiddenServiceContainer.Root container = new())
		{
			user = container.Resolve<IHiddenLogUser>();
		}

		await That(user.LogDisposed).IsFalse()
			.Because("the module suppresses the disposal of the internal log, which the handle registration keeps");
	}

	[Test]
	public async Task ScopedInternalService_IsPerScopeAndDisposedWithIt()
	{
		using HiddenServiceContainer.Root container = new();
		IHiddenCupHolder holder;
		using (IAwaitenScope scope = container.CreateScope())
		{
			holder = scope.Resolve<IHiddenCupHolder>();
			using IAwaitenScope other = container.CreateScope();
			await That(other.Resolve<IHiddenCupHolder>().Cup).IsNotSameAs(holder.Cup)
				.Because("each scope builds its own handle of the internal scoped cup");
		}

		await That(holder.CupDisposed).IsTrue()
			.Because("the scope disposes the handle, which disposes the cup it holds");
	}

	[Test]
	public async Task RelationshipTypes_ReachTheInternalServices()
	{
		using HiddenServiceContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();

		IHiddenBarista barista = scope.Resolve<IHiddenBarista>();
		object store = container.Resolve<IHiddenGrinder>().Store;

		await That(barista.StoreFromFunc()).IsSameAs(store);
		await That(barista.StoreFromLazy).IsSameAs(store)
			.Because("Func and Lazy of a handle resolve the singleton the handle holds");
		await That(barista.Filters).IsEqualTo(["paper", "metal",])
			.Because("a collection of a handle holds every implementation of the internal service in registration order");
		barista.UseOwnedCup(out bool disposedWithHandle);
		await That(disposedWithHandle).IsTrue()
			.Because("an owned handle keeps its scope, so disposing the projected Owned<T> disposes the internal cup");
	}

	[Test]
	public async Task ConventionOnAnInternalService_InitializesItBeforeItsDependents()
	{
		await using HiddenServiceContainer.Root container = new();

		IHiddenBakery bakery = await container.ResolveAsync<IHiddenBakery>();

		await That(bakery.OvenStarted).IsTrue()
			.Because("the module's internal convention runs on the instance the handle holds, through its generated wrapper");
	}

	[Test]
	public async Task ConventionOnAnInternalService_MakesItsDependentAsync()
	{
		await using HiddenServiceContainer.Root container = new();

		await That(() => container.Resolve<IHiddenBakery>()).Throws<InvalidOperationException>()
			.WithMessage("*ResolveAsync*").AsWildcard()
			.Because("the async initialization of the internal oven taints its dependents like a service the container sees");
	}

	[Test]
	public async Task AsyncHookOnAnInternalService_RunsBeforeItsDependents()
	{
		await using HiddenServiceContainer.Root container = new();

		IHiddenTeaMaker teaMaker = await container.ResolveAsync<IHiddenTeaMaker>();

		await That(teaMaker.KettleHeated).IsTrue();
	}

	[Test]
	public async Task AsyncOnlyDisposableInternalService_IsDisposedAsynchronously()
	{
		IHiddenVault vault;
		await using (HiddenAsyncDisposalContainer.Root container = new())
		{
			vault = container.Resolve<IHiddenVault>();
		}

		await That(vault.LockDisposed).IsTrue()
			.Because("the handle variant of an async-only disposable implements only IAsyncDisposable and forwards it");
	}

	[Test]
	public async Task AsyncOnlyDisposableInternalService_RejectsASynchronousDispose()
	{
		HiddenAsyncDisposalContainer.Root container = new();
		container.Resolve<IHiddenVault>();

#pragma warning disable AWT156
		await That(() => container.Dispose()).Throws<InvalidOperationException>()
#pragma warning restore AWT156
			.WithMessage("*DisposeAsync*").AsWildcard()
			.Because("the container knows the internal instance is only asynchronously disposable");
		await container.DisposeAsync();
	}

	[Test]
	public async Task Handles_AreNotResolvable()
	{
		using HiddenServiceContainer.Root container = new();
		Type handle = typeof(CrossAssemblyHiddenServiceModule.Internals).GetNestedTypes().First(type => type.Name.StartsWith("Hidden_HiddenBeanStore_", StringComparison.Ordinal));

		await That(() => container.Resolve(handle)).Throws<InvalidOperationException>()
			.Because("a handle is a dependency only, never offered for resolution");
		await That(((IAwaitenRoot)container).Registrations.Select(registration => registration.ServiceType)).DoesNotContain(handle)
			.Because("a handle is not advertised as a registration");
		await That(((IAwaitenRoot)container).Registrations.Select(registration => registration.ServiceType)).Contains(typeof(IHiddenGrinder));
	}

	[Test]
	public async Task EagerInternalSingleton_IsBuiltWithTheContainer()
	{
		int before = HiddenEagerCounter.Constructed;
		using HiddenServiceContainer.Root container = new();

		await That(HiddenEagerCounter.Constructed).IsGreaterThan(before)
			.Because("an eager internal singleton is built at startup through its handle, though nothing depends on it");
	}
}

[Container]
[Import(typeof(CrossAssemblyHiddenServiceModule))]
public static partial class HiddenServiceContainer;

[Container]
[Import(typeof(CrossAssemblyHiddenAsyncDisposalModule))]
public static partial class HiddenAsyncDisposalContainer;
