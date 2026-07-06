using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     Argument guards and error paths of the bridge surface: the null/order checks on
///     <see cref="AwaitenServiceProviderFactory{TRoot}" /> and <see cref="AwaitenServiceProvider" />, and the
///     synchronous-disposal guard the <c>Task&lt;T&gt;</c> projection slot raises for an async-only-disposable.
/// </summary>
public sealed partial class BridgeGuardTests
{
	public sealed class Widget;

	[Container]
	[Singleton<Widget>]
	public static partial class GuardContainer;

	[Fact]
	public async Task Factory_CreateBuilder_NullServices_Throws()
	{
		AwaitenServiceProviderFactory<GuardContainer.Root> factory = new();

		await That(() => factory.CreateBuilder(null!)).Throws<ArgumentNullException>();
	}

	[Fact]
	public async Task Factory_CreateServiceProvider_NullBuilder_Throws()
	{
		AwaitenServiceProviderFactory<GuardContainer.Root> factory = new();
		factory.CreateBuilder(new ServiceCollection());

		await That(() => factory.CreateServiceProvider(null!)).Throws<ArgumentNullException>();
	}

	[Fact]
	public async Task Factory_CreateServiceProvider_BeforeCreateBuilder_Throws()
	{
		AwaitenServiceProviderFactory<GuardContainer.Root> factory = new();

		await That(() => factory.CreateServiceProvider(new GuardContainer.Root()))
			.Throws<InvalidOperationException>()
			.WithMessage("*CreateBuilder*CreateServiceProvider*").AsWildcard();
	}

	[Fact]
	public async Task Provider_Constructor_NullContainer_Throws()
		=> await That(() => _ = new AwaitenServiceProvider(null!)).Throws<ArgumentNullException>();

	[Fact]
	public async Task Provider_GetService_NullServiceType_Throws()
	{
		await using AwaitenServiceProvider provider = new(new GuardContainer.Root());

		await That(() => provider.GetService(null!)).Throws<ArgumentNullException>();
	}

	[Fact]
	public async Task Provider_GetKeyedService_NullServiceType_Throws()
	{
		await using AwaitenServiceProvider provider = new(new GuardContainer.Root());

		await That(() => provider.GetKeyedService(null!, "k")).Throws<ArgumentNullException>();
	}

	[Fact]
	public async Task Provider_GetRequiredKeyedService_UnknownKey_Throws()
	{
		await using AwaitenServiceProvider provider = new(new GuardContainer.Root());

		await That(() => provider.GetRequiredKeyedService(typeof(Widget), "missing"))
			.Throws<InvalidOperationException>()
			.WithMessage("*Widget*missing*").AsWildcard();
	}

#if NET
	// An async-initialized service (Task<T>-projected) that can only be disposed asynchronously. The generated
	// async-disposal surface exists only on net8.0+, so this scenario is net-only.
	public sealed class AsyncOnlyDisposableSingleton : IAsyncInitializable, IAsyncDisposable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public ValueTask DisposeAsync() => default;
	}

	[Container]
	[Singleton<AsyncOnlyDisposableSingleton>]
	public static partial class AsyncOnlyProjectionContainer;

	[Fact]
	public async Task Projection_SyncDisposeOfAsyncOnlyDisposable_ThrowsGuidance()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<AsyncOnlyProjectionContainer.Root>();
		ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		// The awaited instance is IAsyncDisposable-only, handed to the bridge's disposal slot. A synchronous
		// teardown cannot drain it, so the slot throws the container's "dispose asynchronously" guidance rather
		// than blocking on async disposal.
		_ = await provider.GetRequiredService<Task<AsyncOnlyDisposableSingleton>>();

		await That(() => provider.Dispose())
			.Throws<InvalidOperationException>()
			.WithMessage("*asynchronous disposal*").AsWildcard();

		// The slot stays filled after the throw, so an async teardown can still drain it cleanly.
		await That(() => provider.DisposeAsync()).DoesNotThrow();
	}
#endif
}
