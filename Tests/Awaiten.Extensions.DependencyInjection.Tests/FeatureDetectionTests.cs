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

		[Container]
		[Singleton<AsyncOnly>(Key = "async")]
		public static partial class KeyedAsyncOnlyContainer;

		/// <summary>
		///     <c>SyncResolveAfterInit</c> makes a warmed async-tainted service synchronously resolvable, and the
		///     metadata advertises it as synchronous — the one input <c>HasSyncRegistration</c> and the
		///     <c>Task&lt;T&gt;</c> projection read, so the reported shapes swap over with it.
		/// </summary>
		[Container(SyncResolveAfterInit = true)]
		[Singleton<AsyncOnly>]
		public static partial class SyncAfterInitContainer;

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

		/// <summary>
		///     The keyed probe over the same wide shape list the unkeyed one is swept with. The collection and
		///     dictionary shapes are unkeyed-only, so under a key they must all report false, and a key that matches
		///     nothing must report false for everything.
		/// </summary>
		[Fact]
		public async Task TheKeyedProbeAgreesAcrossTheShapesUnderAKey()
		{
			using KeyedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes, "beta")).IsEqualTo(string.Empty);
			await That(ProbeAgreement.Disagreements(provider, ThingShapes, "missing")).IsEqualTo(string.Empty);
			await That(provider.IsKeyedService(typeof(IEnumerable<IThing>), "beta")).IsFalse()
				.Because("a collection shape is synthesized unkeyed only, and GetKeyedService declines it the same way");
		}

		[Fact]
		public async Task AKeyedAsyncOnlyRegistrationIsReportedUnderItsTaskProjectionAndKey()
		{
			Type[] shapes = [typeof(AsyncOnly), typeof(Task<AsyncOnly>), typeof(Task<Unregistered>),];

			using KeyedAsyncOnlyContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, shapes, "async")).IsEqualTo(string.Empty);
			await That(provider.IsKeyedService(typeof(Task<AsyncOnly>), "async")).IsTrue()
				.Because("the keyed Task<T> projection is served through the keyed ResolveAsync");
			await That(provider.IsKeyedService(typeof(AsyncOnly), "async")).IsFalse()
				.Because("the keyed registration has no synchronous path either");
		}

		[Fact]
		public async Task SyncResolveAfterInitMovesTheAnswerToTheBareType()
		{
			Type[] shapes =
			[
				typeof(AsyncOnly),
				typeof(Task<AsyncOnly>),
				typeof(IEnumerable<AsyncOnly>),
				typeof(AsyncOnly[]),
				typeof(Task<IEnumerable<AsyncOnly>>),
			];

			using SyncAfterInitContainer.Root container = new();
			await container.InitializeAsync(TestContext.Current.CancellationToken);
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, shapes)).IsEqualTo(string.Empty);
			await That(provider.IsService(typeof(AsyncOnly))).IsTrue()
				.Because("the metadata advertises the warmed service as synchronous, and it is one");
			await That(provider.IsService(typeof(Task<AsyncOnly>))).IsFalse()
				.Because("there is no async registration left to project, so the Task<T> shape is not a service");
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
		public async Task AMemberThatNeedsAsyncInitialization()
		{
			using MixedCollectionContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, ThingShapes)).IsEqualTo(string.Empty);
			await That(provider.IsService(typeof(IEnumerable<IThing>))).IsFalse()
				.Because("one member that needs async initialization means no synchronous collection shape is synthesized at all, which the container knows and the registration metadata could never have shown");
			await That(provider.IsService(typeof(Task<IEnumerable<IThing>>))).IsTrue()
				.Because("the awaited view over it does still resolve");
		}

		[Fact]
		public async Task RelationshipShapesOverARegisteredService()
		{
			Type[] shapes =
			[
				typeof(Func<IThing>),
				typeof(Lazy<IThing>),
				typeof(Owned<IThing>),
			];

			using SyncCollectionContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, shapes)).IsEqualTo(string.Empty);
			await That(provider.IsService(typeof(Func<IThing>))).IsTrue()
				.Because("the container dispatches the relationship shapes, so reporting them is what lets a host take one from the provider instead of binding it from somewhere else");
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
	///     A service the container withholds from the root, where it exists but will not be built. The probe reports
	///     it, as MS.DI's does for a scoped service asked on the root, and resolution names the reason instead of
	///     answering with <see langword="null" />.
	/// </summary>
	public sealed partial class WithheldServices
	{
		public sealed class DisposableTransient : IDisposable
		{
			public void Dispose()
			{
			}
		}

		[Container]
		[Transient<DisposableTransient>]
		public static partial class WithheldContainer;

		[Container]
		[Transient<DisposableTransient>(Key = "x")]
		public static partial class KeyedWithheldContainer;

		[Fact]
		public async Task AreReportedAndNamedOnTheRoot()
		{
			using WithheldContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsService(typeof(DisposableTransient))).IsTrue()
				.Because("under the strict lifetime default the container withholds a disposable transient on the root, where each resolution would accumulate for the container's lifetime, but the service exists");

			void Act() => provider.GetService(typeof(DisposableTransient));

			await That(Act).Throws<InvalidOperationException>()
				.Because("resolution surfaces the container's guidance naming the fix, which is what MS.DI does for a scoping violation; a null would have let a host bind the parameter from somewhere else and fail far from the cause");
		}

		[Fact]
		public async Task AreReportedAndNamedOnTheRootUnderAKeyToo()
		{
			using KeyedWithheldContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsKeyedService(typeof(DisposableTransient), "x")).IsTrue()
				.Because("the keyed probe answers existence exactly like the unkeyed one, ignoring the root withholding");

			void Act() => provider.GetKeyedService(typeof(DisposableTransient), "x");

			await That(Act).Throws<InvalidOperationException>().WithMessage("*withheld*").AsWildcard()
				.Because("the keyed resolution surfaces the container's guidance like the unkeyed one; a silent null would contradict the probe");

			using IServiceScope scope = provider.CreateScope();
			IKeyedServiceProvider keyed = (IKeyedServiceProvider)scope.ServiceProvider;
			await That(keyed.GetKeyedService(typeof(DisposableTransient), "x")).IsNotNull()
				.Because("inside a scope such a transient is bounded, so it is built as asked");
		}

		[Fact]
		public async Task AreResolvableInsideAScope()
		{
			using WithheldContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);
			using IServiceScope scope = provider.CreateScope();

			await That(((IServiceProviderIsService)scope.ServiceProvider).IsService(typeof(DisposableTransient)))
				.IsTrue();
			await That(scope.ServiceProvider.GetService(typeof(DisposableTransient))).IsNotNull()
				.Because("inside a scope such a transient is bounded, so it is built as asked");
		}

		[Fact]
		public async Task AgreeAcrossTheirShapes()
		{
			Type[] shapes =
			[
				typeof(DisposableTransient),
				typeof(IEnumerable<DisposableTransient>),
				typeof(DisposableTransient[]),
				typeof(Func<DisposableTransient>),
				typeof(Task<IEnumerable<DisposableTransient>>),
			];

			using WithheldContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider, shapes)).IsEqualTo(string.Empty);
		}
	}

	/// <summary>
	///     A variance-compatible closing of a registered variant generic interface, which the container satisfies
	///     through its runtime variance fallback rather than through a registration of its own.
	/// </summary>
	public sealed partial class GenericVariance
	{
		public interface IEvent;

		public sealed class DerivedEvent : IEvent;

		public interface IHandler<out TEvent>;

		public sealed class DerivedHandler : IHandler<DerivedEvent>;

		[Container]
		[Singleton<DerivedHandler, IHandler<DerivedEvent>>]
		public static partial class VarianceContainer;

		public sealed class DisposableHandler : IHandler<DerivedEvent>, IDisposable
		{
			public void Dispose()
			{
			}
		}

		[Container]
		[Transient<DisposableHandler, IHandler<DerivedEvent>>]
		public static partial class WithheldVarianceContainer;

		[Fact]
		public async Task ACompatibleClosingIsReported()
		{
			using VarianceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(ProbeAgreement.Disagreements(provider,
					[typeof(IHandler<IEvent>), typeof(IHandler<DerivedEvent>), typeof(IHandler<DerivedHandler>),]))
				.IsEqualTo(string.Empty);
			await That(provider.IsService(typeof(IHandler<IEvent>))).IsTrue()
				.Because("the container's variance fallback satisfies a differently-closed variant interface, and the probe runs the same matching rather than looking for a registration that does not exist");
			await That(provider.IsService(typeof(IHandler<DerivedEvent>))).IsTrue()
				.Because("the declared closure resolves directly");
		}

		[Fact]
		public async Task AWithheldCandidateKeepsItsGuidanceThroughAVariantClosing()
		{
			using WithheldVarianceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsService(typeof(IHandler<IEvent>))).IsTrue()
				.Because("the closing exists; only the root declines to build its disposable transient candidate");

			void Act() => provider.GetService(typeof(IHandler<IEvent>));

			await That(Act).Throws<InvalidOperationException>().WithMessage("*withheld*").AsWildcard()
				.Because("the miss names the withheld candidate's guidance instead of claiming no registration exists for a closing the container serves");

			using IServiceScope scope = provider.CreateScope();
			await That(scope.ServiceProvider.GetService(typeof(IHandler<IEvent>))).IsNotNull()
				.Because("a child scope bounds the disposable, so the variance fallback serves the closing there");
		}
	}

	/// <summary>
	///     What remains divergent from MS.DI, pinned so a change of behaviour fails here rather than passing
	///     unnoticed. Both are cases where the probe faithfully reports this container and it is MS.DI that would
	///     answer differently, so neither is a disagreement between the probe and resolution.
	/// </summary>
	/// <remarks>
	///     Both under-report, which is the silent direction: a minimal API told <see langword="false" /> binds the
	///     parameter from the request body rather than failing to resolve it, so annotate such a parameter with
	///     <c>[FromServices]</c>. Neither is reachable by advertising what the container dispatches, because in both
	///     cases the container genuinely has no resolution to advertise.
	/// </remarks>
	public sealed partial class AcceptedDivergences
	{
		public interface IRepo<T>;

		public sealed class Repo<T> : IRepo<T>;

		public sealed class Ledger;

		public sealed class Audit;

		/// <summary>Puts <c>IRepo&lt;Ledger&gt;</c> in the container's own graph, so that closing is expanded.</summary>
		public sealed class LedgerReader
		{
			public LedgerReader(IRepo<Ledger> repo) => _ = repo;
		}

		[Container]
		[Singleton(typeof(Repo<>), typeof(IRepo<>))]
		[Transient<LedgerReader>]
		public static partial class OpenGenericContainer;

		[Container]
		[Singleton<Alpha, IThing>]
		public static partial class SingleRegistrationContainer;

		[Fact]
		public async Task AnEnumerableOfAnUnmentionedElementTypeIsUnderReported()
		{
			using SingleRegistrationContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsService(typeof(IEnumerable<Unregistered>))).IsFalse()
				.Because("MS.DI special-cases IEnumerable<T> and answers true for any T, registered or not, because it can always manifest an empty sequence; the generator emits collection cases only for element types the graph mentions, so this one has no resolution to report");
			await That(provider.GetService(typeof(IEnumerable<Unregistered>))).IsNull()
				.Because("the under-report is faithful to the container: MS.DI would hand back an empty sequence here, and matching that needs a collection built for a type unknown at compile time, which is the reflection this package does not do");
		}

		[Fact]
		public async Task AnOpenGenericClosingNothingInTheGraphAsksForIsUnderReported()
		{
			using OpenGenericContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, ownsContainer: false);

			await That(provider.IsService(typeof(IRepo<Ledger>))).IsTrue()
				.Because("a closing the container's own graph consumes is expanded into a concrete registration, so it is advertised like a hand-written one");
			await That(provider.GetService(typeof(IRepo<Ledger>))).IsNotNull();

			await That(provider.IsService(typeof(IRepo<Audit>))).IsFalse()
				.Because("open generics are expanded per closing that something in the graph asks for, and a framework's handler parameter is not in the graph, so this closing is never synthesized; MS.DI would answer true here, and a minimal API told false binds the parameter from the request body instead of failing to resolve it");
			await That(provider.GetService(typeof(IRepo<Audit>))).IsNull()
				.Because("the under-report is faithful to the container: the closing genuinely has no resolution either, so the divergence is from MS.DI's answer, not from this container's behaviour");

			await That(ProbeAgreement.Disagreements(provider, [typeof(IRepo<Ledger>), typeof(IRepo<Audit>),]))
				.IsEqualTo(string.Empty);
		}
	}
}
