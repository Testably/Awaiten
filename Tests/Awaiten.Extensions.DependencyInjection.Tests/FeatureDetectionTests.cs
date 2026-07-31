using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     <see cref="IServiceProviderIsService" /> and <see cref="IServiceProviderIsKeyedService" /> on the
///     provider-replacement path. ASP.NET Core consults these to decide whether a parameter comes from
///     dependency injection or from the request, so the invariant that matters is that
///     <c>IsService(t)</c> is true exactly when <c>GetService(t)</c> returns an instance.
/// </summary>
/// <remarks>
///     The container dispatches far more resolvable shapes than it advertises as registrations, and the rules it
///     uses are not obvious. So rather than trust a reading of the generator, the shape lists here are checked
///     against what the container actually does, on the root and on a scope, and a disagreement fails naming the
///     shape. The nested classes group by the rule under test; the divergences the bridge cannot avoid live in
///     <see cref="AcceptedDivergences" />.
/// </remarks>
public sealed partial class FeatureDetectionTests
{
	public interface IThing;

	public sealed class Alpha : IThing;

	public sealed class Beta : IThing;

	public sealed class AsyncThing : IThing, IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	public sealed class Unregistered;

	public enum Speed
	{
		Slow,
		Fast,
	}

	/// <summary>
	///     Every shape a container over <see cref="IThing" /> may or may not serve. Shared by the collection and
	///     dictionary groups so that each container is asked the same wide question.
	/// </summary>
	private static Type[] ThingShapes =>
	[
		typeof(IThing),
		typeof(IEnumerable<IThing>),
		typeof(IReadOnlyList<IThing>),
		typeof(IReadOnlyCollection<IThing>),
		typeof(IList<IThing>),
		typeof(ICollection<IThing>),
		typeof(IThing[]),
		typeof(IAsyncEnumerable<IThing>),
		typeof(Task<IEnumerable<IThing>>),
		typeof(Task<IReadOnlyList<IThing>>),
		typeof(Task<IReadOnlyCollection<IThing>>),
		typeof(Task<IList<IThing>>),
		typeof(Task<ICollection<IThing>>),
		typeof(Task<IThing[]>),
		typeof(Task<IAsyncEnumerable<IThing>>),
		typeof(IReadOnlyDictionary<string, IThing>),
		typeof(Task<IReadOnlyDictionary<string, IThing>>),
		typeof(IReadOnlyDictionary<object, IThing>),
		typeof(Task<IReadOnlyDictionary<object, IThing>>),
		typeof(IReadOnlyDictionary<Speed, IThing>),
		typeof(IEnumerable<Unregistered>),
		typeof(Unregistered),
	];

	/// <summary>
	///     Registered one implementation per member, all synchronous — the ordinary case, where every synthesized
	///     collection shape is available.
	/// </summary>
	[Container]
	[Singleton<Alpha, IThing>]
	[Singleton<Beta, IThing>]
	public static partial class SyncCollectionContainer;

	/// <summary>
	///     One synchronous member and one that needs asynchronous initialization. The container then synthesizes no
	///     synchronous collection shape at all, which the metadata cannot show — see
	///     <see cref="AcceptedDivergences" />.
	/// </summary>
	[Container]
	[Singleton<Alpha, IThing>]
	[Singleton<AsyncThing, IThing>]
	public static partial class MixedCollectionContainer;

	/// <summary>The services the provider answers with itself, and how it behaves without container metadata.</summary>
	public sealed partial class ProviderServices
	{
		public sealed class ScopedThing;

		public sealed class TransientThing;

		[Container]
		[Singleton<Alpha, IThing>]
		[Scoped<ScopedThing>]
		[Transient<TransientThing>]
		public static partial class BasicContainer;

		[Fact]
		public async Task ProbeIsOfferedAndAnswersRegistrations()
		{
			using BasicContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			IServiceProviderIsService probe =
				(IServiceProviderIsService)provider.GetService(typeof(IServiceProviderIsService))!;

			await That(probe).IsNotNull()
				.Because("minimal APIs consult this to tell a DI parameter from a request parameter");
			await That(probe.IsService(typeof(IThing))).IsTrue();
			await That(probe.IsService(typeof(ScopedThing))).IsTrue();
			await That(probe.IsService(typeof(TransientThing))).IsTrue();
			await That(probe.IsService(typeof(Unregistered))).IsFalse();
		}

		[Fact]
		public async Task TheProvidersOwnServicesAreReported()
		{
			using BasicContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsService(typeof(IServiceProvider))).IsTrue();
			await That(provider.IsService(typeof(IServiceScopeFactory))).IsTrue();
			await That(provider.IsService(typeof(AwaitenServiceProvider))).IsTrue();
			await That(provider.IsService(typeof(IServiceProviderIsService))).IsTrue();
			await That(provider.IsService(typeof(IServiceProviderIsKeyedService))).IsTrue();
		}

		[Fact]
		public async Task AScopeOffersTheProbeToo()
		{
			using BasicContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);
			using IServiceScope scope = provider.CreateScope();

			IServiceProviderIsService probe =
				(IServiceProviderIsService)scope.ServiceProvider.GetService(typeof(IServiceProviderIsService))!;

			await That(probe).IsNotNull()
				.Because("the metadata flows into scope providers, and a per-request provider is the one ASP.NET asks");
			await That(probe.IsService(typeof(ScopedThing))).IsTrue();
		}

		[Fact]
		public async Task WithoutContainerMetadataTheProbeIsNotOffered()
		{
			using BasicContainer.Root container = new();
			using IAwaitenScope bareScope = container.CreateScope();
			using AwaitenServiceProvider provider = new(bareScope, ownsContainer: false);

			await That(provider.GetService(typeof(IServiceProviderIsService))).IsNull()
				.Because("only a generated Root carries registration metadata, so a bare Scope has nothing to answer from, and declining lets the framework keep its own fallback rather than trusting a probe that would deny every service");
			await That(provider.IsService(typeof(IThing))).IsFalse();
		}

		[Fact]
		public async Task ANullServiceTypeThrows()
		{
			using BasicContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			void IsService() => provider.IsService(null!);
			void IsKeyedService() => provider.IsKeyedService(null!, "key");

			await That(IsService).Throws<ArgumentNullException>();
			await That(IsKeyedService).Throws<ArgumentNullException>();
		}
	}

	/// <summary>How a plain registration is reported, including the <c>Task&lt;T&gt;</c> projection and keys.</summary>
	public sealed partial class Registrations
	{
		public sealed class AsyncOnly : IAsyncInitializable
		{
			public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		}

		[Container]
		[Singleton<AsyncOnly>]
		public static partial class AsyncOnlyContainer;

		[Container]
		[Singleton<Alpha, IThing>]
		[Singleton<Beta, IThing>(Key = "beta")]
		public static partial class KeyedContainer;

		[Container]
		[Singleton<Beta, IThing>(Key = "only")]
		public static partial class KeyOnlyContainer;

		[Fact]
		public async Task AnAsyncOnlyRegistrationIsReportedUnderItsTaskProjection()
		{
			using AsyncOnlyContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsService(typeof(Task<AsyncOnly>))).IsTrue();
			await That(provider.IsService(typeof(AsyncOnly))).IsFalse()
				.Because("it is advertised, but has no synchronous path, so reporting the bare type would promise a service GetService answers with null");
			await That(provider.IsService(typeof(Task<Unregistered>))).IsFalse();
		}

		[Fact]
		public async Task AnAsyncOnlyRegistrationAgreesAcrossItsShapes()
		{
			Type[] shapes =
			[
				typeof(AsyncOnly),
				typeof(Task<AsyncOnly>),
				typeof(Task<Unregistered>),
				typeof(IEnumerable<AsyncOnly>),
				typeof(AsyncOnly[]),
				typeof(Task<IEnumerable<AsyncOnly>>),
				typeof(Task<IReadOnlyList<AsyncOnly>>),
			];

			using AsyncOnlyContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, shapes)).IsEqualTo(string.Empty);
		}

		[Fact]
		public async Task AKeyedRegistrationIsReportedUnderItsKey()
		{
			using KeyedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsKeyedService(typeof(IThing), "beta")).IsTrue();
			await That(provider.IsKeyedService(typeof(IThing), "missing")).IsFalse();
			await That(provider.IsKeyedService(typeof(Unregistered), "beta")).IsFalse();
		}

		[Fact]
		public async Task ANullKeyMeansTheUnkeyedRegistration()
		{
			using KeyedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsKeyedService(typeof(IThing), null)).IsTrue();
			await That(provider.IsKeyedService(typeof(Unregistered), null)).IsFalse();
		}

		[Fact]
		public async Task AnyKeyIsDeclined()
		{
			using KeyedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsKeyedService(typeof(IThing), KeyedService.AnyKey)).IsFalse()
				.Because("Awaiten has no wildcard-key registration, and GetKeyedService declines AnyKey the same way");
			await That(provider.GetKeyedService(typeof(IThing), KeyedService.AnyKey)).IsNull();
		}

		[Fact]
		public async Task AKeyOnlyRegistrationIsNotAnUnkeyedService()
		{
			using KeyOnlyContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsService(typeof(IThing))).IsFalse()
				.Because("GetService resolves only the unkeyed registration, so asked unkeyed this is not a service");
			await That(provider.GetService(typeof(IThing))).IsNull();
			await That(provider.IsKeyedService(typeof(IThing), "only")).IsTrue();
		}
	}

	/// <summary>
	///     The synthesized collection shapes. An explicitly registered collection shape replaces the synthesized
	///     siblings entirely, so each container here asks the same wide shape list and must agree on all of it.
	/// </summary>
	public sealed partial class Collections
	{
		public sealed class ThingList : List<IThing>;

		public sealed class AsyncStream : IAsyncEnumerable<IThing>, IAsyncInitializable
		{
			public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

			public IAsyncEnumerator<IThing> GetAsyncEnumerator(CancellationToken cancellationToken = default)
				=> throw new NotSupportedException();
		}

		[Container]
		[Singleton<Alpha, IThing>]
		[Singleton<ThingList, IList<IThing>>]
		public static partial class ExplicitShapeContainer;

		[Container]
		[Singleton<Alpha, IThing>(Key = "a")]
		[Singleton<Beta, IThing>(Key = "b")]
		public static partial class KeyedOnlyContainer;

		[Container]
		[Singleton<Alpha, IThing>]
		[Singleton<AsyncStream, IAsyncEnumerable<IThing>>]
		public static partial class ExplicitAsyncStreamContainer;

		[Fact]
		public async Task AllMembersSynchronous()
		{
			using SyncCollectionContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes)).IsEqualTo(string.Empty);
		}

		[Fact]
		public async Task EveryShapeOverARegisteredElementTypeIsReported()
		{
			using SyncCollectionContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsService(typeof(IEnumerable<IThing>))).IsTrue();
			await That(provider.IsService(typeof(IReadOnlyList<IThing>))).IsTrue();
			await That(provider.IsService(typeof(IReadOnlyCollection<IThing>))).IsTrue();
			await That(provider.IsService(typeof(IList<IThing>))).IsTrue();
			await That(provider.IsService(typeof(ICollection<IThing>))).IsTrue();
			await That(provider.IsService(typeof(IThing[]))).IsTrue();
			await That(provider.IsService(typeof(IAsyncEnumerable<IThing>))).IsTrue();
			await That(provider.IsService(typeof(IEnumerable<Unregistered>))).IsFalse()
				.Because("the container has no collection case for an element type the graph never mentioned");
		}

		[Fact]
		public async Task AnExplicitlyRegisteredShapeSuppressesItsSiblings()
		{
			using ExplicitShapeContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes)).IsEqualTo(string.Empty);
			await That(provider.IsService(typeof(IList<IThing>))).IsTrue()
				.Because("the registered shape resolves");
			await That(provider.IsService(typeof(IEnumerable<IThing>))).IsFalse()
				.Because("registering one collection shape explicitly stops the container synthesizing the others");
		}

		[Fact]
		public async Task AnExplicitlyRegisteredAsyncOnlyStreamClaimsItsOwnShape()
		{
			using ExplicitAsyncStreamContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes)).IsEqualTo(string.Empty);
			await That(provider.IsService(typeof(IAsyncEnumerable<IThing>))).IsFalse()
				.Because("the explicit registration takes the shape over from the synthesized view, and being async-only it has no synchronous path either");
		}

		[Fact]
		public async Task KeyedRegistrationsAloneSynthesizeNoCollection()
		{
			using KeyedOnlyContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes)).IsEqualTo(string.Empty);
		}

		[Fact]
		public async Task AMixedContainerAgreesOnEverythingButTheSynchronousShapes()
		{
			Type[] shapes =
			[
				typeof(IThing),
				typeof(Task<IEnumerable<IThing>>),
				typeof(Task<IReadOnlyList<IThing>>),
				typeof(Task<IThing[]>),
				typeof(IReadOnlyDictionary<string, IThing>),
				typeof(Task<IReadOnlyDictionary<string, IThing>>),
				typeof(IReadOnlyDictionary<object, IThing>),
				typeof(Task<IReadOnlyDictionary<object, IThing>>),
				typeof(IReadOnlyDictionary<Speed, IThing>),
				typeof(Task<IAsyncEnumerable<IThing>>),
				typeof(IEnumerable<Unregistered>),
				typeof(Unregistered),
			];

			using MixedCollectionContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, shapes)).IsEqualTo(string.Empty)
				.Because("only the synchronous collection shapes are affected by an async member; the awaited views over them still resolve");
		}
	}

	/// <summary>
	///     The synthesized <c>IReadOnlyDictionary&lt;TKey, T&gt;</c>. A dictionary is emitted per key kind and only
	///     for a <see cref="string" /> or enum key, so the shape asked for has to carry the registrations' key type
	///     exactly rather than merely be assignable from it.
	/// </summary>
	public sealed partial class KeyedDictionaries
	{
		public sealed class ThingMap : Dictionary<string, IThing>;

		[Container]
		[Singleton<Alpha, IThing>(Key = "a")]
		[Singleton<Beta, IThing>(Key = "b")]
		public static partial class StringKeyedContainer;

		[Container]
		[Singleton<Alpha, IThing>(Key = Speed.Slow)]
		[Singleton<Beta, IThing>(Key = Speed.Fast)]
		public static partial class EnumKeyedContainer;

		[Container]
		[Singleton<Alpha, IThing>(Key = "a")]
		[Singleton<Beta, IThing>(Key = Speed.Fast)]
		public static partial class MixedKeyKindContainer;

		[Container]
		[Singleton<Alpha, IThing>(Key = "a")]
		[Singleton<AsyncThing, IThing>(Key = "b")]
		public static partial class AsyncMemberContainer;

		[Container]
		[Singleton<Alpha, IThing>(Key = "a")]
		[Singleton<ThingMap, IReadOnlyDictionary<string, IThing>>]
		public static partial class ExplicitDictionaryContainer;

		[Container]
		[Singleton<Alpha, IThing>(Key = "a")]
		[Singleton<ThingMap, IReadOnlyDictionary<string, IThing>>(Key = "map")]
		public static partial class KeyedExplicitDictionaryContainer;

		[Fact]
		public async Task StringKeys()
		{
			using StringKeyedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes)).IsEqualTo(string.Empty);
			await That(provider.IsService(typeof(IReadOnlyDictionary<string, IThing>))).IsTrue();
			await That(provider.IsService(typeof(IReadOnlyDictionary<object, IThing>))).IsFalse()
				.Because("a dictionary is emitted per key kind, so an assignable key type is not the same shape");
		}

		[Fact]
		public async Task EnumKeys()
		{
			using EnumKeyedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes)).IsEqualTo(string.Empty);
			await That(provider.IsService(typeof(IReadOnlyDictionary<Speed, IThing>))).IsTrue();
		}

		[Fact]
		public async Task KeysOfMoreThanOneKindGetNoDictionary()
		{
			using MixedKeyKindContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes)).IsEqualTo(string.Empty);
			await That(provider.IsService(typeof(IReadOnlyDictionary<string, IThing>))).IsFalse()
				.Because("the container emits no dictionary when its keyed members do not share one key kind");
		}

		[Fact]
		public async Task AnAsyncMemberLeavesOnlyTheAwaitedView()
		{
			using AsyncMemberContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes)).IsEqualTo(string.Empty);
			await That(provider.IsService(typeof(IReadOnlyDictionary<string, IThing>))).IsFalse()
				.Because("the synchronous dictionary needs every member synchronously resolvable");
			await That(provider.IsService(typeof(Task<IReadOnlyDictionary<string, IThing>>))).IsTrue()
				.Because("the awaited view does not");
		}

		[Fact]
		public async Task AnUnkeyedExplicitDictionarySuppressesTheSynthesizedOne()
		{
			using ExplicitDictionaryContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes)).IsEqualTo(string.Empty);
		}

		[Fact]
		public async Task AKeyedExplicitDictionarySuppressesNothing()
		{
			using KeyedExplicitDictionaryContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes)).IsEqualTo(string.Empty);
			await That(provider.IsService(typeof(IReadOnlyDictionary<string, IThing>))).IsTrue()
				.Because("a registration of the dictionary shape under a key is reached through that key and replaces nothing, so the synthesized dictionary over the keyed members is still there");
		}
	}

	/// <summary>
	///     Divergences from the invariant that the bridge cannot avoid, each pinned so a change of behaviour fails
	///     here rather than passing unnoticed.
	/// </summary>
	/// <remarks>
	///     Two over-report and one under-reports, and the direction is deliberate. A false positive has the
	///     framework bind from dependency injection and <c>GetRequiredService</c> throw at request time, which is
	///     loud; a false negative has it bind the parameter from the request body, which is silent. Closing the
	///     first and third needs the container to advertise the shapes it dispatches — it builds that set at compile
	///     time already — instead of the bridge inferring them from registrations.
	/// </remarks>
	public sealed partial class AcceptedDivergences
	{
		public sealed class Consumer
		{
			public Consumer(Alpha alpha) => _ = alpha;
		}

		public sealed class DisposableTransient : IDisposable
		{
			public void Dispose()
			{
			}
		}

		public interface IEvent;

		public sealed class DerivedEvent : IEvent;

		public interface IHandler<out TEvent>;

		public sealed class DerivedHandler : IHandler<DerivedEvent>;

		[Container]
		[Singleton<Alpha>]
		[Transient<Consumer>]
		public static partial class RelationshipContainer;

		[Container]
		[Transient<DisposableTransient>]
		public static partial class WithheldContainer;

		[Container]
		[Singleton<DerivedHandler, IHandler<DerivedEvent>>]
		public static partial class VarianceContainer;

		[Fact]
		public async Task ACollectionWithANonSynchronousMemberIsOverReported()
		{
			using MixedCollectionContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsService(typeof(IEnumerable<IThing>))).IsTrue();
			await That(provider.GetService(typeof(IEnumerable<IThing>))).IsNull()
				.Because("an async-initialized member stops the synchronous collection being synthesized, and no rule over the metadata can see that: the implementations of one service type coalesce into a single advertised entry, so this container and one with only the synchronous member advertise byte-identical metadata and resolve differently");

			await That(provider.IsService(typeof(IThing))).IsTrue();
			await That(provider.GetService(typeof(IThing))).IsNotNull()
				.Because("the element type itself is reported correctly");
		}

		[Fact]
		public async Task ADisposableTransientIsOverReportedOnTheRoot()
		{
			using WithheldContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsService(typeof(DisposableTransient))).IsTrue();
			await That(provider.GetService(typeof(DisposableTransient))).IsNull()
				.Because("under the strict lifetime default the container withholds a disposable transient on the root, where each resolution would accumulate for the container's lifetime; IsService answers whether the service exists, as MS.DI's does, not whether this scope will serve it");

			await That(provider.IsService(typeof(IEnumerable<DisposableTransient>))).IsTrue();
			await That(provider.GetService(typeof(IEnumerable<DisposableTransient>))).IsNull()
				.Because("the collection views over it are withheld on the root too, not just the bare type");
		}

		[Fact]
		public async Task ADisposableTransientAgreesInsideAScope()
		{
			using WithheldContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);
			using IServiceScope scope = provider.CreateScope();

			await That(((IServiceProviderIsService)scope.ServiceProvider).IsService(typeof(DisposableTransient)))
				.IsTrue();
			await That(scope.ServiceProvider.GetService(typeof(DisposableTransient))).IsNotNull()
				.Because("inside a scope such a transient is bounded, so the two agree again");
		}

		[Fact]
		public async Task RelationshipShapesAreUnderReported()
		{
			using RelationshipContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.GetService(typeof(Func<Alpha>))).IsNotNull();
			await That(provider.IsService(typeof(Func<Alpha>))).IsFalse()
				.Because("a relationship shape is not an advertised registration, and MS.DI has no such shapes at all, so a framework calibrated against MS.DI is told what it expects");
			await That(provider.GetService(typeof(Lazy<Alpha>))).IsNotNull();
			await That(provider.IsService(typeof(Lazy<Alpha>))).IsFalse();
			await That(provider.GetService(typeof(Owned<Alpha>))).IsNotNull();
			await That(provider.IsService(typeof(Owned<Alpha>))).IsFalse();
		}

		[Fact]
		public async Task AVarianceCompatibleClosingIsUnderReported()
		{
			using VarianceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.GetService(typeof(IHandler<IEvent>))).IsNotNull();
			await That(provider.IsService(typeof(IHandler<IEvent>))).IsFalse()
				.Because("the container's variance fallback satisfies a differently-closed variant interface at run time, but only the declared closure is advertised as a registration");
			await That(provider.IsService(typeof(IHandler<DerivedEvent>))).IsTrue()
				.Because("the declared closure is advertised and resolves");
		}
	}
}
