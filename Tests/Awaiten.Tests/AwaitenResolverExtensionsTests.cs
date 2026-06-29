using System.Diagnostics.CodeAnalysis;

namespace Awaiten.Tests;

/// <summary>
///     The generic <see cref="AwaitenResolverExtensions" /> conveniences over the
///     <see cref="IAwaitenResolver" /> resolution surface, exercised against a hand-written
///     <see cref="FakeResolver" /> so each branch is isolated from the generated container.
/// </summary>
public sealed class AwaitenResolverExtensionsTests
{
	[Fact]
	public async Task Resolve_CastsTheResolvedInstanceToT()
	{
		FakeResolver resolver = new()
		{
			ResolveResult = "value",
		};

		string resolved = resolver.Resolve<string>();

		await That(resolved).IsEqualTo("value");
		await That(resolver.RequestedResolveType).IsEqualTo(typeof(string));
	}

	[Fact]
	public async Task Resolve_WhenResolverIsNull_Throws()
	{
		IAwaitenResolver resolver = null!;

		await That(() => resolver.Resolve<string>()).Throws<ArgumentNullException>()
			.WithParamName("resolver");
	}

	[Fact]
	public async Task TryResolve_WhenNotResolved_ReturnsFalseAndDefaultsInstance()
	{
		FakeResolver resolver = new()
		{
			TryResolveResult = false,
		};

		bool resolved = resolver.TryResolve(out string? instance);

		await That(resolved).IsFalse();
		await That(instance).IsNull();
	}

	[Fact]
	public async Task TryResolve_WhenResolved_ReturnsTrueAndCastsInstance()
	{
		FakeResolver resolver = new()
		{
			TryResolveResult = true,
			TryResolveInstance = "value",
		};

		bool resolved = resolver.TryResolve(out string? instance);

		await That(resolved).IsTrue();
		await That(instance).IsEqualTo("value");
		await That(resolver.RequestedTryResolveType).IsEqualTo(typeof(string));
	}

	[Fact]
	public async Task TryResolve_WhenResolverIsNull_Throws()
	{
		IAwaitenResolver resolver = null!;

		await That(() => resolver.TryResolve(out string? _)).Throws<ArgumentNullException>()
			.WithParamName("resolver");
	}

	[Fact]
	public async Task ResolveAsync_ForwardsTheRequestedTypeAndCastsTheResult()
	{
		FakeResolver resolver = new()
		{
			ResolveResult = "value",
		};

		string resolved = await resolver.ResolveAsync<string>(TestContext.Current.CancellationToken);

		await That(resolved).IsEqualTo("value");
		await That(resolver.RequestedResolveAsyncType).IsEqualTo(typeof(string));
	}

	[Fact]
	public async Task ResolveAsync_WhenResolverIsNull_ThrowsSynchronously()
	{
		IAwaitenAsyncResolver resolver = null!;

		// The null check is eager argument validation, not deferred into the returned task: invoking and
		// discarding the call (without awaiting it) still throws, proving the guard runs before the task.
		await That(() => { _ = resolver.ResolveAsync<string>(); }).Throws<ArgumentNullException>()
			.WithParamName("resolver");
	}

	private sealed class FakeResolver : IAwaitenAsyncResolver
	{
		public object ResolveResult { get; set; } = null!;
		public bool TryResolveResult { get; set; }
		public object? TryResolveInstance { get; set; }
		public Type? RequestedResolveType { get; private set; }
		public Type? RequestedTryResolveType { get; private set; }

		public object Resolve(Type serviceType)
		{
			RequestedResolveType = serviceType;
			return ResolveResult;
		}

		public bool TryResolve(Type serviceType, [NotNullWhen(true)] out object? instance)
		{
			RequestedTryResolveType = serviceType;
			instance = TryResolveInstance;
			return TryResolveResult;
		}

		public Type? RequestedResolveAsyncType { get; private set; }

		public Task<object> ResolveAsync(Type serviceType, System.Threading.CancellationToken cancellationToken = default)
		{
			RequestedResolveAsyncType = serviceType;
			return Task.FromResult(ResolveResult);
		}
	}
}
