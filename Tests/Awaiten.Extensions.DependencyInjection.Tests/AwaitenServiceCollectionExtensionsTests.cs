using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed class AwaitenServiceCollectionExtensionsTests
{
	[Fact]
	public async Task AddGeneratedContainer_ShouldRegisterContainerAsSingleton()
	{
		ServiceCollection services = new();

		services.AddGeneratedContainer<DummyContainer>();

		ServiceProvider provider = services.BuildServiceProvider();
		IAwaitenScope container = provider.GetRequiredService<IAwaitenScope>();
		await That(container).Is<DummyContainer>();
		await That(provider.GetRequiredService<IAwaitenScope>()).IsSameAs(container);
	}

	[Fact]
	public async Task AddGeneratedContainer_WhenServicesIsNull_ShouldThrowArgumentNullException()
	{
		void Act()
		{
			AwaitenServiceCollectionExtensions.AddGeneratedContainer<DummyContainer>(null!);
		}

		await That(Act).Throws<ArgumentNullException>().WithParamName("services");
	}

	[Fact]
	public async Task AddGeneratedContainerExtensions_ShouldBeAStaticClass()
	{
		Type type = typeof(AwaitenServiceCollectionExtensions);

		await That(type is { IsAbstract: true, IsSealed: true, }).IsTrue();
	}

	private sealed class DummyContainer : IAwaitenContainerMetadata
	{
		public System.Collections.Generic.IReadOnlyList<AwaitenRegistration> Registrations
			=> System.Array.Empty<AwaitenRegistration>();

		public System.Collections.Generic.IReadOnlyList<AwaitenExternalDependency> ExternalDependencies
			=> System.Array.Empty<AwaitenExternalDependency>();

		public IExternalResolver? ExternalResolver { get; set; }

		public bool IsResolvable(Type serviceType, object? key) => false;

		public string? WithheldReason(Type serviceType, object? key) => null;

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
