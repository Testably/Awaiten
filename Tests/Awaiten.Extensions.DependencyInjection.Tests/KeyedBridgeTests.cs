using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     Keyed resolution across the Microsoft.Extensions.DependencyInjection bridge: keyed Awaiten registrations are
///     projected as keyed MS.DI descriptors (so <c>[FromKeyedServices]</c> and <c>GetKeyedService</c> reach them),
///     and an <see cref="AwaitenServiceProvider" /> serves them directly through <see cref="IKeyedServiceProvider" />.
/// </summary>
public sealed partial class KeyedBridgeTests
{
	public interface IChannel;

	public sealed class FastChannel : IChannel;

	public sealed class SlowChannel : IChannel;

	public sealed class DefaultChannel : IChannel;

	// A host-registered consumer that selects a keyed Awaiten registration through [FromKeyedServices].
	public sealed class Router
	{
		public Router([FromKeyedServices("fast")] IChannel primary, [FromKeyedServices("slow")] IChannel backup)
		{
			Primary = primary;
			Backup = backup;
		}

		public IChannel Primary { get; }

		public IChannel Backup { get; }
	}

	[Container]
	[Singleton<DefaultChannel, IChannel>]
	[Singleton<FastChannel, IChannel>(Key = "fast")]
	[Singleton<SlowChannel, IChannel>(Key = "slow")]
	public static partial class KeyedContainer;

	[Fact]
	public async Task AddGeneratedContainer_ProjectsKeyedRegistrations_ForGetKeyedService()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<KeyedContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(provider.GetRequiredKeyedService<IChannel>("fast")).Is<FastChannel>();
		await That(provider.GetRequiredKeyedService<IChannel>("slow")).Is<SlowChannel>();
		await That(provider.GetRequiredService<IChannel>()).Is<DefaultChannel>()
			.Because("the unkeyed registration is still projected as the default service");
	}

	[Fact]
	public async Task AddGeneratedContainer_KeyedRegistration_FlowsIntoFromKeyedServicesConsumer()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<KeyedContainer.Root>();
		services.AddSingleton<Router>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		Router router = provider.GetRequiredService<Router>();

		await That(router.Primary).Is<FastChannel>()
			.Because("[FromKeyedServices(\"fast\")] selects the Awaiten registration keyed 'fast'");
		await That(router.Backup).Is<SlowChannel>()
			.Because("[FromKeyedServices(\"slow\")] selects the Awaiten registration keyed 'slow'");
	}

	[Fact]
	public async Task AddGeneratedContainer_UnknownKey_ReturnsNull()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<KeyedContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(provider.GetKeyedService<IChannel>("unknown")).IsNull();
	}

	[Fact]
	public async Task AwaitenServiceProvider_ServesKeyedRegistrationsThroughIKeyedServiceProvider()
	{
		await using AwaitenServiceProvider provider = new(new KeyedContainer.Root());

		await That(provider.GetRequiredKeyedService<IChannel>("fast")).Is<FastChannel>();
		await That(provider.GetRequiredKeyedService<IChannel>("slow")).Is<SlowChannel>();
		await That(provider.GetKeyedService<IChannel>("unknown")).IsNull()
			.Because("an unknown key resolves nothing");
		await That(provider.GetRequiredService<IChannel>()).Is<DefaultChannel>()
			.Because("a null key falls back to the unkeyed registration");
	}

	[Fact]
	public async Task AwaitenServiceProvider_AnyKey_IsDeclined()
	{
		await using AwaitenServiceProvider provider = new(new KeyedContainer.Root());

		await That(provider.GetKeyedService<IChannel>(KeyedService.AnyKey)).IsNull()
			.Because("Awaiten has no wildcard-key semantics, so AnyKey matches nothing");
	}

	[Fact]
	public async Task Registrations_ExposeTheUserKeyOfKeyedRegistrations()
	{
		KeyedContainer.Root root = new();
		System.Collections.Generic.IReadOnlyList<AwaitenRegistration> registrations =
			((IAwaitenContainerMetadata)root).Registrations;

		await That(registrations.Any(r => r.ServiceType == typeof(IChannel) && Equals(r.Key, "fast"))).IsTrue()
			.Because("the keyed 'fast' registration advertises its user key");
		await That(registrations.Any(r => r.ServiceType == typeof(IChannel) && Equals(r.Key, "slow"))).IsTrue()
			.Because("the keyed 'slow' registration advertises its user key");
		await That(registrations.Any(r => r.ServiceType == typeof(IChannel) && r.Key is null)).IsTrue()
			.Because("the unkeyed registration advertises a null key");
	}
}
