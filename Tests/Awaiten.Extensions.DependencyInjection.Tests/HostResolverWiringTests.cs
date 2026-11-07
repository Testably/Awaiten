using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed partial class HostResolverWiringTests
{
	public interface IClock;

	public sealed class FixedClock : IClock;

	public sealed class Reporter
	{
		public Reporter(IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	public sealed class Menu;

	[Container]
	[ImportService<IClock>]
	[Transient<Reporter>]
	public static partial class ImportContainer;

	[Container]
	[ImportServices]
	[Singleton<Menu>]
	public static partial class ImportServicesContainer;

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
	public async Task ProviderFactory_WiresTheRootBeforeAnythingResolves()
	{
		FixedClock clock = new();
		ServiceCollection services = new();
		services.AddSingleton<IClock>(clock);
		AwaitenServiceProviderFactory<ImportContainer.Root> factory = new();
		ImportContainer.Root root = factory.CreateBuilder(services);
		using ServiceProvider provider = (ServiceProvider)factory.CreateServiceProvider(root);

		await That(root.Resolve<IClock>()).IsSameAs(clock)
			.Because("the factory builds the host provider and wires the root to it at once, so a direct resolve needs no bridged resolve first");
	}

	[Test]
	public async Task ProviderFactory_WiresARootWhoseGraphInjectsNoImport()
	{
		FixedClock clock = new();
		ServiceCollection services = new();
		services.AddSingleton<IClock>(clock);
		AwaitenServiceProviderFactory<ImportServicesContainer.Root> factory = new();
		ImportServicesContainer.Root root = factory.CreateBuilder(services);
		using ServiceProvider provider = (ServiceProvider)factory.CreateServiceProvider(root);

		await That(root.Resolve<IClock>()).IsSameAs(clock)
			.Because("[ImportServices] routes a direct resolve to the host although no dependency is imported");
	}

	[Test]
	public async Task ProviderFactory_KeepsAResolverSetOnTheRoot()
	{
		FixedClock hostClock = new();
		FixedClock ownClock = new();
		ServiceCollection services = new();
		services.AddSingleton<IClock>(hostClock);
		AwaitenServiceProviderFactory<ImportContainer.Root> factory = new();
		ImportContainer.Root root = factory.CreateBuilder(services);
		((IAwaitenExternalResolverHost)root).SetExternalResolver(new FixedResolver(ownClock));
		using ServiceProvider provider = (ServiceProvider)factory.CreateServiceProvider(root);

		await That(root.Resolve<IClock>()).IsSameAs(ownClock)
			.Because("a host configures the root before the provider is built, and the factory leaves that resolver alone");
	}
}
