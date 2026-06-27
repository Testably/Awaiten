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
		IAwaitenContainer container = provider.GetRequiredService<IAwaitenContainer>();
		await That(container).Is<DummyContainer>();
		await That(provider.GetRequiredService<IAwaitenContainer>()).IsSameAs(container);
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

	private sealed class DummyContainer : IAwaitenContainer
	{
		public object Resolve(Type serviceType) => throw new NotSupportedException();

		public bool TryResolve(Type serviceType, [NotNullWhen(true)] out object? instance)
		{
			instance = null;
			return false;
		}

		public IAwaitenScope CreateScope() => throw new NotSupportedException();

		public void Dispose()
		{
		}
	}
}
