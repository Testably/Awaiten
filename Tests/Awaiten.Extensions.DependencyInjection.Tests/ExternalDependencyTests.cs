using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed partial class ExternalDependencyTests
{
	public interface IClock
	{
		string Now { get; }
	}

	public sealed class FixedClock : IClock
	{
		public string Now => "noon";
	}

	public sealed class TimeReporter
	{
		private readonly IClock _clock;

		// Satisfied from the external (host-owned) provider, not the Awaiten graph.
		public TimeReporter(IClock clock) => _clock = clock;

		public string Report() => _clock.Now;
	}

	[Container]
	[ImportService<IClock>]
	[Singleton<TimeReporter>]
	public static partial class ExternalContainer;

	// [ImportServices]: the unannotated IClock dependency falls through to the external provider.
	public sealed class ImplicitReporter
	{
		private readonly IClock _clock;

		public ImplicitReporter(IClock clock) => _clock = clock;

		public string Report() => _clock.Now;
	}

	[Container]
	[ImportServices]
	[Singleton<ImplicitReporter>]
	public static partial class ImportingContainer;

	private sealed class ClockResolver : IExternalResolver
	{
		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
		{
			if (serviceType == typeof(IClock))
			{
				instance = new FixedClock();
				return true;
			}

			instance = null;
			return false;
		}
	}

	[Test]
	public async Task AwaitenService_ResolvesMsDiOwnedDependencyAcrossTheSeam()
	{
		ServiceCollection services = new();
		services.AddSingleton<IClock>(new FixedClock());
		services.AddAwaitenContainer<ExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		TimeReporter reporter = provider.GetRequiredService<TimeReporter>();

		await That(reporter.Report()).IsEqualTo("noon");
	}

	[Test]
	public async Task ImportServices_ResolvesUnannotatedDependencyFromHost()
	{
		ServiceCollection services = new();
		services.AddSingleton<IClock>(new FixedClock());
		services.AddAwaitenContainer<ImportingContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		ImplicitReporter reporter = provider.GetRequiredService<ImplicitReporter>();

		await That(reporter.Report()).IsEqualTo("noon");
	}

	public interface IUnregistered;

	public sealed class ResolvingAnImportedService
	{
		[Test]
		public async Task FromTheRoot_ReturnsTheHostsInstance()
		{
			FixedClock clock = new();
			ServiceCollection services = new();
			services.AddSingleton<IClock>(clock);
			services.AddAwaitenContainer<ExternalContainer.Root>();
			using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
			_ = provider.GetRequiredService<TimeReporter>();
			ExternalContainer.Root root = provider.GetRequiredService<ExternalContainer.Root>();

			await That(root.Resolve<IClock>()).IsSameAs(clock)
				.Because("the bridge wires the root to the host provider, which a typed resolution of an imported type asks");
		}

		[Test]
		public async Task ThroughAnAwaitenServiceProvider_ReturnsTheHostsInstance()
		{
			FixedClock clock = new();
			ServiceCollection services = new();
			services.AddSingleton<IClock>(clock);
			services.AddAwaitenContainer<ExternalContainer.Root>();
			using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
			_ = provider.GetRequiredService<TimeReporter>();
			using AwaitenServiceProvider awaiten = new(provider.GetRequiredService<ExternalContainer.Root>(), ownsContainer: false);

			await That(awaiten.GetService(typeof(IClock))).IsSameAs(clock)
				.Because("GetService reaches the host through the container's TryResolve");
		}

		[Test]
		public async Task UnderImportServices_AnUnknownTypeDoesNotLoopBetweenTheProviders()
		{
			ServiceCollection services = new();
			services.AddSingleton<IClock>(new FixedClock());
			services.AddAwaitenContainer<ImportingContainer.Root>();
			using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
			_ = provider.GetRequiredService<ImplicitReporter>();
			ImportingContainer.Root root = provider.GetRequiredService<ImportingContainer.Root>();
			using AwaitenServiceProvider awaiten = new(root, ownsContainer: false);

			await That(() => root.Resolve<IUnregistered>()).Throws<InvalidOperationException>()
				.WithMessage("*external dependency*IUnregistered*is not available*").AsWildcard()
				.Because("the host provider has no projection of an unregistered type, so it answers null instead of asking the container back");
			await That(awaiten.GetService(typeof(IUnregistered))).IsNull();
			await That(provider.GetService<IUnregistered>()).IsNull();
		}
	}

	public sealed class Menu;

	[Container]
	[ImportService<IClock>]
	[Singleton<Menu>]
	public static partial class UninjectedImportContainer;

	[Container]
	[ImportServices]
	[Singleton<Menu>]
	public static partial class UninjectedImportServicesContainer;

	public sealed class ResolvingAnImportNothingInjects
	{
		[Test]
		public async Task FromTheRoot_ReturnsTheHostsInstance()
		{
			FixedClock clock = new();
			ServiceCollection services = new();
			services.AddSingleton<IClock>(clock);
			services.AddAwaitenContainer<UninjectedImportContainer.Root>();
			using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
			_ = provider.GetRequiredService<Menu>();
			UninjectedImportContainer.Root root = provider.GetRequiredService<UninjectedImportContainer.Root>();

			await That(root.Resolve<IClock>()).IsSameAs(clock)
				.Because("a declared import is reachable through a direct resolve, so the bridge wires the root to the host even though nothing in the graph injects it");
		}

		[Test]
		public async Task ThroughAnAwaitenServiceProvider_ReturnsTheHostsInstance()
		{
			FixedClock clock = new();
			ServiceCollection services = new();
			services.AddSingleton<IClock>(clock);
			services.AddAwaitenContainer<UninjectedImportContainer.Root>();
			using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
			_ = provider.GetRequiredService<Menu>();
			using AwaitenServiceProvider awaiten = new(provider.GetRequiredService<UninjectedImportContainer.Root>(), ownsContainer: false);

			await That(awaiten.GetService(typeof(IClock))).IsSameAs(clock)
				.Because("GetService reaches the host through the root the bridge wired");
		}

		[Test]
		public async Task UnderImportServices_FromTheRoot_ReturnsTheHostsInstance()
		{
			FixedClock clock = new();
			ServiceCollection services = new();
			services.AddSingleton<IClock>(clock);
			services.AddAwaitenContainer<UninjectedImportServicesContainer.Root>();
			using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
			_ = provider.GetRequiredService<Menu>();
			UninjectedImportServicesContainer.Root root = provider.GetRequiredService<UninjectedImportServicesContainer.Root>();

			await That(root.Resolve<IClock>()).IsSameAs(clock)
				.Because("[ImportServices] routes a direct resolve of an unregistered type to the host, so the bridge wires the root although no dependency is imported");
		}

		[Test]
		public async Task IsAdvertisedAsAnExternalDependency()
		{
			using UninjectedImportContainer.Root container = new();

			await That(((IAwaitenRoot)container).ExternalDependencies)
				.IsEqualTo([new AwaitenExternalDependency(typeof(IClock)),])
				.Because("declaring the import is the contract that the host provides it, so startup verification checks it");
		}
	}

	[Test]
	public async Task MissingExternalRegistration_ThrowsWhenResolved()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<ExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.GetRequiredService<TimeReporter>()).Throws<InvalidOperationException>();
	}

	[Test]
	public async Task StandaloneContainer_UsesExplicitlyWiredExternalResolver()
	{
		using ExternalContainer.Root container = new();
		((IAwaitenExternalResolverHost)container).SetExternalResolver(new ClockResolver());

		TimeReporter reporter = container.Resolve<TimeReporter>();

		await That(reporter.Report()).IsEqualTo("noon");
	}

	[Test]
	public async Task Container_AdvertisesExternalDependencies()
	{
		using ExternalContainer.Root container = new();

		await That(((IAwaitenRoot)container).ExternalDependencies)
			.Contains(new AwaitenExternalDependency(typeof(IClock)));
	}

	[Test]
	public async Task Container_AdvertisesTheKeyOfAKeyedExternalDependency()
	{
		using KeyedExternalContainer.Root container = new();

		await That(((IAwaitenRoot)container).ExternalDependencies)
			.Contains(new AwaitenExternalDependency(typeof(IClock), "utc"));
	}

	private sealed class DisposableClock : IClock, IDisposable
	{
		public bool Disposed { get; private set; }

		public string Now => "noon";

		public void Dispose() => Disposed = true;
	}

	private sealed class FixedResolver : IExternalResolver
	{
		private readonly IClock _clock;

		public FixedResolver(IClock clock) => _clock = clock;

		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
		{
			instance = serviceType == typeof(IClock) ? _clock : null;
			return instance is not null;
		}
	}

	[Test]
	public async Task ExternallySuppliedInstance_IsNotDisposedWithTheContainer()
	{
		DisposableClock clock = new();
		using (ExternalContainer.Root container = new())
		{
			((IAwaitenExternalResolverHost)container).SetExternalResolver(new FixedResolver(clock));
			_ = container.Resolve<TimeReporter>();
		}

		// An externally supplied instance stays owned by whoever provided it; the container never captures
		// it for disposal.
		await That(clock.Disposed).IsFalse();
	}

	[Test]
	public async Task ChildScope_FallsBackToTheRootsExternalResolver()
	{
		using ScopedExternalContainer.Root container = new();
		((IAwaitenExternalResolverHost)container).SetExternalResolver(new ClockResolver());
		using IAwaitenScope scope = container.CreateScope();

		// The child scope has no resolver of its own, so its external dependency routes through the root's.
		await That(scope.Resolve<ScopedReporter>().Clock).IsNotNull();
	}

	public sealed class ScopedReporter
	{
		// A scoped Awaiten service whose external dependency is a scoped host service.
		public ScopedReporter(IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	[Container]
	[ImportService<IClock>]
	[Scoped<ScopedReporter>]
	public static partial class ScopedExternalContainer;

	[Test]
	public async Task ScopedExternalDependency_ResolvesFromTheAlignedScope()
	{
		ServiceCollection services = new();
		services.AddScoped<IClock>(_ => new FixedClock());
		services.AddAwaitenContainer<ScopedExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		using IServiceScope scope1 = provider.CreateScope();
		using IServiceScope scope2 = provider.CreateScope();

		ScopedReporter first = scope1.ServiceProvider.GetRequiredService<ScopedReporter>();
		ScopedReporter second = scope2.ServiceProvider.GetRequiredService<ScopedReporter>();

		// The scoped host dependency resolves from each aligned MS.DI scope's provider (not the root, which
		// would throw under validateScopes), so the two scopes see distinct instances.
		await That(first.Clock).IsNotSameAs(second.Clock);
		await That(scope1.ServiceProvider.GetRequiredService<IClock>()).IsSameAs(first.Clock);
	}

	public sealed class ReporterUser
	{
		private readonly Func<Owned<ScopedReporter>> _factory;

		public ReporterUser(Func<Owned<ScopedReporter>> factory) => _factory = factory;

		public IClock ResolveOwnedClock()
		{
			using Owned<ScopedReporter> owned = _factory();
			return owned.Value.Clock;
		}
	}

	[Container]
	[ImportService<IClock>]
	[Scoped<ScopedReporter>]
	[Scoped<ReporterUser>]
	public static partial class OwnedExternalContainer;

	[Test]
	public async Task OwnedResolution_UsesTheCreatingScopesExternalResolver()
	{
		ServiceCollection services = new();
		services.AddScoped<IClock>(_ => new FixedClock());
		services.AddAwaitenContainer<OwnedExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		using IServiceScope scope = provider.CreateScope();
		ReporterUser user = scope.ServiceProvider.GetRequiredService<ReporterUser>();

		// The throwaway Owned scope inherits the creating scope's external resolver, so the scoped host
		// service resolves from the aligned MS.DI scope (resolving it from the root provider would throw
		// under validateScopes) and is the same instance the scope itself sees.
		await That(user.ResolveOwnedClock()).IsSameAs(scope.ServiceProvider.GetRequiredService<IClock>());
	}

	public sealed class KeyedReporter
	{
		// The [FromKey] selects the keyed external service; the key is forwarded to the resolver.
		public KeyedReporter([FromKey("utc")] IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	[Container]
	[ImportService<IClock>]
	[Singleton<KeyedReporter>]
	public static partial class KeyedExternalContainer;

	[Test]
	public async Task KeyedExternalDependency_ResolvesTheKeyedHostService()
	{
		FixedClock utc = new();
		ServiceCollection services = new();
		services.AddKeyedSingleton<IClock>("local", new FixedClock());
		services.AddKeyedSingleton<IClock>("utc", utc);
		services.AddAwaitenContainer<KeyedExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		KeyedReporter reporter = provider.GetRequiredService<KeyedReporter>();

		await That(reporter.Clock).IsSameAs(utc);
	}
}
