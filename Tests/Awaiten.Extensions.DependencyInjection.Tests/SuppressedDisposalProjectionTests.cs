using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     A service built with <c>SuppressDisposal = true</c> must never be disposed by its host. MS.DI disposes every
///     disposable a factory descriptor returns, so the projection rejects such a service at startup (and AWT214
///     reports it at build time). Async registrations and non-disposable services stay projectable, and an
///     <see cref="AwaitenServiceProvider" /> honours the suppression.
/// </summary>
public sealed partial class SuppressedDisposalProjectionTests
{
	[Test]
	public async Task Projection_OfASuppressedDisposableSingleton_ThrowsBeforeRegisteringAnything()
	{
		ServiceCollection services = new();

#pragma warning disable AWT214
		await That(() => services.AddAwaitenContainer<SuppressedSingletonContainer.Root>())
			.Throws<InvalidOperationException>()
			.WithMessage("*Pooled*SuppressDisposal*Instance*AwaitenServiceProvider*").AsWildcard()
			.Because("MS.DI would dispose the instance the container promised not to dispose, so the projection fails at startup with the remedies");
#pragma warning restore AWT214

		await That(services).IsEmpty()
			.Because("the check runs before the projection registers anything");
	}

	[Test]
	public async Task Projection_OfASuppressedDisposableKeyedTransient_NamesTheKey()
	{
		ServiceCollection services = new();

#pragma warning disable AWT214
		await That(() => services.AddAwaitenContainer<SuppressedKeyedTransientContainer.Root>())
			.Throws<InvalidOperationException>()
			.WithMessage("*Pooled*key 'pool'*").AsWildcard();
#pragma warning restore AWT214
	}

	[Test]
	public async Task Projection_OfASuppressedFactoryThatMayReturnADisposable_Throws()
	{
		ServiceCollection services = new();

#pragma warning disable AWT214
		await That(() => services.AddAwaitenContainer<SuppressedFactoryContainer.Root>())
			.Throws<InvalidOperationException>()
			.WithMessage("*IBuffer*SuppressDisposal*").AsWildcard()
			.Because("the factory's declared interface can hide a disposable implementation, which MS.DI would dispose");
#pragma warning restore AWT214
	}

	[Test]
	public async Task HostFactory_OverASuppressedDisposableSingleton_Throws()
	{
#pragma warning disable AWT214
		AwaitenServiceProviderFactory<SuppressedSingletonContainer.Root> factory = new();
#pragma warning restore AWT214
		SuppressedSingletonContainer.Root root = factory.CreateBuilder(new ServiceCollection());

		await That(() => factory.CreateServiceProvider(root)).Throws<InvalidOperationException>()
			.WithMessage("*Pooled*SuppressDisposal*").AsWildcard()
			.Because("the host factory builds a standard MS.DI provider over the same projection");
	}

	[Test]
	public async Task Projection_OfASuppressedNonDisposableService_IsProjected()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<SuppressedTicketContainer.Root>();

		await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(provider.GetService<Ticket>()).IsNotNull()
			.Because("MS.DI never disposes a non-disposable instance, so nothing can override the suppression");
	}

	[Test]
	public async Task Projection_OfASuppressedAsyncService_IsProjectedAndNeverDisposed()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<SuppressedAsyncContainer.Root>();
		AsyncPooled pooled;

		await using (ServiceProvider provider = services.BuildServiceProvider(validateScopes: true))
		{
			pooled = await provider.GetRequiredService<Task<AsyncPooled>>();
		}

		await That(pooled.DisposeCount).IsEqualTo(0)
			.Because("MS.DI only captures the projected Task, and the container does not dispose a suppressed instance");
	}

	[Test]
	public async Task AwaitenServiceProvider_OverASuppressedDisposableSingleton_NeverDisposesIt()
	{
		Pooled pooled;
		await using (AwaitenServiceProvider provider = new(new SuppressedSingletonContainer.Root()))
		{
			pooled = (Pooled)provider.GetService(typeof(Pooled))!;
		}

		await That(pooled.DisposeCount).IsEqualTo(0)
			.Because("the container is the single owner on this path, so the suppression holds");
	}

	public sealed class Pooled : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	public interface IBuffer;

	private sealed class Buffer : IBuffer, IDisposable
	{
		public void Dispose()
		{
		}
	}

	public sealed class Ticket;

	public sealed class AsyncPooled : IAsyncInitializable, IDisposable
	{
		public int DisposeCount { get; private set; }

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public void Dispose() => DisposeCount++;
	}

	[Container]
	[Singleton<Pooled>(SuppressDisposal = true)]
	public static partial class SuppressedSingletonContainer;

	[Container]
	[Transient<Pooled>(SuppressDisposal = true, Key = "pool")]
	public static partial class SuppressedKeyedTransientContainer;

	[Container]
	[Singleton<IBuffer>(Factory = nameof(Make), SuppressDisposal = true)]
	public static partial class SuppressedFactoryContainer
	{
		private static IBuffer Make() => new Buffer();
	}

	[Container]
	[Singleton<Ticket>(SuppressDisposal = true, OnRelease = nameof(Release))]
	public static partial class SuppressedTicketContainer
	{
		private static void Release(Ticket ticket)
		{
		}
	}

	[Container]
	[Singleton<AsyncPooled>(SuppressDisposal = true)]
	public static partial class SuppressedAsyncContainer;
}
