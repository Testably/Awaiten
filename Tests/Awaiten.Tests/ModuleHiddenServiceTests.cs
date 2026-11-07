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
			.WithMessage("*'Awaiten.Tests.Support.HiddenLock' requires asynchronous disposal*DisposeAsync*").AsWildcard()
			.Because("the container knows the internal instance is only asynchronously disposable, and the guidance names the internal implementation instead of its handle");
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
	public async Task FactoryMember_BuildsTheInternalServiceAndTheContainerDisposesIt()
	{
		IHiddenWeigher weigher;
		using (HiddenServiceContainer.Root container = new())
		{
			weigher = container.Resolve<IHiddenWeigher>();
			await That(weigher.ScaleSilo).IsSameAs(container.Resolve<IHiddenGrinder>().Silo)
				.Because("the factory member takes the internal silo singleton through its handle");
			await That(weigher.ScaleDisposed).IsFalse();
		}

		await That(weigher.ScaleDisposed).IsTrue()
			.Because("the handle picks the disposable variant from the subtype the factory returned, so the container disposes it");
	}

	[Test]
	public async Task InstanceMember_IsInjectedAndNotDisposed()
	{
		IHiddenWeigher weigher;
		using (HiddenServiceContainer.Root container = new())
		{
			weigher = container.Resolve<IHiddenWeigher>();
			await That(weigher.RecipeBook).IsSameAs(HiddenRecipeBookAccess.RecipeBook);
		}

		await That(weigher.RecipeBookDisposed).IsFalse()
			.Because("a container never owns a pre-built instance, also not through a handle");
	}

	[Test]
	public async Task AsyncFactoryMember_IsAwaitedBeforeItsDependents()
	{
		await using HiddenServiceContainer.Root container = new();

		IHiddenGaugeReader reader = await container.ResolveAsync<IHiddenGaugeReader>();

		await That(reader.GaugeCalibrated).IsTrue();
		await That(() => container.Resolve<IHiddenGaugeReader>()).Throws<InvalidOperationException>()
			.Because("an async factory member taints its dependents like any async factory");
	}

	[Test]
	public async Task DecoratorsOfAnInternalService_WrapItInOrderAndAreDisposedLinkByLink()
	{
		IHiddenBookkeeper bookkeeper;
		await using (HiddenDecoratorContainer.Root container = new())
		{
			bookkeeper = container.Resolve<IHiddenBookkeeper>();
			await That(bookkeeper.Chain).IsEqualTo("counting(audited(ledger))")
				.Because("each decorator wraps the handle of the link before it, in declaration order");
			await That(bookkeeper.Disposed).IsEqualTo([false, false, false,]);
		}

		await That(bookkeeper.Disposed).IsEqualTo([true, true, true,])
			.Because("every link is an instance of its own that the container disposes as its implementation needs");
	}

	[Test]
	public async Task DecoratorOfAnInternalService_LeavesAPublicServiceOfTheImplementationUndecorated()
	{
		using HiddenDecoratedAliasContainer.Root container = new();

		IHiddenJournalReader reader = container.Resolve<IHiddenJournalReader>();

		await That(reader.Journal.GetType().Name).IsEqualTo("TracedJournal");
		await That(reader.Inner).IsSameAs(container.Resolve<IHiddenJournalView>())
			.Because("the decorator wraps the one instance, which the public service serves undecorated, as in a container of the module's own assembly");
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

[Container]
[Import(typeof(CrossAssemblyHiddenDecoratorModule))]
public static partial class HiddenDecoratorContainer;

[Container]
[Import(typeof(CrossAssemblyHiddenDecoratedAliasModule))]
public static partial class HiddenDecoratedAliasContainer;
