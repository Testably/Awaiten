using System;
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
		public TimeReporter([FromServices] IClock clock) => _clock = clock;

		public string Report() => _clock.Now;
	}

	[Container]
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
		public bool TryResolve(Type serviceType, out object? instance)
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

	[Fact]
	public async Task AwaitenService_ResolvesMsDiOwnedDependencyAcrossTheSeam()
	{
		ServiceCollection services = new();
		services.AddSingleton<IClock>(new FixedClock());
		services.AddGeneratedContainer<ExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		TimeReporter reporter = provider.GetRequiredService<TimeReporter>();

		await That(reporter.Report()).IsEqualTo("noon");
	}

	[Fact]
	public async Task ImportServices_ResolvesUnannotatedDependencyFromHost()
	{
		ServiceCollection services = new();
		services.AddSingleton<IClock>(new FixedClock());
		services.AddGeneratedContainer<ImportingContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		ImplicitReporter reporter = provider.GetRequiredService<ImplicitReporter>();

		await That(reporter.Report()).IsEqualTo("noon");
	}

	[Fact]
	public async Task MissingExternalRegistration_ThrowsWhenResolved()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<ExternalContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.GetRequiredService<TimeReporter>()).Throws<InvalidOperationException>();
	}

	[Fact]
	public async Task StandaloneContainer_UsesExplicitlyWiredExternalResolver()
	{
		using ExternalContainer.Root container = new();
		container.ExternalResolver = new ClockResolver();

		TimeReporter reporter = container.Resolve<TimeReporter>();

		await That(reporter.Report()).IsEqualTo("noon");
	}

	[Fact]
	public async Task Container_AdvertisesExternalDependencies()
	{
		using ExternalContainer.Root container = new();

		await That(((IAwaitenContainerMetadata)container).ExternalDependencies).Contains(typeof(IClock));
	}
}
