using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed class AwaitenServiceCollectionExtensionsTests
{
	[Test]
	public async Task AddAwaitenContainer_ShouldRegisterContainerAsSingleton()
	{
		ServiceCollection services = new();

		services.AddAwaitenContainer<DummyContainer>();

		ServiceProvider provider = services.BuildServiceProvider();
		IAwaitenScope container = provider.GetRequiredService<IAwaitenScope>();
		await That(container).Is<DummyContainer>();
		await That(provider.GetRequiredService<IAwaitenScope>()).IsSameAs(container);
	}

	[Test]
	public async Task AddAwaitenContainer_WhenServicesIsNull_ShouldThrowArgumentNullException()
	{
		void Act()
		{
			AwaitenServiceCollectionExtensions.AddAwaitenContainer<DummyContainer>(null!);
		}

		await That(Act).Throws<ArgumentNullException>().WithParamName("services");
	}

	[Test]
	public async Task AddAwaitenContainer_WithRoot_WhenServicesIsNull_ShouldThrowArgumentNullException()
	{
		void Act()
		{
			AwaitenServiceCollectionExtensions.AddAwaitenContainer(null!, new DummyContainer());
		}

		await That(Act).Throws<ArgumentNullException>().WithParamName("services");
	}

	[Test]
	public async Task AddAwaitenContainer_WithRoot_WhenRootIsNull_ShouldThrowArgumentNullException()
	{
		void Act()
		{
			new ServiceCollection().AddAwaitenContainer<DummyContainer>(null!);
		}

		await That(Act).Throws<ArgumentNullException>().WithParamName("root");
	}

	[Test]
	public async Task AddAwaitenContainer_ShouldReturnBuilderOverSameServices()
	{
		ServiceCollection services = new();

		AwaitenContainerBuilder<DummyContainer> builder = services.AddAwaitenContainer<DummyContainer>();

		await That(builder.Services).IsSameAs(services);
	}

	[Test]
	public async Task AddAwaitenContainerExtensions_ShouldBeAStaticClass()
	{
		Type type = typeof(AwaitenServiceCollectionExtensions);

		await That(type is { IsAbstract: true, IsSealed: true, }).IsTrue();
	}

	private sealed class DummyContainer : IAwaitenRoot
	{
		public System.Collections.Generic.IReadOnlyList<AwaitenRegistration> Registrations
			=> System.Array.Empty<AwaitenRegistration>();

		public System.Collections.Generic.IReadOnlyList<AwaitenExternalDependency> ExternalDependencies
			=> System.Array.Empty<AwaitenExternalDependency>();

		public IExternalResolver? ExternalResolver { get; private set; }

		public void SetExternalResolver(IExternalResolver resolver) => ExternalResolver = resolver;

		public AwaitenResolvability GetResolvability(Type serviceType, object? key) => default;

		public object Resolve(Type serviceType) => throw new NotSupportedException();

		public object Resolve(Type serviceType, object? key) => throw new NotSupportedException();

		public bool TryResolve(Type serviceType, [NotNullWhen(true)] out object? instance)
		{
			instance = null;
			return false;
		}

		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
		{
			instance = null;
			return false;
		}

		public IAwaitenScope CreateScope() => throw new NotSupportedException();

		public Task<object> ResolveAsync(Type serviceType, System.Threading.CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<object> ResolveAsync(Type serviceType, object? key, System.Threading.CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task InitializeAsync(System.Threading.CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<IAwaitenScope> CreateScopeAsync(System.Threading.CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public void Dispose()
		{
		}
	}
}
