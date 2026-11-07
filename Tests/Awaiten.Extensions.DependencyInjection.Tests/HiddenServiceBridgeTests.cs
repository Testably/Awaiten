using System.Linq;
using Awaiten.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     The bridge never sees the generated handles of the services a module of another assembly registers under internal
///     types: they are dependencies of the container only, while the public services that need them project as usual.
/// </summary>
public sealed partial class HiddenServiceBridgeTests
{
	private static readonly Type[] Handles = typeof(CrossAssemblyHiddenServiceModule.Internals).GetNestedTypes()
		.Where(type => type.Name.StartsWith("Hidden_", StringComparison.Ordinal))
		.ToArray();

	[Container]
	[Import(typeof(CrossAssemblyHiddenServiceModule))]
	public static partial class HiddenServiceContainer;

	[Test]
	public async Task Projection_LeavesTheHandlesOut()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<HiddenServiceContainer.Root>();

		await That(Handles).IsNotEmpty();
		await That(services.Select(descriptor => descriptor.ServiceType).Intersect(Handles)).IsEmpty()
			.Because("a handle is not a registration, so the projection does not add it to the service collection");
		await That(services.Select(descriptor => descriptor.ServiceType)).Contains(typeof(IHiddenGrinder));
	}

	[Test]
	public async Task Provider_ResolvesThePublicServiceAndDisposesTheInternalOne()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<HiddenServiceContainer.Root>();
		IHiddenGrinder grinder;
		using (ServiceProvider provider = services.BuildServiceProvider(validateScopes: true))
		{
			grinder = provider.GetRequiredService<IHiddenGrinder>();
			await That(provider.GetService<IServiceProviderIsService>()!.IsService(Handles[0])).IsFalse()
				.Because("a handle is not a service of the provider");
		}

		await That(grinder.StoreDisposed).IsTrue()
			.Because("the container disposes the internal service through its handle when the provider is disposed");
	}

	[Test]
	public async Task AwaitenServiceProvider_DoesNotResolveAHandle()
	{
		await using AwaitenServiceProvider provider = new(new HiddenServiceContainer.Root());

		await That(provider.GetService(Handles[0])).IsNull()
			.Because("a handle is not offered for resolution, so the provider reports it as missing");
		await That(provider.GetService<IHiddenGrinder>()).IsNotNull();
	}
}
