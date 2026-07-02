using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     Disposal behaviour of the provider-replacement bridge (<see cref="AwaitenServiceProvider" /> /
///     <see cref="AwaitenServiceProviderFactory{TRoot}" />), where the Awaiten container is the single owner:
///     every instance - and everything built for it - is disposed exactly once. These pin the two cases the
///     collection projection (<see cref="AwaitenServiceCollectionExtensions.AddGeneratedContainer{TRoot}" />)
///     documents it does not guarantee: an implementation exposed under several service types, and a
///     disposable reached only as a nested dependency.
/// </summary>
public sealed partial class BridgeDisposalTests
{
	public interface IFirst;

	public interface ISecond;

	// One implementation exposed under two service types. Awaiten shares - and disposes - a single instance.
	public sealed class MultiService : IFirst, ISecond, IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	public sealed class InnerDependency : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	// Reaches InnerDependency only as a constructor dependency - it is never resolved on its own.
	public sealed class OuterService
	{
		public OuterService(InnerDependency inner) => Inner = inner;

		public InnerDependency Inner { get; }
	}

	[Container]
	[Singleton<MultiService, IFirst>]
	[Singleton<MultiService, ISecond>]
	public static partial class MultiServiceContainer;

	[Container]
	[Scoped<OuterService>]
	[Scoped<InnerDependency>]
	public static partial class NestedContainer;

	[Fact]
	public async Task ProviderReplacement_MultiServiceSingleton_DisposedExactlyOnce()
	{
		MultiServiceContainer.Root container = new();
		AwaitenServiceProvider provider = new(container);

		MultiService first = (MultiService)provider.GetService(typeof(IFirst))!;
		MultiService second = (MultiService)provider.GetService(typeof(ISecond))!;
		await That(first).IsSameAs(second)
			.Because("the two service types share one Awaiten singleton instance");

		provider.Dispose();

		await That(first.DisposeCount).IsEqualTo(1);
	}

	[Fact]
	public async Task ProviderReplacement_NestedScopedDependency_DisposedWithScope()
	{
		using NestedContainer.Root container = new();
		using AwaitenServiceProvider provider = new(container, ownsContainer: false);

		InnerDependency inner;
		using (IServiceScope scope = provider.CreateScope())
		{
			OuterService outer = (OuterService)scope.ServiceProvider.GetService(typeof(OuterService))!;
			inner = outer.Inner;
			await That(inner.DisposeCount).IsEqualTo(0);
		}

		await That(inner.DisposeCount).IsEqualTo(1);
	}
}
