using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of collection dependencies. A constructor parameter typed as a collection of a service
///     (<see cref="IEnumerable{T}" />, <see cref="IReadOnlyList{T}" />, <see cref="IReadOnlyCollection{T}" />,
///     <see cref="IList{T}" />, <see cref="ICollection{T}" /> or <c>T[]</c>) resolves to every unkeyed
///     registration of that service, in registration order, each member keeping its own lifetime.
///     <see cref="IEnumerable{T}" /> and <c>T[]</c> are also publicly resolvable.
/// </summary>
public partial class CollectionTests
{
	[Fact]
	public async Task Enumerable_ResolvesEveryRegistrationInOrder()
	{
		using CollectionContainer.Root container = new();

		PluginHost host = container.Resolve<PluginHost>();

		await That(host.Plugins).HasCount(2);
		await That(host.Plugins[0].Name).IsEqualTo("alpha");
		await That(host.Plugins[1].Name).IsEqualTo("beta");
	}

	[Fact]
	public async Task Enumerable_IsPubliclyResolvable()
	{
		using CollectionContainer.Root container = new();

		string[] names = container.Resolve<IEnumerable<IPlugin>>().Select(p => p.Name).ToArray();

		await That(names).HasCount(2);
		await That(names[0]).IsEqualTo("alpha");
		await That(names[1]).IsEqualTo("beta");
	}

	[Fact]
	public async Task Array_IsPubliclyResolvable()
	{
		using CollectionContainer.Root container = new();

		IPlugin[] plugins = container.Resolve<IPlugin[]>();

		await That(plugins).HasCount(2);
	}

	[Fact]
	public async Task SingletonMembers_ShareTheInstanceWithSingleResolution()
	{
		using CollectionContainer.Root container = new();

		IPlugin[] plugins = container.Resolve<IPlugin[]>();

		// The single resolution returns the first (winning) registration; the collection holds the same
		// singleton instance, not a second copy.
		await That(plugins.First()).IsSameAs(container.Resolve<IPlugin>());
	}

	[Fact]
	public async Task EmptyCollection_ResolvesToAnEmptyCollection()
	{
		using CollectionContainer.Root container = new();

		// IExtension has no registration; the collection is empty rather than a missing-dependency error.
		ExtensionHost host = container.Resolve<ExtensionHost>();

		await That(host.Count).IsEqualTo(0);
	}

	[Fact]
	public async Task TransientMembers_AreFreshOnEachCollectionResolution()
	{
		using CollectionContainer.Root container = new();

		IReadOnlyList<IStep> first = container.Resolve<StepRunner>().Steps;
		IReadOnlyList<IStep> second = container.Resolve<Func<StepRunner>>()().Steps;

		// Each StepRunner gets its own freshly-built transient steps.
		await That(ReferenceEquals(first[0], second[0])).IsFalse();
	}

	[Fact]
	public async Task ScopedMembers_ResolveFromTheScope()
	{
		using CollectionContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();

		IReadOnlyList<IUnit> units = scope.Resolve<UnitHost>().Units;

		await That(units.Single()).IsSameAs(scope.Resolve<IUnit>());
	}

	[Fact]
	public async Task Strict_ResolvingACollectionOfDisposableTransientsFromTheRoot_ThrowsGuidance()
	{
		using DisposableCollectionContainer.Root container = new();

		await That(() => container.Resolve<IWidget[]>()).Throws<InvalidOperationException>()
			.Because("materializing a collection of disposable transients by type on the root would accumulate them for the container's lifetime, so it is withheld under strict lifetime safety - just like the singular resolution of such a member");
		await That(() => container.Resolve<IEnumerable<IWidget>>()).Throws<InvalidOperationException>()
			.Because("the IEnumerable<T> shape of the same collection is withheld on the root too");
	}

	[Fact]
	public async Task Strict_TryResolvingAWithheldCollectionFromTheRoot_ReturnsFalseRatherThanThrowing()
	{
		using DisposableCollectionContainer.Root container = new();

		bool resolved = container.TryResolve<IWidget[]>(out IWidget[]? widgets);

		await That(resolved).IsFalse()
			.Because("TryResolve is a non-throwing probe; the withheld collection reports false on the root rather than throwing the guidance");
		await That(widgets).IsNull();
	}

	[Fact]
	public async Task Strict_ResolvingAWithheldCollectionFromAScope_Works()
	{
		using DisposableCollectionContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();

		IWidget[] widgets = scope.Resolve<IWidget[]>();

		await That(widgets).HasCount(1)
			.Because("a child scope bounds the members' lifetime, so it resolves by type the collection the root withholds");
	}

	[Fact]
	public async Task Strict_AWithheldCollectionResolvedFromAScope_IsDisposedWithTheScope()
	{
		using DisposableCollectionContainer.Root container = new();

		DisposableWidget widget;
		using (IAwaitenScope scope = container.CreateScope())
		{
			widget = (DisposableWidget)scope.Resolve<IWidget[]>().Single();
			await That(widget.Disposed).IsFalse()
				.Because("the scope is still alive");
		}

		await That(widget.Disposed).IsTrue()
			.Because("the scope tracks the transient members it materialized and disposes them with the scope - the accumulation the root would suffer is bounded here");
	}

	[Fact]
	public async Task Loose_ResolvingACollectionOfDisposableTransientsFromTheRoot_Works()
	{
		using LooseCollectionContainer.Root container = new();

		IWidget[] widgets = container.Resolve<IWidget[]>();

		await That(widgets).HasCount(1)
			.Because("Loose lifetime safety keeps the collection resolvable by type on the root, like a singular disposable transient");
	}

	[Fact]
	public async Task ExplicitCollectionRegistration_WinsOverSynthesis()
	{
		using ExplicitCollectionContainer.Root container = new();

		BundleHost host = container.Resolve<BundleHost>();

		// IEnumerable<IPlugin> is registered as an opaque value (a Bundle), so both the injected parameter and the
		// public resolution return that registration, not the collection synthesized from the IPlugin members.
		await That(host.Plugins).HasCount(1);
		await That(host.Plugins[0].Name).IsEqualTo("bundle");
		await That(container.Resolve<IEnumerable<IPlugin>>().Single().Name).IsEqualTo("bundle");

		// All-or-nothing synthesis: because a collection shape of IPlugin (IEnumerable<IPlugin>) is explicitly
		// registered, no shape is synthesized. The unregistered IPlugin[] shape is unresolvable rather than a
		// silently synthesized second collection that would disagree with the registered one.
		await That(() => container.Resolve<IPlugin[]>()).Throws<InvalidOperationException>()
			.Because("registering one collection shape of IPlugin suppresses synthesis for every shape of IPlugin");
	}

	[Fact]
	public async Task AllCollectionShapes_ArePubliclyResolvable()
	{
		using CollectionContainer.Root container = new();

		// Every supported collection shape resolves the same two members (the eagerly materialized array
		// satisfies each), not only IEnumerable<T> and T[].
		await That(container.Resolve<IReadOnlyList<IPlugin>>()).HasCount(2);
		await That(container.Resolve<IReadOnlyCollection<IPlugin>>()).HasCount(2);
		await That(container.Resolve<IList<IPlugin>>()).HasCount(2);
		await That(container.Resolve<ICollection<IPlugin>>()).HasCount(2);
	}

	[Fact]
	public async Task FromKey_CollectionResolvesOnlyTheMembersUnderThatKey()
	{
		using KeyedCollectionContainer.Root container = new();

		KeyedPluginHost host = container.Resolve<KeyedPluginHost>();

		// Each [FromKey] collection resolves the registration(s) under that key, never the others. A key
		// identifies at most one registration per service type here (two would be AWT117), so each keyed
		// collection holds its single keyed member; the point is that the buckets stay disjoint.
		await That(host.Primary).HasCount(1);
		await That(host.Primary[0].Name).IsEqualTo("alpha");
		await That(host.Secondary).HasCount(1);
		await That(host.Secondary[0].Name).IsEqualTo("gamma");

		// The unkeyed collection resolves only the unkeyed registration; a keyed member is never an unkeyed one.
		await That(host.Unkeyed).HasCount(1);
		await That(host.Unkeyed[0].Name).IsEqualTo("plain");
	}

	[Fact]
	public async Task SyncResolveAfterInit_CollectionWithAnAsyncMember_ResolvesInitializedMembersAfterWarmUp()
	{
		using PragmaticCollectionContainer.Root container = new();

		await container.InitializeAsync(TestContext.Current.CancellationToken);

		// The collection holds an async-initialized member. In the strict default this is AWT122 (a synchronous
		// materialization cannot await the initialization); SyncResolveAfterInit warms the graph first, so the
		// collection materializes synchronously and hands back the already-initialized member, both when injected
		// through a host and when resolved publicly by type.
		AsyncPluginHost host = container.Resolve<AsyncPluginHost>();

		await That(host.Plugins).HasCount(2);
		await That(host.Plugins.OfType<AsyncPlugin>().Single().Initialized).IsTrue()
			.Because("InitializeAsync warmed the async member before the synchronously materialized collection handed it back");

		AsyncPlugin publiclyResolved = container.Resolve<IEnumerable<IPlugin>>().OfType<AsyncPlugin>().Single();

		await That(publiclyResolved.Initialized).IsTrue()
			.Because("the publicly resolved collection returns the same warmed singleton member");
	}

	[Fact]
	public async Task AsyncEnumerable_AwaitsMemberInitializationAndYieldsInRegistrationOrder()
	{
		using AsyncStreamContainer.Root container = new();

		// The host captures an async-tainted async-collection, so it is itself async-tainted and resolved through
		// ResolveAsync. In the strict default this is legal precisely because the collection is IAsyncEnumerable<T>
		// (a synchronous IEnumerable<T> of the same members would be AWT122).
		AsyncStreamHost host = await container.ResolveAsync<AsyncStreamHost>(TestContext.Current.CancellationToken);

		List<IPlugin> plugins = new();
		await foreach (IPlugin plugin in host.Plugins.WithCancellation(TestContext.Current.CancellationToken))
		{
			plugins.Add(plugin);
		}

		await That(plugins).HasCount(2);
		await That(plugins[0].Name).IsEqualTo("alpha");
		await That(plugins[1].Name).IsEqualTo("async");
		await That(plugins.OfType<AsyncPlugin>().Single().Initialized).IsTrue()
			.Because("materializing the IAsyncEnumerable<T> awaited the async member's initialization in registration order");
	}

	[Fact]
	public async Task AsyncEnumerable_AllSynchronousMembers_IsSynchronouslyResolvable()
	{
		using SyncAsyncStreamContainer.Root container = new();

		// Every member is synchronous, so the async collection is a synchronous expression and the host is not
		// async-tainted: it resolves synchronously, and iterating the stream yields the members in registration order.
		AsyncStreamHost host = container.Resolve<AsyncStreamHost>();

		List<IPlugin> plugins = new();
		await foreach (IPlugin plugin in host.Plugins.WithCancellation(TestContext.Current.CancellationToken))
		{
			plugins.Add(plugin);
		}

		await That(plugins).HasCount(2);
		await That(plugins[0].Name).IsEqualTo("alpha");
		await That(plugins[1].Name).IsEqualTo("beta");
	}

	[Fact]
	public async Task AsyncEnumerable_NoRegistrations_YieldsAnEmptyStream()
	{
		using EmptyAsyncStreamContainer.Root container = new();

		AsyncExtensionHost host = container.Resolve<AsyncExtensionHost>();

		int count = 0;
		await foreach (IExtension _ in host.Extensions.WithCancellation(TestContext.Current.CancellationToken))
		{
			count++;
		}

		await That(count).IsEqualTo(0)
			.Because("an element type with no registration yields an empty async collection, not a missing-dependency error");
	}

	[Fact]
	public async Task AsyncEnumerable_ScopedDisposableMember_ResolvesFromScopeAndIsDisposedWithIt()
	{
		using ScopedAsyncStreamContainer.Root container = new();

		DisposableScopedPlugin member;
		using (IAwaitenScope scope = container.CreateScope())
		{
			AsyncStreamHost host = scope.Resolve<AsyncStreamHost>();

			List<IPlugin> plugins = new();
			await foreach (IPlugin plugin in host.Plugins.WithCancellation(TestContext.Current.CancellationToken))
			{
				plugins.Add(plugin);
			}

			member = (DisposableScopedPlugin)plugins.Single();
			await That(member).IsSameAs(scope.Resolve<IPlugin>())
				.Because("the async collection materialized its member off the resolving scope");
			await That(member.Disposed).IsFalse()
				.Because("the scope is still alive");
		}

		await That(member.Disposed).IsTrue()
			.Because("the scope tracked the member the async collection materialized and disposed it with the scope");
	}

	[Fact]
	public async Task AsyncEnumerable_SynchronousMembers_IsResolvableByTypeSynchronously()
	{
		using SyncAsyncStreamContainer.Root container = new();

		// A collection whose members are all synchronous is publicly resolvable as IAsyncEnumerable<T> straight
		// through Resolve, wrapping the synchronously materialized members.
		IAsyncEnumerable<IPlugin> stream = container.Resolve<IAsyncEnumerable<IPlugin>>();

		List<IPlugin> plugins = new();
		await foreach (IPlugin plugin in stream.WithCancellation(TestContext.Current.CancellationToken))
		{
			plugins.Add(plugin);
		}

		await That(plugins).HasCount(2);
		await That(plugins[0].Name).IsEqualTo("alpha");
		await That(plugins[1].Name).IsEqualTo("beta");
	}

	[Fact]
	public async Task AsyncEnumerable_WithAnAsyncMember_IsResolvableByTypeThroughResolveAsync()
	{
		using AsyncStreamContainer.Root container = new();

		// The collection holds an async-initialized member, so its IAsyncEnumerable<T> shape is resolvable by type
		// only asynchronously: ResolveAsync materializes it, awaiting each member's initialization.
		IAsyncEnumerable<IPlugin> stream = await container.ResolveAsync<IAsyncEnumerable<IPlugin>>(TestContext.Current.CancellationToken);

		List<IPlugin> plugins = new();
		await foreach (IPlugin plugin in stream.WithCancellation(TestContext.Current.CancellationToken))
		{
			plugins.Add(plugin);
		}

		await That(plugins).HasCount(2);
		await That(plugins[0].Name).IsEqualTo("alpha");
		await That(plugins.OfType<AsyncPlugin>().Single().Initialized).IsTrue()
			.Because("ResolveAsync materialized the async collection, awaiting the async member's initialization");
	}

	[Fact]
	public async Task AsyncEnumerable_WithAnAsyncMember_SynchronousResolveThrowsGuidance()
	{
		using AsyncStreamContainer.Root container = new();

		await That(() => container.Resolve<IAsyncEnumerable<IPlugin>>()).Throws<InvalidOperationException>()
			.Because("the async collection awaits its members, so it has no synchronous materialization - synchronous Resolve steers to ResolveAsync");
	}

	[Fact]
	public async Task AsyncEnumerable_RootWithheldDisposableMember_ResolveAsyncFromTheRootThrowsGuidance()
	{
		using AsyncDisposableCollectionContainer.Root container = new();

		// The member is a build-on-demand disposable, so materializing the async collection by type on the root
		// would accumulate the transients for the container's lifetime, so it is withheld under strict lifetime
		// safety, mirroring the synchronous shapes and the singular resolution of such a member.
		Func<Task> resolveOnRoot = () => container.ResolveAsync<IAsyncEnumerable<IWidget>>(TestContext.Current.CancellationToken);
		await That(resolveOnRoot).Throws<InvalidOperationException>()
			.Because("the async collection of a build-on-demand disposable is withheld from by-type ResolveAsync on the root");
	}

	[Fact]
	public async Task AsyncEnumerable_RootWithheldDisposableMember_ResolveAsyncFromAScope_WorksAndIsDisposedWithTheScope()
	{
		using AsyncDisposableCollectionContainer.Root container = new();

		AsyncDisposableWidget member;
		using (IAwaitenScope scope = container.CreateScope())
		{
			IAsyncEnumerable<IWidget> stream = await scope.ResolveAsync<IAsyncEnumerable<IWidget>>(TestContext.Current.CancellationToken);

			List<IWidget> widgets = new();
			await foreach (IWidget widget in stream.WithCancellation(TestContext.Current.CancellationToken))
			{
				widgets.Add(widget);
			}

			member = (AsyncDisposableWidget)widgets.Single();
			await That(member.Disposed).IsFalse()
				.Because("the scope is still alive");
		}

		await That(member.Disposed).IsTrue()
			.Because("a child scope bounds the members the async collection materializes and disposes them with the scope - the accumulation the root would suffer is bounded here");
	}

	[Fact]
	public async Task FromKey_AsyncEnumerableResolvesOnlyTheMembersUnderThatKey()
	{
		using KeyedAsyncStreamContainer.Root container = new();

		KeyedAsyncStreamHost host = container.Resolve<KeyedAsyncStreamHost>();

		// Each [FromKey] async collection resolves the registration(s) under that key, never the others, and the
		// unkeyed async collection resolves only the unkeyed registration. The buckets stay disjoint, exactly as
		// for the synchronous shapes.
		List<IPlugin> primary = new();
		await foreach (IPlugin plugin in host.Primary.WithCancellation(TestContext.Current.CancellationToken))
		{
			primary.Add(plugin);
		}

		List<IPlugin> unkeyed = new();
		await foreach (IPlugin plugin in host.Unkeyed.WithCancellation(TestContext.Current.CancellationToken))
		{
			unkeyed.Add(plugin);
		}

		await That(primary).HasCount(1);
		await That(primary[0].Name).IsEqualTo("alpha");
		await That(unkeyed).HasCount(1);
		await That(unkeyed[0].Name).IsEqualTo("plain");
	}

	[Fact]
	public async Task SyncResolveAfterInit_AsyncEnumerable_IsSynchronouslyResolvableAfterWarmUp()
	{
		using PragmaticCollectionContainer.Root container = new();

		await container.InitializeAsync(TestContext.Current.CancellationToken);

		// In pragmatic mode every member has a synchronous resolver after warm-up, so the IAsyncEnumerable<T> view
		// joins the synchronous by-type dispatch instead of being served by an async arm.
		IAsyncEnumerable<IPlugin> stream = container.Resolve<IAsyncEnumerable<IPlugin>>();

		List<IPlugin> plugins = new();
		await foreach (IPlugin plugin in stream.WithCancellation(TestContext.Current.CancellationToken))
		{
			plugins.Add(plugin);
		}

		await That(plugins).HasCount(2);
		await That(plugins.OfType<AsyncPlugin>().Single().Initialized).IsTrue()
			.Because("InitializeAsync warmed the async member before the synchronously materialized stream handed it back");
	}

	[Fact]
	public async Task ExplicitlyRegisteredAsyncEnumerable_IsServedByTypeOnBothResolveAndResolveAsync()
	{
		using ExplicitAsyncStreamContainer.Root container = new();

		// IAsyncEnumerable<IPlugin> is itself registered (an opaque channel), so it owns the by-type slot on both
		// surfaces: no async collection is synthesized behind it from the (async-tainted) IPlugin registration, and
		// Resolve and ResolveAsync hand back the same registered singleton.
		PluginChannel channel = (PluginChannel)container.Resolve<IAsyncEnumerable<IPlugin>>();
		object viaAsync = await container.ResolveAsync<IAsyncEnumerable<IPlugin>>(TestContext.Current.CancellationToken);

		await That(viaAsync).IsSameAs(channel)
			.Because("both surfaces serve the explicitly registered channel, never a synthesized collection that would disagree with it");
	}

	[Fact]
	public async Task AsyncTaintedRegisteredAsyncEnumerable_SyncResolveThrowsItsGuidanceAndResolveAsyncServesIt()
	{
		using AsyncChannelContainer.Root container = new();

		// The registered channel requires asynchronous initialization, so it is absent from the synchronous
		// dispatch, and the async view synthesized from the (all-synchronous) IPlugin members must not claim its
		// vacated slot: synchronous Resolve throws the channel's steer-to-ResolveAsync guidance instead.
		await That(() => container.Resolve<IAsyncEnumerable<IPlugin>>()).Throws<InvalidOperationException>()
			.Because("the registered channel's slot is not shadowed by the synthesized async view");

		AsyncPluginChannel channel = (AsyncPluginChannel)await container.ResolveAsync<IAsyncEnumerable<IPlugin>>(TestContext.Current.CancellationToken);
		await That(channel.Initialized).IsTrue()
			.Because("ResolveAsync serves the registered channel, initialized");
	}

	[Fact]
	public async Task AwaitedCollection_AwaitsMemberInitializationWithoutTaintingTheConsumer()
	{
		using AwaitedStreamContainer.Root container = new();

		// The host injects Task<IReadOnlyList<IPlugin>> over an async-initialized member, yet resolves
		// SYNCHRONOUSLY in the strict default: the awaited collection launders its members' taint like the bare
		// Task<T> relationship. The await happens inside the produced task, not at the host's construction.
		AwaitedPluginHost host = container.Resolve<AwaitedPluginHost>();

		IReadOnlyList<IPlugin> plugins = await host.Plugins;

		await That(plugins).HasCount(2);
		await That(plugins[0].Name).IsEqualTo("alpha");
		await That(plugins[1].Name).IsEqualTo("async");
		await That(plugins.OfType<AsyncPlugin>().Single().Initialized).IsTrue()
			.Because("awaiting the collection awaited the async member's initialization in registration order");
	}

	[Fact]
	public async Task AwaitedCollection_InjectedIntoAProperty_AwaitsMemberInitializationWithoutTaintingTheConsumer()
	{
		using AwaitedPropertyContainer.Root container = new();

		// The awaited collection is filled through an [Inject] property (the object initializer run after
		// construction) rather than a constructor parameter, yet behaves identically: the host resolves
		// SYNCHRONOUSLY in the strict default because the awaited collection launders its members' taint, the
		// await happening inside the produced task rather than at the host's construction.
		AwaitedPropertyHost host = container.Resolve<AwaitedPropertyHost>();

		IReadOnlyList<IPlugin> plugins = await host.Plugins!;

		await That(plugins).HasCount(2);
		await That(plugins[0].Name).IsEqualTo("alpha");
		await That(plugins[1].Name).IsEqualTo("async");
		await That(plugins.OfType<AsyncPlugin>().Single().Initialized).IsTrue()
			.Because("awaiting the property-injected collection awaited the async member's initialization in registration order");
	}

	[Fact]
	public async Task AwaitedCollection_AllSynchronousMembers_IsACompletedTask()
	{
		using SyncAwaitedStreamContainer.Root container = new();

		AwaitedPluginHost host = container.Resolve<AwaitedPluginHost>();

		// Every member is synchronous, so the awaited collection is a completed Task.FromResult over the
		// synchronously materialized array, available without ever leaving the synchronous path.
		await That(host.Plugins.IsCompleted).IsTrue()
			.Because("an all-synchronous awaited collection carries no async machinery");

		IReadOnlyList<IPlugin> plugins = await host.Plugins;
		await That(plugins).HasCount(2);
		await That(plugins[0].Name).IsEqualTo("alpha");
		await That(plugins[1].Name).IsEqualTo("beta");
	}

	[Fact]
	public async Task AwaitedCollection_NoRegistrations_YieldsACompletedEmptyCollection()
	{
		using EmptyAwaitedStreamContainer.Root container = new();

		AwaitedExtensionHost host = container.Resolve<AwaitedExtensionHost>();

		IExtension[] extensions = await host.Extensions;
		await That(extensions).HasCount(0)
			.Because("an element type with no registration yields a completed empty awaited collection, not a missing-dependency error");
	}

	[Fact]
	public async Task AwaitedCollection_SingletonMembers_AreSharedAcrossResolutions()
	{
		using AwaitedStreamContainer.Root container = new();

		AwaitedPluginHost first = container.Resolve<AwaitedPluginHost>();
		AwaitedPluginHost second = container.Resolve<AwaitedPluginHost>();

		// The transient hosts each materialize their own awaited collection, but the singleton members inside
		// are shared. Each member keeps its own lifetime on the awaited path, exactly as on the synchronous one.
		await That((await second.Plugins)[0]).IsSameAs((await first.Plugins)[0]);
		await That((await second.Plugins)[1]).IsSameAs((await first.Plugins)[1]);
	}

	[Fact]
	public async Task FromKey_AwaitedCollectionResolvesOnlyTheMembersUnderThatKey()
	{
		using KeyedAwaitedStreamContainer.Root container = new();

		KeyedAwaitedPluginHost host = container.Resolve<KeyedAwaitedPluginHost>();

		// Each [FromKey] awaited collection resolves the registration(s) under that key, never the others, and
		// the unkeyed one only the unkeyed registration. The buckets stay disjoint, as for every other shape.
		IReadOnlyList<IPlugin> primary = await host.Primary;
		IReadOnlyList<IPlugin> unkeyed = await host.Unkeyed;

		await That(primary).HasCount(1);
		await That(primary[0].Name).IsEqualTo("alpha");
		await That(unkeyed).HasCount(1);
		await That(unkeyed[0].Name).IsEqualTo("plain");
	}

	[Fact]
	public async Task AwaitedCollection_AllShapesArePubliclyResolvableByType()
	{
		using SyncAwaitedStreamContainer.Root container = new();

		// The awaited collection joins the by-type dispatch alongside the synchronous shapes and IAsyncEnumerable<T>:
		// every Task<C> shape is resolvable straight through Resolve, handing back a completed task over the members.
		await That(await container.Resolve<Task<IEnumerable<IPlugin>>>()).HasCount(2);
		await That(await container.Resolve<Task<IReadOnlyList<IPlugin>>>()).HasCount(2);
		await That(await container.Resolve<Task<IReadOnlyCollection<IPlugin>>>()).HasCount(2);
		await That(await container.Resolve<Task<IList<IPlugin>>>()).HasCount(2);
		await That(await container.Resolve<Task<ICollection<IPlugin>>>()).HasCount(2);
		await That(await container.Resolve<Task<IPlugin[]>>()).HasCount(2);
	}

	[Fact]
	public async Task AwaitedCollection_WithAnAsyncMember_IsPubliclyResolvableByTypeSynchronously()
	{
		using AwaitedStreamContainer.Root container = new();

		// Unlike the synchronous shapes (AWT122) and IAsyncEnumerable<T> (async-only by type), the awaited collection
		// is obtainable through synchronous Resolve even with an async-tainted member: it hands back a Task that
		// awaits that member behind it. Awaiting the returned task initializes the async member in registration order.
		Task<IReadOnlyList<IPlugin>> task = container.Resolve<Task<IReadOnlyList<IPlugin>>>();

		IReadOnlyList<IPlugin> plugins = await task;
		await That(plugins).HasCount(2);
		await That(plugins[0].Name).IsEqualTo("alpha");
		await That(plugins.OfType<AsyncPlugin>().Single().Initialized).IsTrue()
			.Because("awaiting the by-type awaited collection awaited the async member's initialization");
	}

	[Fact]
	public async Task AwaitedCollection_ScopedDisposableMember_ResolvesFromScopeAndIsDisposedWithIt()
	{
		using ScopedAsyncStreamContainer.Root container = new();

		DisposableScopedPlugin member;
		using (IAwaitenScope scope = container.CreateScope())
		{
			IReadOnlyList<IPlugin> plugins = await scope.Resolve<Task<IReadOnlyList<IPlugin>>>();

			member = (DisposableScopedPlugin)plugins.Single();
			await That(member).IsSameAs(scope.Resolve<IPlugin>())
				.Because("the awaited collection materialized its member off the resolving scope");
			await That(member.Disposed).IsFalse()
				.Because("the scope is still alive");
		}

		await That(member.Disposed).IsTrue()
			.Because("the scope tracked the member the awaited collection materialized and disposed it with the scope");
	}

	[Fact]
	public async Task Strict_AwaitedCollectionOfDisposableTransients_IsWithheldFromTheRoot()
	{
		using DisposableCollectionContainer.Root container = new();

		await That(() => container.Resolve<Task<IWidget[]>>()).Throws<InvalidOperationException>()
			.Because("the awaited collection materializes its members eagerly, so on the root it would accumulate the disposable transients for the container's lifetime - withheld under strict lifetime safety, like the synchronous shapes");

		bool resolved = container.TryResolve<Task<IWidget[]>>(out Task<IWidget[]>? widgets);
		await That(resolved).IsFalse()
			.Because("TryResolve is a non-throwing probe; the withheld awaited collection reports false on the root");
		await That(widgets is null).IsTrue()
			.Because("a failed TryResolve leaves the awaited collection null");
	}

	[Fact]
	public async Task Strict_AwaitedCollectionOfDisposableTransients_ResolvedFromAScope_WorksAndIsDisposedWithTheScope()
	{
		using DisposableCollectionContainer.Root container = new();

		DisposableWidget widget;
		using (IAwaitenScope scope = container.CreateScope())
		{
			widget = (DisposableWidget)(await scope.Resolve<Task<IWidget[]>>()).Single();
			await That(widget.Disposed).IsFalse()
				.Because("the scope is still alive");
		}

		await That(widget.Disposed).IsTrue()
			.Because("a child scope bounds the members the awaited collection materializes and disposes them with the scope - the accumulation the root would suffer is bounded here");
	}

	[Fact]
	public async Task AwaitedCollection_BuiltOnTheAsyncPath_ForwardsTheResolveTimeTokenToItsAwaitedMembers()
	{
		using AsyncPathAwaitedContainer.Root container = new();
		using CancellationTokenSource cts = new();

		// The host is itself async-initialized, so it is built on the async path, where the awaited collection
		// forwards the resolve-time token to each awaited member (rather than the default a synchronously built
		// consumer supplies). The member captures the token it was initialized with.
		AsyncAwaitedHost host = await container.ResolveAsync<AsyncAwaitedHost>(cts.Token);
		IReadOnlyList<IPlugin> plugins = await host.Plugins;

		TokenCapturingPlugin member = plugins.OfType<TokenCapturingPlugin>().Single();
		await That(member.Received).IsEqualTo(cts.Token)
			.Because("an awaited collection built on the async path forwards the resolve-time token to its awaited members");
	}

	public interface IPlugin
	{
		string Name { get; }
	}

	public sealed class Alpha : IPlugin
	{
		public string Name => "alpha";
	}

	public sealed class Beta : IPlugin
	{
		public string Name => "beta";
	}

	public sealed class AsyncPlugin : IPlugin, IAsyncInitializable
	{
		public string Name => "async";

		public bool Initialized { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}
	}

	public sealed class AsyncPluginHost
	{
		public AsyncPluginHost(IEnumerable<IPlugin> plugins) => Plugins = plugins.ToArray();

		public IReadOnlyList<IPlugin> Plugins { get; }
	}

	public sealed class AsyncStreamHost
	{
		public AsyncStreamHost(IAsyncEnumerable<IPlugin> plugins) => Plugins = plugins;

		public IAsyncEnumerable<IPlugin> Plugins { get; }
	}

	public sealed class AsyncExtensionHost
	{
		public AsyncExtensionHost(IAsyncEnumerable<IExtension> extensions) => Extensions = extensions;

		public IAsyncEnumerable<IExtension> Extensions { get; }
	}

	public sealed class DisposableScopedPlugin : IPlugin, IDisposable
	{
		public string Name => "scoped";

		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public sealed class KeyedAsyncStreamHost
	{
		public KeyedAsyncStreamHost(
			[FromKey("primary")] IAsyncEnumerable<IPlugin> primary,
			IAsyncEnumerable<IPlugin> unkeyed)
		{
			Primary = primary;
			Unkeyed = unkeyed;
		}

		public IAsyncEnumerable<IPlugin> Primary { get; }

		public IAsyncEnumerable<IPlugin> Unkeyed { get; }
	}

	public sealed class AwaitedPluginHost
	{
		public AwaitedPluginHost(Task<IReadOnlyList<IPlugin>> plugins) => Plugins = plugins;

		public Task<IReadOnlyList<IPlugin>> Plugins { get; }
	}

	// The awaited-collection counterpart to AwaitedPluginHost, filled through an opt-in [Inject] property
	// instead of a constructor parameter.
	public sealed class AwaitedPropertyHost
	{
		[Inject]
		public Task<IReadOnlyList<IPlugin>>? Plugins { get; set; }
	}

	public sealed class AwaitedExtensionHost
	{
		public AwaitedExtensionHost(Task<IExtension[]> extensions) => Extensions = extensions;

		public Task<IExtension[]> Extensions { get; }
	}

	public sealed class KeyedAwaitedPluginHost
	{
		public KeyedAwaitedPluginHost(
			[FromKey("primary")] Task<IReadOnlyList<IPlugin>> primary,
			Task<IReadOnlyList<IPlugin>> unkeyed)
		{
			Primary = primary;
			Unkeyed = unkeyed;
		}

		public Task<IReadOnlyList<IPlugin>> Primary { get; }

		public Task<IReadOnlyList<IPlugin>> Unkeyed { get; }
	}

	// Captures the CancellationToken its initialization was handed, so a test can assert which token an awaited
	// collection forwarded to its awaited members.
	public sealed class TokenCapturingPlugin : IPlugin, IAsyncInitializable
	{
		public string Name => "token";

		public CancellationToken Received { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Received = cancellationToken;
			return Task.CompletedTask;
		}
	}

	// A host that is itself async-initialized (so built on the async path) and injects an awaited collection: the
	// awaited collection is therefore materialized on the async path, where it forwards the host's resolve-time token.
	public sealed class AsyncAwaitedHost : IAsyncInitializable
	{
		public AsyncAwaitedHost(Task<IReadOnlyList<IPlugin>> plugins) => Plugins = plugins;

		public Task<IReadOnlyList<IPlugin>> Plugins { get; }

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	// An opaque IAsyncEnumerable<IPlugin> service in its own right: the explicit registration the synthesized
	// async view must step aside for. The stream is empty (via a nested enumerator, so the service itself is not
	// IAsyncDisposable); the tests only assert which instance the container serves, never its contents.
	public sealed class PluginChannel : IAsyncEnumerable<IPlugin>
	{
		public IAsyncEnumerator<IPlugin> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new EmptyEnumerator();
	}

	// The channel that itself requires asynchronous initialization: excluded from the synchronous dispatch, so the
	// by-type slot it vacates there must throw its guidance rather than serve the synthesized view.
	public sealed class AsyncPluginChannel : IAsyncEnumerable<IPlugin>, IAsyncInitializable
	{
		public bool Initialized { get; private set; }

		public IAsyncEnumerator<IPlugin> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new EmptyEnumerator();

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}
	}

	private sealed class EmptyEnumerator : IAsyncEnumerator<IPlugin>
	{
		public IPlugin Current => null!;

		public ValueTask<bool> MoveNextAsync() => new(false);

		public ValueTask DisposeAsync() => default;
	}

	public sealed class PluginHost
	{
		public PluginHost(IEnumerable<IPlugin> plugins) => Plugins = plugins.ToArray();

		public IReadOnlyList<IPlugin> Plugins { get; }
	}

	public interface IExtension;

	public sealed class ExtensionHost
	{
		public ExtensionHost(IReadOnlyList<IExtension> extensions) => Count = extensions.Count;

		public int Count { get; }
	}

	public interface IStep;

	public sealed class StepA : IStep;

	public sealed class StepRunner
	{
		public StepRunner(IReadOnlyList<IStep> steps) => Steps = steps;

		public IReadOnlyList<IStep> Steps { get; }
	}

	public interface IUnit;

	public sealed class UnitA : IUnit;

	public sealed class UnitHost
	{
		public UnitHost(IReadOnlyList<IUnit> units) => Units = units;

		public IReadOnlyList<IUnit> Units { get; }
	}

	public interface IWidget;

	public sealed class DisposableWidget : IWidget, IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	// A disposable transient that is also async-initialized: as a collection member it makes the collection both
	// non-sync-materializable (so its IAsyncEnumerable<T> shape is served by the async arm) and root-withheld (a
	// build-on-demand disposable would accumulate on the root), exercising the async collection's root-withholding.
	public sealed class AsyncDisposableWidget : IWidget, IAsyncInitializable, IDisposable
	{
		public bool Disposed { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public void Dispose() => Disposed = true;
	}

	public sealed class Bundled : IPlugin
	{
		public string Name => "bundle";
	}

	// An opaque collection value: a concrete IEnumerable<IPlugin> registered as a whole, the way command-line
	// arguments (string[]) or a config list would be. It must win over the collection synthesized from the
	// individual IPlugin registrations.
	public sealed class PluginBundle : List<IPlugin>
	{
		public PluginBundle() => Add(new Bundled());
	}

	public sealed class BundleHost
	{
		public BundleHost(IEnumerable<IPlugin> plugins) => Plugins = plugins.ToArray();

		public IReadOnlyList<IPlugin> Plugins { get; }
	}

	public sealed class Gamma : IPlugin
	{
		public string Name => "gamma";
	}

	public sealed class Plain : IPlugin
	{
		public string Name => "plain";
	}

	public sealed class KeyedPluginHost
	{
		public KeyedPluginHost(
			[FromKey("primary")] IEnumerable<IPlugin> primary,
			[FromKey("secondary")] IReadOnlyList<IPlugin> secondary,
			IEnumerable<IPlugin> unkeyed)
		{
			Primary = primary.ToArray();
			Secondary = secondary;
			Unkeyed = unkeyed.ToArray();
		}

		public IReadOnlyList<IPlugin> Primary { get; }

		public IReadOnlyList<IPlugin> Secondary { get; }

		public IReadOnlyList<IPlugin> Unkeyed { get; }
	}

	[Container]
	[Singleton<Alpha, IPlugin>]
	[Singleton<Beta, IPlugin>]
	[Singleton<PluginHost>]
	[Singleton<ExtensionHost>]
	[Transient<StepA, IStep>]
	[Transient<StepRunner>]
	[Scoped<UnitA, IUnit>]
	[Scoped<UnitHost>]
	public static partial class CollectionContainer;

	// A collection holding an async-initialized member. Under the strict default injecting it is AWT122 (a
	// synchronous materialization has no place to await the member's initialization); SyncResolveAfterInit warms
	// the graph so the collection is synchronously materializable after InitializeAsync.
	[Container(SyncResolveAfterInit = true)]
	[Singleton<Alpha, IPlugin>]
	[Singleton<AsyncPlugin, IPlugin>]
	[Singleton<AsyncPluginHost>]
	public static partial class PragmaticCollectionContainer;

	// The strict default with an async-initialized collection member consumed as IAsyncEnumerable<T>: the async
	// shape awaits each member, so the async member is legal (no AWT122) and the host that captures it is
	// async-tainted, resolved through ResolveAsync.
	[Container]
	[Singleton<Alpha, IPlugin>]
	[Singleton<AsyncPlugin, IPlugin>]
	[Singleton<AsyncStreamHost>]
	public static partial class AsyncStreamContainer;

	// Every member is synchronous, so the async collection is a synchronous expression and its host stays
	// synchronously resolvable. The IAsyncEnumerable<T> shape does not by itself force the async path.
	[Container]
	[Singleton<Alpha, IPlugin>]
	[Singleton<Beta, IPlugin>]
	[Singleton<AsyncStreamHost>]
	public static partial class SyncAsyncStreamContainer;

	[Container]
	[Singleton<AsyncExtensionHost>]
	public static partial class EmptyAsyncStreamContainer;

	// A disposable scoped member consumed through an async collection: the member resolves off the scope and is
	// tracked for disposal there, so the async-collection materialization respects scoping and disposal.
	[Container]
	[Scoped<DisposableScopedPlugin, IPlugin>]
	[Scoped<AsyncStreamHost>]
	public static partial class ScopedAsyncStreamContainer;

	// A collection whose member is a disposable transient: materializing it by type on the root would
	// accumulate the transients for the container's lifetime, so under strict lifetime safety the collection is
	// withheld on the root (resolvable from a child scope, which bounds it). Loose keeps it root-resolvable.
	[Container]
	[Transient<DisposableWidget, IWidget>]
	public static partial class DisposableCollectionContainer;

	[Container(LifetimeSafety = LifetimeSafety.Loose)]
	[Transient<DisposableWidget, IWidget>]
	public static partial class LooseCollectionContainer;

	// The async analogue of DisposableCollectionContainer: the member is async-tainted, so the IAsyncEnumerable<T>
	// collection is served by the async by-type resolver, and it is a build-on-demand disposable, so that resolver
	// is withheld on the root (materializing it there would accumulate the transients for the container's lifetime).
	[Container]
	[Transient<AsyncDisposableWidget, IWidget>]
	public static partial class AsyncDisposableCollectionContainer;

	// Async collections under disjoint key buckets: 'primary' (alpha) and unkeyed (plain). A [FromKey] async
	// collection resolves exactly the members of its bucket, like the synchronous shapes.
	[Container]
	[Singleton<Alpha, IPlugin>(Key = "primary")]
	[Singleton<Plain, IPlugin>]
	[Singleton<KeyedAsyncStreamHost>]
	public static partial class KeyedAsyncStreamContainer;

	// IAsyncEnumerable<IPlugin> is registered in its own right (an opaque channel) alongside an async-tainted
	// IPlugin registration whose synthesized async view would otherwise claim the same by-type slot on the async
	// surface: the registration owns typeof(IAsyncEnumerable<IPlugin>) on both Resolve and ResolveAsync.
	[Container]
	[Singleton<AsyncPlugin, IPlugin>]
	[Singleton<PluginChannel, IAsyncEnumerable<IPlugin>>]
	public static partial class ExplicitAsyncStreamContainer;

	// The mirror image: the registered channel is itself async-initialized (absent from the synchronous dispatch)
	// and the IPlugin members are synchronous, so the synthesized view could otherwise shadow the channel's
	// vacated slot on the synchronous surface.
	[Container]
	[Singleton<Alpha, IPlugin>]
	[Singleton<AsyncPluginChannel, IAsyncEnumerable<IPlugin>>]
	public static partial class AsyncChannelContainer;

	// The strict default with an async-initialized member consumed as an awaited Task<IReadOnlyList<T>>: the
	// awaited collection awaits each member behind the produced task, so the async member is legal (no AWT122)
	// and, unlike the IAsyncEnumerable<T> shape, the members' taint is laundered, so the transient host stays
	// synchronously resolvable.
	[Container]
	[Singleton<Alpha, IPlugin>]
	[Singleton<AsyncPlugin, IPlugin>]
	[Transient<AwaitedPluginHost>]
	public static partial class AwaitedStreamContainer;

	// Same registrations as AwaitedStreamContainer, but the host receives the awaited collection through an
	// [Inject] property rather than a constructor parameter.
	[Container]
	[Singleton<Alpha, IPlugin>]
	[Singleton<AsyncPlugin, IPlugin>]
	[Transient<AwaitedPropertyHost>]
	public static partial class AwaitedPropertyContainer;

	// Every member is synchronous, so the awaited collection is a completed Task.FromResult.
	[Container]
	[Singleton<Alpha, IPlugin>]
	[Singleton<Beta, IPlugin>]
	[Singleton<AwaitedPluginHost>]
	public static partial class SyncAwaitedStreamContainer;

	[Container]
	[Singleton<AwaitedExtensionHost>]
	public static partial class EmptyAwaitedStreamContainer;

	// Awaited collections under disjoint key buckets: 'primary' (alpha) and unkeyed (plain).
	[Container]
	[Singleton<Alpha, IPlugin>(Key = "primary")]
	[Singleton<Plain, IPlugin>]
	[Singleton<KeyedAwaitedPluginHost>]
	public static partial class KeyedAwaitedStreamContainer;

	// The host is async-initialized (built on the async path) and injects an awaited collection whose member is
	// async-tainted: the awaited collection is materialized on the async path, forwarding the host's resolve-time
	// token to the awaited member, which captures it, so the test can assert the forwarding.
	[Container]
	[Singleton<TokenCapturingPlugin, IPlugin>]
	[Singleton<AsyncAwaitedHost>]
	public static partial class AsyncPathAwaitedContainer;

	// IEnumerable<IPlugin> is registered directly (an opaque value); the individual IPlugin registrations would
	// otherwise synthesize a collection of two, so the counts distinguish which one injection resolves to.
	[Container]
	[Singleton<Alpha, IPlugin>]
	[Singleton<Beta, IPlugin>]
	[Singleton<PluginBundle, IEnumerable<IPlugin>>]
	[Singleton<BundleHost>]
	public static partial class ExplicitCollectionContainer;

	// Registrations under three disjoint buckets: 'primary' (alpha), 'secondary' (gamma) and unkeyed (plain).
	// A [FromKey] collection resolves exactly the members of its bucket. A key identifies at most one
	// registration per service type (two would be AWT117), so each keyed collection holds its single member.
	[Container]
	[Singleton<Alpha, IPlugin>(Key = "primary")]
	[Singleton<Gamma, IPlugin>(Key = "secondary")]
	[Singleton<Plain, IPlugin>]
	[Singleton<KeyedPluginHost>]
	public static partial class KeyedCollectionContainer;
}
