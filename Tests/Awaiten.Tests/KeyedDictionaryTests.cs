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
}
