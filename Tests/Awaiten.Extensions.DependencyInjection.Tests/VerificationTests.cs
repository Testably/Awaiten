using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed partial class VerificationTests
{
	public interface IEvent;

	public sealed class DerivedEvent : IEvent;

	public interface IHandler<out TEvent>;

	public sealed class DerivedHandler : IHandler<DerivedEvent>;

	/// <summary>Consumes a variant closing that only the provider's variance fallback can satisfy.</summary>
	public sealed class Dispatcher
	{
		public Dispatcher(IHandler<IEvent> handler) => _ = handler;
	}

	[Container]
	[ImportService<IHandler<IEvent>>]
	[Singleton<Dispatcher>]
	public static partial class ImportingContainer;

	[Container]
	[Singleton<DerivedHandler, IHandler<DerivedEvent>>]
	public static partial class ProvidingContainer;

	public sealed class AsyncService : IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	public sealed class AsyncConsumer
	{
		public AsyncConsumer(AsyncService service) => _ = service;
	}

	/// <summary>Provides the import, but only asynchronously, so it is withheld from the synchronous path.</summary>
	[Container]
	[Singleton<AsyncService>]
	public static partial class AsyncProvidingContainer;

	[Container]
	[ImportService<AsyncService>]
	[Singleton<AsyncConsumer>]
	public static partial class AsyncImportingContainer;

	[Fact]
	public async Task VerifyAgainst_AnAwaitenProvider_AcceptsWhatThatProviderResolves()
	{
		// One container's imports satisfied by another container through the provider seam. The provider's own
		// feature-detection surface is consulted first, and a shape it does not report must still be accepted when
		// the provider resolves it, or a satisfiable import fails verification at startup.
		using ProvidingContainer.Root providing = new();
		using AwaitenServiceProvider provider = new(providing, ownsContainer: false);
		using ImportingContainer.Root importing = new();

		await That(provider.GetService(typeof(IHandler<IEvent>))).IsNotNull()
			.Because("the providing container satisfies the variant closing through its variance fallback");
		await That(() => ((IAwaitenContainerMetadata)importing).VerifyAgainst(provider)).DoesNotThrow();
	}

	[Fact]
	public async Task VerifyAgainst_AnAwaitenProviderThatWithholdsTheImport_SurfacesThatProvidersGuidance()
	{
		using AsyncProvidingContainer.Root providing = new();
		using AwaitenServiceProvider provider = new(providing, ownsContainer: false);
		using AsyncImportingContainer.Root importing = new();

		await That(() => ((IAwaitenContainerMetadata)importing).VerifyAgainst(provider))
			.DoesNotThrow()
			.Because("the import exists on the providing side, so verification passes rather than reporting it missing; reaching it synchronously is the consumer's problem");

		((IExternalResolverHost)importing).ExternalResolver = new ServiceProviderExternalResolver(provider);

		await That(() => importing.Resolve<AsyncConsumer>()).Throws<InvalidOperationException>()
			.WithMessage("*ResolveAsync*").AsWildcard()
			.Because("the providing container names why it withholds the import, and that reaches the consumer instead of the importing container's generic 'external dependency unavailable'");
	}

	[Fact]
	public async Task VerifyAgainst_AnAwaitenProvider_StillReportsWhatIsGenuinelyMissing()
	{
		using ConformanceOnlyContainer.Root providing = new();
		using AwaitenServiceProvider provider = new(providing, ownsContainer: false);
		using ImportingContainer.Root importing = new();

		await That(() => ((IAwaitenContainerMetadata)importing).VerifyAgainst(provider))
			.Throws<InvalidOperationException>().WithMessage("*IHandler*").AsWildcard();
	}

	[Container]
	[Singleton<DerivedEvent>]
	public static partial class ConformanceOnlyContainer;

	/// <summary>
	///     A provider that resolves a service its own feature detection denies. Contrived, but it is the shape of
	///     the hazard: any probe that under-reports would otherwise turn a satisfiable import into a startup
	///     failure, and no container in this repository under-reports a shape it resolves, so nothing else pins it.
	/// </summary>
	private sealed class UnderReportingProvider : IServiceProvider, IServiceProviderIsService
	{
		public bool IsService(Type serviceType) => false;

		public object? GetService(Type serviceType)
		{
			if (serviceType == typeof(IServiceProviderIsService))
			{
				return this;
			}

			return serviceType == typeof(IHandler<IEvent>) ? new DerivedHandler() : null;
		}
	}

	[Fact]
	public async Task VerifyAgainst_AProviderThatUnderReports_FallsBackToResolution()
	{
		using ImportingContainer.Root importing = new();

		await That(() => ((IAwaitenContainerMetadata)importing).VerifyAgainst(new UnderReportingProvider()))
			.DoesNotThrow()
			.Because("only a positive from the probe is final; a negative has to fall through to resolution, or verification reports an import the provider would have satisfied");
	}

	[Fact]
	public async Task VerifyAwaitenContainers_PassesWhenExternalDependencyIsRegistered()
	{
		ServiceCollection services = new();
		services.AddSingleton<ExternalDependencyTests.IClock>(new ExternalDependencyTests.FixedClock());
		services.AddGeneratedContainer<ExternalDependencyTests.ExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).DoesNotThrow();
	}

	[Fact]
	public async Task VerifyAwaitenContainers_ThrowsWhenExternalDependencyIsMissing()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<ExternalDependencyTests.ExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).Throws<InvalidOperationException>()
			.WithMessage("*IClock*").AsWildcard();
	}

	[Fact]
	public async Task VerifyAwaitenContainers_PassesWhenScopedExternalDependencyIsRegistered()
	{
		// A scoped external dependency is a registration like any other; the check runs against the root
		// provider and must not report it missing just because it cannot be resolved from the root itself.
		ServiceCollection services = new();
		services.AddScoped<ExternalDependencyTests.IClock>(_ => new ExternalDependencyTests.FixedClock());
		services.AddGeneratedContainer<ExternalDependencyTests.ScopedExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).DoesNotThrow();
	}

	[Fact]
	public async Task VerifyAwaitenContainers_PassesWhenKeyedExternalDependencyIsRegisteredUnderItsKey()
	{
		// Regression: a [FromKey] dependency resolves the keyed registration, so verification must probe the
		// keyed surface. An unkeyed IsService check would wrongly report the keyed registration missing.
		ServiceCollection services = new();
		services.AddKeyedSingleton<ExternalDependencyTests.IClock>("utc", new ExternalDependencyTests.FixedClock());
		services.AddGeneratedContainer<ExternalDependencyTests.KeyedExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).DoesNotThrow();
	}

	[Fact]
	public async Task VerifyAwaitenContainers_ThrowsWhenKeyedExternalDependencyIsRegisteredUnderTheWrongKey()
	{
		// An unkeyed registration and a differently-keyed one both leave the requested "utc" key unsatisfied.
		// Verification must not be fooled into passing (the false-negative direction of the same bug).
		ServiceCollection services = new();
		services.AddSingleton<ExternalDependencyTests.IClock>(new ExternalDependencyTests.FixedClock());
		services.AddKeyedSingleton<ExternalDependencyTests.IClock>("local", new ExternalDependencyTests.FixedClock());
		services.AddGeneratedContainer<ExternalDependencyTests.KeyedExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).Throws<InvalidOperationException>()
			.WithMessage("*IClock (key: utc)*").AsWildcard();
	}

	[Fact]
	public async Task VerifyAgainst_ThrowsListingTheMissingDependency()
	{
		ServiceCollection services = new();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
		using ExternalDependencyTests.ExternalContainer.Root container = new();

		await That(() => ((IAwaitenContainerMetadata)container).VerifyAgainst(provider))
			.Throws<InvalidOperationException>()
			.WithMessage(
				"Awaiten: the container's external dependencies are not registered in the provider: "
				+ "*IClock. Register them before building the provider, or remove the "
				+ "[ImportService<T>]/[ImportServices] usage.")
			.AsWildcard();
	}

	[Fact]
	public async Task VerifyAgainst_ListsTheKeyOfAMissingKeyedDependency()
	{
		ServiceCollection services = new();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
		using ExternalDependencyTests.KeyedExternalContainer.Root container = new();

		await That(() => ((IAwaitenContainerMetadata)container).VerifyAgainst(provider))
			.Throws<InvalidOperationException>()
			.WithMessage("*IClock (key: utc)*").AsWildcard();
	}

	[Fact]
	public async Task VerifyAgainst_NullContainer_Throws()
	{
		using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();

		await That(() => ((IAwaitenContainerMetadata)null!).VerifyAgainst(provider))
			.Throws<ArgumentNullException>();
	}

	[Fact]
	public async Task VerifyAgainst_NullProvider_Throws()
	{
		using ExternalDependencyTests.ExternalContainer.Root container = new();

		await That(() => ((IAwaitenContainerMetadata)container).VerifyAgainst(null!))
			.Throws<ArgumentNullException>();
	}

	[Fact]
	public async Task VerifyAwaitenContainers_NullProvider_Throws()
		=> await That(() => ((IServiceProvider)null!).VerifyAwaitenContainers()).Throws<ArgumentNullException>();

	// A provider that offers no IServiceProviderIs(Keyed)Service probe (a provider that predates it), so
	// verification must fall back to a resolution-based check.
	private sealed class ProbelessProvider : IServiceProvider
	{
		private readonly Func<Type, object?> _resolve;

		public ProbelessProvider(Func<Type, object?> resolve) => _resolve = resolve;

		public object? GetService(Type serviceType)
			=> serviceType == typeof(IServiceProviderIsService) || serviceType == typeof(IServiceProviderIsKeyedService)
				? null
				: _resolve(serviceType);
	}

	private sealed class ProbelessKeyedProvider : IKeyedServiceProvider
	{
		private readonly Func<Type, object?, object?> _resolve;

		public ProbelessKeyedProvider(Func<Type, object?, object?> resolve) => _resolve = resolve;

		public object? GetService(Type serviceType) => null;

		public object? GetKeyedService(Type serviceType, object? serviceKey) => _resolve(serviceType, serviceKey);

		public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
			=> _resolve(serviceType, serviceKey) ?? throw new InvalidOperationException();
	}

	[Fact]
	public async Task VerifyAgainst_ProviderWithoutProbe_FallsBackToResolution()
	{
		ProbelessProvider provider = new(_ => new ExternalDependencyTests.FixedClock());
		using ExternalDependencyTests.ExternalContainer.Root container = new();

		await That(() => ((IAwaitenContainerMetadata)container).VerifyAgainst(provider)).DoesNotThrow();
	}

	[Fact]
	public async Task VerifyAgainst_ProviderWithoutProbe_MissingDependency_Throws()
	{
		ProbelessProvider provider = new(_ => null);
		using ExternalDependencyTests.ExternalContainer.Root container = new();

		await That(() => ((IAwaitenContainerMetadata)container).VerifyAgainst(provider))
			.Throws<InvalidOperationException>()
			.WithMessage("*IClock*").AsWildcard();
	}

	[Fact]
	public async Task VerifyAgainst_ScopeRestrictedResolution_TreatedAsRegistered()
	{
		// A provider whose resolution throws InvalidOperationException (a scope-restricted service resolved from
		// the wrong scope) still proves the registration exists, so verification must not report it missing.
		ProbelessProvider provider = new(_ => throw new InvalidOperationException("scope-restricted"));
		using ExternalDependencyTests.ExternalContainer.Root container = new();

		await That(() => ((IAwaitenContainerMetadata)container).VerifyAgainst(provider)).DoesNotThrow();
	}

	[Fact]
	public async Task VerifyAgainst_KeyedProviderWithoutProbe_FallsBackToKeyedResolution()
	{
		ProbelessKeyedProvider provider = new((serviceType, key) =>
			serviceType == typeof(ExternalDependencyTests.IClock) && Equals(key, "utc")
				? new ExternalDependencyTests.FixedClock()
				: null);
		using ExternalDependencyTests.KeyedExternalContainer.Root container = new();

		await That(() => ((IAwaitenContainerMetadata)container).VerifyAgainst(provider)).DoesNotThrow();
	}
}
