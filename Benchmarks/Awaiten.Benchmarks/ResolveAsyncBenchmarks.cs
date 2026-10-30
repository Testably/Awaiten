using Awaiten.Benchmarks.Helpers;
using BenchmarkDotNet.Attributes;

namespace Awaiten.Benchmarks;

/// <summary>
///     The cost of the generic <c>ResolveAsync&lt;T&gt;</c> per lifetime, Awaiten only: a synchronous singleton,
///     an async-initialized singleton warmed through <c>InitializeAsync</c>, the same singleton on a fresh
///     container (cold, so the container's construction and disposal are part of the measurement), a scoped
///     service from a long-lived scope and a transient from the root.
/// </summary>
public class ResolveAsyncBenchmarks : BenchmarksBase
{
	private AwaitenAsyncContainer.Root _root = null!;
	private AwaitenAsyncContainer.Scope _scope = null!;

	[GlobalSetup]
	public void Setup()
	{
		_root = new AwaitenAsyncContainer.Root();
		_root.InitializeAsync().GetAwaiter().GetResult();
		_scope = _root.CreateScope();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_scope.Dispose();
		_root.Dispose();
	}

	[Benchmark(Baseline = true)]
	public Task<SyncSingleton> ResolveAsync_SyncSingleton() => _root.ResolveAsync<SyncSingleton>();

	[Benchmark]
	public Task<AsyncSingleton> ResolveAsync_WarmedAsyncSingleton() => _root.ResolveAsync<AsyncSingleton>();

	[Benchmark]
	public async Task<AsyncSingleton> ResolveAsync_ColdAsyncSingleton()
	{
		await using AwaitenAsyncContainer.Root root = new();
		return await root.ResolveAsync<AsyncSingleton>();
	}

	[Benchmark]
	public Task<SyncScoped> ResolveAsync_Scoped() => _scope.ResolveAsync<SyncScoped>();

	[Benchmark]
	public Task<SyncTransient> ResolveAsync_Transient() => _root.ResolveAsync<SyncTransient>();
}
