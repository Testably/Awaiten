using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     The adapter from an MS.DI <see cref="IServiceProvider" /> to Awaiten's <see cref="IExternalResolver" />
///     seam: an unkeyed dependency resolves through the ordinary surface, a keyed one through the provider's keyed
///     surface, and a provider without keyed support simply yields nothing.
/// </summary>
public sealed class ServiceProviderExternalResolverTests
{
	private interface IClock;

	private sealed class SystemClock : IClock;

	// A provider that has no keyed surface at all, to pin the "provider without keyed support" branch.
	private sealed class NonKeyedProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}

	[Fact]
	public async Task Constructor_NullProvider_Throws()
		=> await That(() => _ = new ServiceProviderExternalResolver(null!)).Throws<ArgumentNullException>();

	[Fact]
	public async Task TryResolve_NullServiceType_Throws()
	{
		using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
		ServiceProviderExternalResolver resolver = new(provider);

		await That(() => resolver.TryResolve(null!, null, out _)).Throws<ArgumentNullException>();
	}

	[Fact]
	public async Task TryResolve_Unkeyed_ReturnsRegisteredInstance()
	{
		SystemClock clock = new();
		ServiceCollection services = new();
		services.AddSingleton<IClock>(clock);
		using ServiceProvider provider = services.BuildServiceProvider();
		ServiceProviderExternalResolver resolver = new(provider);

		bool resolved = resolver.TryResolve(typeof(IClock), null, out object? instance);

		await That(resolved).IsTrue();
		await That(instance).IsSameAs(clock);
	}

	[Fact]
	public async Task TryResolve_Unkeyed_ReturnsFalseWhenMissing()
	{
		using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
		ServiceProviderExternalResolver resolver = new(provider);

		bool resolved = resolver.TryResolve(typeof(IClock), null, out object? instance);

		await That(resolved).IsFalse();
		await That(instance).IsNull();
	}

	[Fact]
	public async Task TryResolve_Keyed_ReturnsKeyedInstance()
	{
		SystemClock utc = new();
		ServiceCollection services = new();
		services.AddKeyedSingleton<IClock>("utc", utc);
		using ServiceProvider provider = services.BuildServiceProvider();
		ServiceProviderExternalResolver resolver = new(provider);

		bool resolved = resolver.TryResolve(typeof(IClock), "utc", out object? instance);

		await That(resolved).IsTrue();
		await That(instance).IsSameAs(utc);
	}

	[Fact]
	public async Task TryResolve_Keyed_WhenProviderHasNoKeyedSupport_ReturnsFalse()
	{
		ServiceProviderExternalResolver resolver = new(new NonKeyedProvider());

		bool resolved = resolver.TryResolve(typeof(IClock), "utc", out object? instance);

		await That(resolved).IsFalse()
			.Because("a provider without keyed support yields no keyed instance");
		await That(instance).IsNull();
	}
}
