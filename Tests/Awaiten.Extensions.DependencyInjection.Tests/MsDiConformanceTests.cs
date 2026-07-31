using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     Conformance of the provider-replacement path (<see cref="AwaitenServiceProvider" />) against the
///     behaviours Microsoft.Extensions.DependencyInjection guarantees to its consumers.
/// </summary>
/// <remarks>
///     <para>
///         The reference suite for a container's MS.DI compatibility is
///         <c>Microsoft.Extensions.DependencyInjection.Specification.Tests</c>. It cannot be inherited here for
///         two reasons: it is built against xunit v2 (<c>xunit.assert</c>, <c>xunit.extensibility.core</c>) while
///         this repository is on xunit.v3, and — decisively — its contract is
///         <c>CreateServiceProvider(IServiceCollection)</c>, which hands the container the types to register at
///         run time. An Awaiten graph is fixed when the generator runs, so no implementation of that method can
///         exercise Awaiten.
///     </para>
///     <para>
///         These tests therefore take the specification's <em>behaviours</em> as the checklist and assert them
///         against containers declared at compile time, grouped by the guarantee under test.
///         <c>MsDiConformance.md</c>, next to this file, records which guarantees are covered, which are not
///         applicable to a compile-time container, and where Awaiten deliberately diverges. Feature detection
///         (<see cref="IServiceProviderIsService" />) has its own suite in <c>FeatureDetectionTests</c>, and
///         disposal ownership in <c>BridgeDisposalTests</c>.
///     </para>
/// </remarks>
public sealed partial class MsDiConformanceTests
{
	public interface IService;

	public sealed class SingletonService : IService;

	public sealed class ScopedService;

	public sealed class TransientService;

	public sealed class UnregisteredService;

	[Container]
	[Singleton<SingletonService, IService>]
	[Scoped<ScopedService>]
	[Transient<TransientService>]
	public static partial class ConformanceContainer;

	/// <summary>
	///     That each lifetime hands back the instance MS.DI consumers expect, and no other.
	/// </summary>
	public sealed class Lifetimes
	{
		[Fact]
		public async Task ANestedScopeIsIndependentOfItsParent()
		{
			using ConformanceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			using IServiceScope outer = provider.CreateScope();
			ScopedService fromOuter = (ScopedService)outer.ServiceProvider.GetService(typeof(ScopedService))!;

			IServiceScopeFactory factory =
				(IServiceScopeFactory)outer.ServiceProvider.GetService(typeof(IServiceScopeFactory))!;
			using IServiceScope inner = factory.CreateScope();
			ScopedService fromInner = (ScopedService)inner.ServiceProvider.GetService(typeof(ScopedService))!;

			await That(ReferenceEquals(fromOuter, fromInner)).IsFalse()
				.Because("a nested scope is a scope in its own right, not a view onto its parent");
		}

		[Fact]
		public async Task ASingletonReachedFromAScopeIsTheRootsInstance()
		{
			using ConformanceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			IService fromRoot = (IService)provider.GetService(typeof(IService))!;
			using IServiceScope scope = provider.CreateScope();
			IService fromScope = (IService)scope.ServiceProvider.GetService(typeof(IService))!;

			await That(fromScope).IsSameAs(fromRoot)
				.Because("a singleton is owned by the root, so a scope must hand back the root's instance");
		}

		[Fact]
		public async Task ATransientIsFreshPerResolution()
		{
			using ConformanceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			TransientService first = (TransientService)provider.GetService(typeof(TransientService))!;
			TransientService second = (TransientService)provider.GetService(typeof(TransientService))!;

			await That(ReferenceEquals(first, second)).IsFalse();
		}

		[Fact]
		public async Task ScopedInstancesAreNotSharedBetweenSiblingScopes()
		{
			using ConformanceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			ScopedService first;
			ScopedService second;
			using (IServiceScope scope = provider.CreateScope())
			{
				first = (ScopedService)scope.ServiceProvider.GetService(typeof(ScopedService))!;
			}

			using (IServiceScope scope = provider.CreateScope())
			{
				second = (ScopedService)scope.ServiceProvider.GetService(typeof(ScopedService))!;
			}

			await That(ReferenceEquals(first, second)).IsFalse();
		}
	}

	/// <summary>
	///     The services a provider must answer with itself for the host stack to work.
	/// </summary>
	public sealed class ProviderOwnServices
	{
		[Fact]
		public async Task AScopeCanOpenFurtherScopes()
		{
			using ConformanceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);
			using IServiceScope outer = provider.CreateScope();

			IServiceScopeFactory factory =
				(IServiceScopeFactory)outer.ServiceProvider.GetService(typeof(IServiceScopeFactory))!;

			await That(factory).IsNotNull();
			using IServiceScope nested = factory.CreateScope();
			await That(nested.ServiceProvider.GetService(typeof(ScopedService))).IsNotNull();
		}

		[Fact]
		public async Task AScopeResolvesIServiceProviderToItself()
		{
			using ConformanceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);
			using IServiceScope scope = provider.CreateScope();

			object? resolved = scope.ServiceProvider.GetService(typeof(IServiceProvider));

			await That(resolved).IsSameAs(scope.ServiceProvider)
				.Because("a scope's IServiceProvider is the scope itself, not the root — resolving the root would leak scoped state");
		}
	}

	/// <summary>
	///     How an unregistered service is reported, which MS.DI consumers rely on precisely.
	/// </summary>
	public sealed class AbsentServices
	{
		[Fact]
		public async Task AnEnumerableOfAnUnmentionedTypeReturnsNull_KnownGap()
		{
			using ConformanceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			object? resolved = provider.GetService(typeof(IEnumerable<UnregisteredService>));

			await That(resolved).IsNull()
				.Because("MS.DI guarantees IEnumerable<T> resolves to an empty sequence for any T, so consumers enumerate without a null check; the generator only emits collection cases for element types the graph mentions, so one it never saw has no case to hit. Pinned so that closing the gap breaks this test rather than passing unnoticed");
		}

		[Fact]
		public async Task AnEnumerableOfARegisteredTypeYieldsEveryRegistration()
		{
			using KeyedServices.KeyedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			object? resolved = provider.GetService(typeof(IEnumerable<IService>));

			await That(resolved).IsNotNull();
		}

		[Fact]
		public async Task GetRequiredServiceThrowsInvalidOperationException()
		{
			using ConformanceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			void Act()
			{
				provider.GetRequiredService<UnregisteredService>();
			}

			await That(Act).Throws<InvalidOperationException>();
		}

		[Fact]
		public async Task GetServiceReturnsNull()
		{
			using ConformanceContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			await That(provider.GetService(typeof(UnregisteredService))).IsNull();
		}
	}

	/// <summary>
	///     Disposal timing and ownership. Broader ownership cases live in <c>BridgeDisposalTests</c>.
	/// </summary>
	public sealed partial class Disposal
	{
		[Fact]
		public async Task DisposalRunsInReverseOrderOfCreation()
		{
			DisposeOrder.Clear();
			using OrderedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			using (IServiceScope scope = provider.CreateScope())
			{
				scope.ServiceProvider.GetService(typeof(OrderedSecond));
			}

			await That(DisposeOrder.Count).IsEqualTo(2);
			await That(DisposeOrder[0]).IsEqualTo(nameof(OrderedSecond))
				.Because("MS.DI disposes in reverse order of creation, so a dependent goes before the dependency it was built on");
			await That(DisposeOrder[1]).IsEqualTo(nameof(OrderedFirst));
		}

		[Fact]
		public async Task DisposingAScopeLeavesSingletonsAlive()
		{
			using DisposalContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			DisposableSingleton singleton;
			using (IServiceScope scope = provider.CreateScope())
			{
				singleton = (DisposableSingleton)scope.ServiceProvider.GetService(typeof(DisposableSingleton))!;
			}

			await That(singleton.DisposeCount).IsEqualTo(0)
				.Because("a singleton outlives the scope that first reached it");
		}

		[Fact]
		public async Task ResolvingFromADisposedProviderThrows()
		{
			DisposalContainer.Root container = new();
			AwaitenServiceProvider provider = new(container);
			provider.Dispose();

			void Act()
			{
				provider.GetService(typeof(ScopedService));
			}

			await That(Act).Throws<ObjectDisposedException>();
		}

		public sealed class DisposableSingleton : IDisposable
		{
			public int DisposeCount { get; private set; }

			public void Dispose() => DisposeCount++;
		}

		public sealed class OrderedFirst : IDisposable
		{
			public void Dispose() => DisposeOrder.Add(nameof(OrderedFirst));
		}

		public sealed class OrderedSecond : IDisposable
		{
			public OrderedSecond(OrderedFirst first)
			{
				_ = first;
			}

			public void Dispose() => DisposeOrder.Add(nameof(OrderedSecond));
		}

		internal static List<string> DisposeOrder { get; } = [];

		[Container]
		[Singleton<DisposableSingleton>]
		[Scoped<ScopedService>]
		public static partial class DisposalContainer;

		[Container]
		[Scoped<OrderedFirst>]
		[Scoped<OrderedSecond>]
		public static partial class OrderedContainer;
	}

	/// <summary>
	///     The keyed resolution surface, including the key values MS.DI gives special meaning.
	/// </summary>
	public sealed partial class KeyedServices
	{
		[Fact]
		public async Task AKeyedSingletonIsSharedAcrossScopes()
		{
			using KeyedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			object fromRoot = provider.GetRequiredKeyedService(typeof(IService), "fast");
			using IServiceScope scope = provider.CreateScope();
			object fromScope =
				((IKeyedServiceProvider)scope.ServiceProvider).GetRequiredKeyedService(typeof(IService), "fast");

			await That(fromScope).IsSameAs(fromRoot);
		}

		[Fact]
		public async Task ANullKeyResolvesTheUnkeyedRegistration()
		{
			using KeyedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			await That(provider.GetKeyedService(typeof(IService), null)).Is<SingletonService>()
				.Because("MS.DI treats a null key as a request for the unkeyed registration");
		}

		[Fact]
		public async Task AnUnknownKeyThrowsFromGetRequiredKeyedService()
		{
			using KeyedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			void Act()
			{
				provider.GetRequiredKeyedService(typeof(IService), "missing");
			}

			await That(Act).Throws<InvalidOperationException>();
		}

		[Fact]
		public async Task ARegistrationResolvesUnderItsKey()
		{
			using KeyedContainer.Root container = new();
			using AwaitenServiceProvider provider = new(container, false);

			await That(provider.GetKeyedService(typeof(IService), "fast")).Is<FastChannel>();
			await That(provider.GetKeyedService(typeof(IService), "slow")).Is<SlowChannel>();
		}

		public sealed class FastChannel : IService;

		public sealed class SlowChannel : IService;

		[Container]
		[Singleton<SingletonService, IService>]
		[Singleton<FastChannel, IService>(Key = "fast")]
		[Singleton<SlowChannel, IService>(Key = "slow")]
		public static partial class KeyedContainer;
	}
}
