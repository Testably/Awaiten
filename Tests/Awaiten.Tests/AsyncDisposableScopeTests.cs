using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     The scope surface for asynchronous disposal: <c>CreateScopeAsync</c> hands back the concrete generated
///     <c>Scope</c>, and <see cref="IAwaitenScope" /> is <see cref="IAsyncDisposable" /> on every framework, so a
///     scope opened through the interface can be <c>await using</c>d without a cast.
/// </summary>
public partial class AsyncDisposableScopeTests
{
	private static CancellationToken Ct => TestContext.Current!.Execution.CancellationToken;

	[Test]
	public async Task CreateScopeAsync_ThroughTheInterface_OnADisposedRoot_ThrowsOnTheCall()
	{
		IAwaitenScope root = new PlainContainer.Root();
		root.Dispose();

		await That(() => { _ = root.CreateScopeAsync(Ct); }).Throws<ObjectDisposedException>()
			.Because("the interface member forwards to the concrete CreateScopeAsync, whose disposed-guard runs on the call");
	}

	[Test]
	public async Task CreateScopeAsync_ThroughTheInterface_ReturnsTheGeneratedChildScope()
	{
		using PlainContainer.Root container = new();
		IAwaitenScope root = container;

		using IAwaitenScope scope = await root.CreateScopeAsync(Ct);

		await That(scope).IsExactly<PlainContainer.Scope>();
	}

	public sealed class Order;

	[Container]
	[Scoped<Order>]
	public static partial class PlainContainer;

	[Test]
	public async Task AwaitUsing_AnInterfaceTypedScope_DisposesAsyncOnlyServicesAsynchronously()
	{
		await using AsyncOnlyContainer.Root container = new();

		Rinse rinse = await UseScopeAsync(container);

		await That(rinse.DisposedAsync).IsTrue()
			.Because("an IAwaitenScope disposed through await using reaches the generated scope's asynchronous drain");

		static async Task<Rinse> UseScopeAsync(IAwaitenScope root)
		{
			await using IAwaitenScope scope = await root.CreateScopeAsync(Ct);
			return scope.Resolve<Rinse>();
		}
	}

#if NET
	// Only .NET carries the default DisposeAsync on IAwaitenScope; a netstandard2.0 implementation must write its own.
	[Test]
	public async Task AwaitUsing_AHandWrittenScopeWithoutDisposeAsync_FallsBackToDispose()
	{
		HandWrittenScope handWritten = new();

		await using (IAwaitenScope scope = handWritten)
		{
			await That(scope.CreateScope()).IsSameAs(handWritten);
		}

		await That(handWritten.DisposeCount).IsEqualTo(1)
			.Because("a scope that implements no DisposeAsync of its own is disposed asynchronously through the interface's synchronous fallback");
	}
#endif

	public sealed class Rinse : IAsyncDisposable
	{
		public bool DisposedAsync { get; private set; }

		public ValueTask DisposeAsync()
		{
			DisposedAsync = true;
			return default;
		}
	}

	[Container]
	[Scoped<Rinse>]
	public static partial class AsyncOnlyContainer;

#if NET
	private sealed class HandWrittenScope : IAwaitenScope
	{
		public int DisposeCount { get; private set; }

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

		public Task<object> ResolveAsync(Type serviceType, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<object> ResolveAsync(Type serviceType, object? key, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public IAwaitenScope CreateScope() => this;

		public Task<IAwaitenScope> CreateScopeAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IAwaitenScope>(this);

		public void Dispose() => DisposeCount++;
	}
#endif
}
