using System.Collections.Generic;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of keyed-dictionary injection: a constructor parameter or injected <c>[Inject]</c>
///     property typed <c>IReadOnlyDictionary&lt;string, TService&gt;</c> resolves to every <em>keyed</em>
///     registration of that service, keyed by each registration's <c>[Key]</c>, each respecting its own
///     lifetime. The dictionary is always satisfiable (an empty index yields an empty dictionary) and is also
///     publicly resolvable by type. The containers and services are nested types, so the enclosing class is
///     <c>partial</c>.
/// </summary>
public partial class KeyedDictionaryTests
{
	[Fact]
	public async Task Dictionary_ResolvesEveryKeyedRegistrationByItsKey()
	{
		using DictionaryContainer.Root container = new();

		ChannelRouter router = container.Resolve<ChannelRouter>();

		await That(router.Channels).HasCount(2);
		await That(router.Channels["fast"]).Is<FastChannel>()
			.Because("the 'fast' key resolves the implementation registered under it");
		await That(router.Channels["slow"]).Is<SlowChannel>()
			.Because("the 'slow' key resolves the implementation registered under it");
	}

	[Fact]
	public async Task Dictionary_IsPubliclyResolvableByType()
	{
		using DictionaryContainer.Root container = new();

		IReadOnlyDictionary<string, IChannel> channels = container.Resolve<IReadOnlyDictionary<string, IChannel>>();

		await That(channels).HasCount(2);
		await That(channels["fast"]).Is<FastChannel>();
		await That(channels["slow"]).Is<SlowChannel>();
	}

	[Fact]
	public async Task Dictionary_ExcludesUnkeyedRegistrations()
	{
		using DictionaryContainer.Root container = new();

		IReadOnlyDictionary<string, IChannel> channels = container.Resolve<IReadOnlyDictionary<string, IChannel>>();

		// PlainChannel is registered unkeyed, so it wins the unkeyed single resolution but is NOT a member of the
		// keyed dictionary - the dictionary is every keyed registration, never the unkeyed ones.
		await That(container.Resolve<IChannel>()).Is<PlainChannel>()
			.Because("the unkeyed registration is the unkeyed single-resolution winner");

		bool anyPlain = false;
		foreach (IChannel channel in channels.Values)
		{
			anyPlain |= channel is PlainChannel;
		}

		await That(anyPlain).IsFalse()
			.Because("an unkeyed registration is not a keyed-dictionary member");
	}

	[Fact]
	public async Task SingletonMembers_ShareTheInstanceAcrossResolutions()
	{
		using DictionaryContainer.Root container = new();

		// Each resolution materializes its own dictionary, but the singleton members inside are the same instance.
		IChannel first = container.Resolve<IReadOnlyDictionary<string, IChannel>>()["fast"];
		IChannel second = container.Resolve<IReadOnlyDictionary<string, IChannel>>()["fast"];

		await That(first).IsSameAs(second)
			.Because("a singleton keyed member is shared across every dictionary that includes it");
	}

	[Fact]
	public async Task TransientMembers_AreFreshOnEachDictionaryResolution()
	{
		using DictionaryContainer.Root container = new();

		IReadOnlyDictionary<string, IJob> first = container.Resolve<JobRunner>().Jobs;
		IReadOnlyDictionary<string, IJob> second = container.Resolve<Func<JobRunner>>()().Jobs;

		// Each JobRunner materializes its own dictionary of freshly-built transient jobs.
		await That(first["import"]).IsNotSameAs(second["import"])
			.Because("a transient keyed member is built fresh for each dictionary");
	}

	[Fact]
	public async Task EmptyDictionary_ResolvesToAnEmptyDictionary()
	{
		using DictionaryContainer.Root container = new();

		// IGadget has no keyed registration; the dictionary is empty rather than a missing-dependency error.
		GadgetHost host = container.Resolve<GadgetHost>();

		await That(host.Count).IsEqualTo(0)
			.Because("a service with no keyed registration yields an empty dictionary, not AWT101");
	}

	[Fact]
	public async Task Dictionary_InjectedIntoAProperty_ResolvesEveryKeyedRegistration()
	{
		using DictionaryContainer.Root container = new();

		PropertyRouter router = container.Resolve<PropertyRouter>();

		await That(router.Channels!).HasCount(2);
		await That(router.Channels!["fast"]).Is<FastChannel>()
			.Because("an [Inject] property typed as a keyed dictionary is filled exactly like a constructor parameter");
	}

	[Fact]
	public async Task Dictionary_ResolvedFromAScope_ResolvesScopedMembersOnThatScope()
	{
		using DictionaryContainer.Root container = new();
		using IAwaitenScope scope1 = container.CreateScope();
		using IAwaitenScope scope2 = container.CreateScope();

		// A scoped member is shared within the scope that materializes the dictionary, and distinct across scopes.
		IWork first = scope1.Resolve<IReadOnlyDictionary<string, IWork>>()["main"];
		IWork again = scope1.Resolve<IReadOnlyDictionary<string, IWork>>()["main"];
		IWork other = scope2.Resolve<IReadOnlyDictionary<string, IWork>>()["main"];

		await That(first).IsSameAs(again)
			.Because("a scoped keyed member is shared within its scope");
		await That(first).IsNotSameAs(other)
			.Because("a different scope materializes its own scoped keyed member");
	}

	[Fact]
	public async Task Strict_KeyedDictionaryOfDisposableTransients_IsWithheldFromTheRootButResolvesFromAScope()
	{
		using WidgetContainer.Root container = new();

		// Materializing the dictionary by type off the Root would accumulate its disposable transient members for
		// the container's lifetime, so under strict lifetime safety it is withheld there, exactly like a collection.
		await That(() => container.Resolve<IReadOnlyDictionary<string, IWidget>>()).Throws<InvalidOperationException>()
			.Because("a keyed dictionary with a build-on-demand disposable member is root-withheld under strict safety");

		// A child scope bounds those disposables, so it resolves there.
		using IAwaitenScope scope = container.CreateScope();
		await That(scope.Resolve<IReadOnlyDictionary<string, IWidget>>()).HasCount(1)
			.Because("a child scope bounds the disposable members the dictionary materializes");
	}

	public interface IChannel;

	public sealed class FastChannel : IChannel;

	public sealed class SlowChannel : IChannel;

	public sealed class PlainChannel : IChannel;

	public sealed class ChannelRouter
	{
		public ChannelRouter(IReadOnlyDictionary<string, IChannel> channels) => Channels = channels;

		public IReadOnlyDictionary<string, IChannel> Channels { get; }
	}

	public sealed class PropertyRouter
	{
		[Inject]
		public IReadOnlyDictionary<string, IChannel>? Channels { get; set; }
	}

	public interface IJob;

	public sealed class ImportJob : IJob;

	public sealed class JobRunner
	{
		public JobRunner(IReadOnlyDictionary<string, IJob> jobs) => Jobs = jobs;

		public IReadOnlyDictionary<string, IJob> Jobs { get; }
	}

	public interface IWork;

	public sealed class MainWork : IWork;

	public interface IGadget;

	public sealed class GadgetHost
	{
		public GadgetHost(IReadOnlyDictionary<string, IGadget> gadgets) => Count = gadgets.Count;

		public int Count { get; }
	}

	[Container]
	[Singleton<FastChannel, IChannel>(Key = "fast")]
	[Singleton<SlowChannel, IChannel>(Key = "slow")]
	[Singleton<PlainChannel, IChannel>]
	[Singleton<ChannelRouter>]
	[Singleton<PropertyRouter>]
	[Transient<ImportJob, IJob>(Key = "import")]
	[Transient<JobRunner>]
	[Scoped<MainWork, IWork>(Key = "main")]
	[Singleton<GadgetHost>]
	public static partial class DictionaryContainer;

	[Fact]
	public async Task SameImplementationUnderTwoKeys_SharesTheSingletonAcrossKeys()
	{
		using TwinContainer.Root container = new();

		IReadOnlyDictionary<string, ITwin> twins = container.Resolve<IReadOnlyDictionary<string, ITwin>>();

		await That(twins).HasCount(2);
		await That(twins["a"]).IsSameAs(twins["b"])
			.Because("one singleton implementation registered under two keys backs both dictionary entries");
	}

	[Fact]
	public async Task ExplicitlyRegisteredDictionary_WinsOverSynthesis()
	{
		using RegisteredMapContainer.Root container = new();

		MapConsumer consumer = container.Resolve<MapConsumer>();

		// IReadOnlyDictionary<string, IChannel> is itself registered (ChannelMap), so injection and by-type
		// resolution both hand out that registration - never a dictionary synthesized from the keyed
		// registrations, which would contain FastChannel under "fast".
		await That(consumer.Channels).Is<ChannelMap>()
			.Because("an explicitly registered dictionary service preempts the synthesized keyed dictionary");
		await That(consumer.Channels).HasCount(0)
			.Because("the registered (empty) map is handed out as-is; the keyed IChannel registrations do not leak into it");
		await That(container.Resolve<IReadOnlyDictionary<string, IChannel>>()).IsSameAs(consumer.Channels)
			.Because("by-type resolution dispatches the registered singleton dictionary, not a fresh synthesized one");
	}

	public interface IWidget;

	public sealed class DisposableWidget : IWidget, IDisposable
	{
		public void Dispose()
		{
		}
	}

	[Container]
	[Transient<DisposableWidget, IWidget>(Key = "a")]
	public static partial class WidgetContainer;

	public interface ITwin;

	public sealed class TwinChannel : ITwin;

	[Container]
	[Singleton<TwinChannel, ITwin>(Key = "a")]
	[Singleton<TwinChannel, ITwin>(Key = "b")]
	public static partial class TwinContainer;

	public sealed class ChannelMap : Dictionary<string, IChannel>;

	public sealed class MapConsumer
	{
		public MapConsumer(IReadOnlyDictionary<string, IChannel> channels) => Channels = channels;

		public IReadOnlyDictionary<string, IChannel> Channels { get; }
	}

	[Container]
	[Singleton<FastChannel, IChannel>(Key = "fast")]
	[Singleton<ChannelMap, IReadOnlyDictionary<string, IChannel>>]
	[Singleton<MapConsumer>]
	public static partial class RegisteredMapContainer;

	// ----- Awaited keyed dictionary (Task<IReadOnlyDictionary<string, TService>>) -----

	[Fact]
	public async Task AwaitedDictionary_AwaitsMemberInitializationWithoutTaintingTheConsumer()
	{
		using AwaitedDictionaryContainer.Root container = new();

		// The router injects Task<IReadOnlyDictionary<string, IFeed>> over an async-initialized member, yet resolves
		// SYNCHRONOUSLY in the strict default: the awaited keyed dictionary launders its members' taint like the bare
		// Task<T> relationship - the await happens inside the produced task, not at the router's construction.
		AwaitedFeedRouter router = container.Resolve<AwaitedFeedRouter>();

		IReadOnlyDictionary<string, IFeed> feeds = await router.Feeds;

		await That(feeds).HasCount(2);
		await That(feeds["sync"]).Is<SyncFeed>();
		await That(feeds["async"]).Is<AsyncFeed>();
		await That(((AsyncFeed)feeds["async"]).Initialized).IsTrue()
			.Because("awaiting the keyed dictionary awaited the async member's initialization behind the produced task");
	}

	[Fact]
	public async Task AwaitedDictionary_InjectedIntoAProperty_AwaitsMemberInitialization()
	{
		using AwaitedDictionaryContainer.Root container = new();

		AwaitedFeedPropertyRouter router = container.Resolve<AwaitedFeedPropertyRouter>();

		IReadOnlyDictionary<string, IFeed> feeds = await router.Feeds!;

		await That(feeds).HasCount(2);
		await That(((AsyncFeed)feeds["async"]).Initialized).IsTrue()
			.Because("an [Inject] property typed as an awaited keyed dictionary awaits its members exactly like a constructor parameter");
	}

	[Fact]
	public async Task AwaitedDictionary_AllSynchronousMembers_IsACompletedTask()
	{
		using AwaitedDictionaryContainer.Root container = new();

		// The all-synchronous ITransientFeed dictionary is a completed Task.FromResult over the materialized
		// dictionary - no async machinery at all.
		Task<IReadOnlyDictionary<string, ITransientFeed>> task = container.Resolve<Task<IReadOnlyDictionary<string, ITransientFeed>>>();

		await That(task.IsCompleted).IsTrue()
			.Because("an all-synchronous awaited keyed dictionary carries no async machinery");
		await That(await task).HasCount(1);
	}

	[Fact]
	public async Task AwaitedDictionary_EmptyMembership_YieldsACompletedEmptyDictionary()
	{
		using AwaitedDictionaryContainer.Root container = new();

		AwaitedEmptyRouter router = container.Resolve<AwaitedEmptyRouter>();

		IReadOnlyDictionary<string, IEmptyFeed> feeds = await router.Feeds;
		await That(feeds).HasCount(0)
			.Because("a service with no keyed registration yields a completed empty awaited keyed dictionary, not AWT101");
	}

	[Fact]
	public async Task AwaitedDictionary_IsPubliclyResolvableByType()
	{
		using AwaitedDictionaryContainer.Root container = new();

		IReadOnlyDictionary<string, IFeed> feeds = await container.Resolve<Task<IReadOnlyDictionary<string, IFeed>>>();

		await That(feeds).HasCount(2);
		await That(((AsyncFeed)feeds["async"]).Initialized).IsTrue()
			.Because("the awaited keyed dictionary joins the by-type dispatch and awaits its async member when awaited");
	}

	[Fact]
	public async Task AwaitedDictionary_SingletonMembers_AreSharedAcrossResolutions()
	{
		using AwaitedDictionaryContainer.Root container = new();

		IFeed first = (await container.Resolve<Task<IReadOnlyDictionary<string, IFeed>>>())["sync"];
		IFeed second = (await container.Resolve<Task<IReadOnlyDictionary<string, IFeed>>>())["sync"];

		await That(first).IsSameAs(second)
			.Because("a singleton keyed member is shared across every awaited dictionary that includes it");
	}

	[Fact]
	public async Task AwaitedDictionary_TransientMembers_AreFreshOnEachResolution()
	{
		using AwaitedDictionaryContainer.Root container = new();

		ITransientFeed first = (await container.Resolve<Task<IReadOnlyDictionary<string, ITransientFeed>>>())["t"];
		ITransientFeed second = (await container.Resolve<Task<IReadOnlyDictionary<string, ITransientFeed>>>())["t"];

		await That(first).IsNotSameAs(second)
			.Because("a transient keyed member is built fresh for each awaited dictionary");
	}

	[Fact]
	public async Task AwaitedDictionary_ScopedMembers_AreResolvedPerScope()
	{
		using AwaitedDictionaryContainer.Root container = new();
		using IAwaitenScope scope1 = container.CreateScope();
		using IAwaitenScope scope2 = container.CreateScope();

		IScopedFeed first = (await scope1.Resolve<Task<IReadOnlyDictionary<string, IScopedFeed>>>())["main"];
		IScopedFeed again = (await scope1.Resolve<Task<IReadOnlyDictionary<string, IScopedFeed>>>())["main"];
		IScopedFeed other = (await scope2.Resolve<Task<IReadOnlyDictionary<string, IScopedFeed>>>())["main"];

		await That(first).IsSameAs(again)
			.Because("a scoped keyed member is shared within the scope that materializes the awaited dictionary");
		await That(first).IsNotSameAs(other)
			.Because("a different scope materializes its own scoped keyed member");
	}

	[Fact]
	public async Task Strict_AwaitedKeyedDictionaryOfDisposableTransients_IsWithheldFromTheRootButResolvesFromAScope()
	{
		using WidgetContainer.Root container = new();

		// Materializing the awaited dictionary by type off the Root would accumulate its disposable transient members
		// for the container's lifetime (the task materializes them at construction), so under strict lifetime safety
		// it is withheld there, exactly like the synchronous dictionary and the awaited collection.
		await That(() => container.Resolve<Task<IReadOnlyDictionary<string, IWidget>>>()).Throws<InvalidOperationException>()
			.Because("an awaited keyed dictionary with a build-on-demand disposable member is root-withheld under strict safety");

		using IAwaitenScope scope = container.CreateScope();
		await That(await scope.Resolve<Task<IReadOnlyDictionary<string, IWidget>>>()).HasCount(1)
			.Because("a child scope bounds the disposable members the awaited dictionary materializes");
	}

	public interface IFeed;

	public sealed class SyncFeed : IFeed;

	public sealed class AsyncFeed : IFeed, IAsyncInitializable
	{
		public bool Initialized { get; private set; }

		public Task InitializeAsync(System.Threading.CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}
	}

	public sealed class AwaitedFeedRouter
	{
		public AwaitedFeedRouter(Task<IReadOnlyDictionary<string, IFeed>> feeds) => Feeds = feeds;

		public Task<IReadOnlyDictionary<string, IFeed>> Feeds { get; }
	}

	public sealed class AwaitedFeedPropertyRouter
	{
		[Inject]
		public Task<IReadOnlyDictionary<string, IFeed>>? Feeds { get; set; }
	}

	public interface ITransientFeed;

	public sealed class TransientFeed : ITransientFeed;

	public interface IScopedFeed;

	public sealed class ScopedFeed : IScopedFeed;

	public interface IEmptyFeed;

	public sealed class AwaitedEmptyRouter
	{
		public AwaitedEmptyRouter(Task<IReadOnlyDictionary<string, IEmptyFeed>> feeds) => Feeds = feeds;

		public Task<IReadOnlyDictionary<string, IEmptyFeed>> Feeds { get; }
	}

	[Container]
	[Singleton<SyncFeed, IFeed>(Key = "sync")]
	[Singleton<AsyncFeed, IFeed>(Key = "async")]
	[Transient<TransientFeed, ITransientFeed>(Key = "t")]
	[Scoped<ScopedFeed, IScopedFeed>(Key = "main")]
	[Transient<AwaitedFeedRouter>]
	[Transient<AwaitedFeedPropertyRouter>]
	[Singleton<AwaitedEmptyRouter>]
	public static partial class AwaitedDictionaryContainer;

	[Fact]
	public async Task AwaitedDictionary_FromKeyOverAKeyRegisteredDictionary_ResolvesTheRegistration()
	{
		using RegisteredAwaitedMapContainer.Root container = new();

		FromKeyAwaitedRouter router = container.Resolve<FromKeyAwaitedRouter>();
		IReadOnlyDictionary<string, IChannel> channels = await router.Channels;

		// A [FromKey] selection admits no synthesized awaited view (that would be AWT160), so the parameter is the
		// bare Task relationship over the dictionary registered under that key - never a synthesized dictionary of
		// the keyed IChannel registrations.
		await That(channels).Is<KeyedChannelMap>()
			.Because("[FromKey] resolves the dictionary registered under that key through the bare Task relationship");
		await That(channels).HasCount(0)
			.Because("the registered (empty) map is handed out as-is; the keyed IChannel registrations do not leak into it");
	}

	[Fact]
	public async Task AwaitedDictionary_NonStringKeyOverARegisteredDictionary_ResolvesTheRegistration()
	{
		using RegisteredAwaitedMapContainer.Root container = new();

		IntAwaitedRouter router = container.Resolve<IntAwaitedRouter>();
		IReadOnlyDictionary<int, IChannel> channels = await router.Channels;

		// A non-string key admits no synthesized awaited view at all (unregistered it is AWT159), so over a
		// registered dictionary the parameter stays the bare Task relationship and resolves the registration.
		await That(channels).Is<IntChannelMap>()
			.Because("a registered non-string-keyed dictionary is resolvable through its awaited Task<…> view");
	}

	public sealed class KeyedChannelMap : Dictionary<string, IChannel>;

	public sealed class IntChannelMap : Dictionary<int, IChannel>;

	public sealed class FromKeyAwaitedRouter
	{
		public FromKeyAwaitedRouter([FromKey("map")] Task<IReadOnlyDictionary<string, IChannel>> channels) => Channels = channels;

		public Task<IReadOnlyDictionary<string, IChannel>> Channels { get; }
	}

	public sealed class IntAwaitedRouter
	{
		public IntAwaitedRouter(Task<IReadOnlyDictionary<int, IChannel>> channels) => Channels = channels;

		public Task<IReadOnlyDictionary<int, IChannel>> Channels { get; }
	}

	[Container]
	[Singleton<FastChannel, IChannel>(Key = "fast")]
	[Singleton<KeyedChannelMap, IReadOnlyDictionary<string, IChannel>>(Key = "map")]
	[Singleton<IntChannelMap, IReadOnlyDictionary<int, IChannel>>]
	[Singleton<FromKeyAwaitedRouter>]
	[Singleton<IntAwaitedRouter>]
	public static partial class RegisteredAwaitedMapContainer;
}
