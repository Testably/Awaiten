using Autofac;
using Awaiten.Benchmarks.Helpers;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Benchmarks;

/// <summary>
///     Steady-state resolution latency: resolve the last-registered service by <see cref="Type" /> from a
///     container of <see cref="Size" /> services. This is the worst case for a linear type-chain (every
///     earlier comparison misses first); Awaiten's dictionary+switch and the other containers' hash lookups
///     are size-independent. Resolving by <see cref="Type" /> is the primary API for Awaiten, MS.DI and
///     Autofac, and the secondary (non-generic) path for the compile-time competitors Jab and Pure.DI.
/// </summary>
public class ResolveBenchmarks : BenchmarksBase
{
	private IContainer _autofac = null!;

	private IAwaitenContainer _awaiten = null!;
	private IServiceProvider _jab = null!;
	private Type _lastType = null!;
	private ServiceProvider _msdi = null!;
	private Func<Type, object> _pure = null!;
	[Params(8, 64, 256)] public int Size { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_awaiten = Fixtures.Awaiten(Size);
		Type[] types = Fixtures.ServiceTypes(Size);
		_lastType = types[types.Length - 1];
		_msdi = Fixtures.BuildMsDI(types);
		_autofac = Fixtures.BuildAutofac(types);
		_jab = Fixtures.Jab(Size);
		_pure = Fixtures.PureResolve(Size);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_awaiten.Dispose();
		_msdi.Dispose();
		_autofac.Dispose();
		(_jab as IDisposable)?.Dispose();
	}

	[Benchmark(Baseline = true)]
	public object Resolve_Awaiten() => _awaiten.Resolve(_lastType);

	[Benchmark]
	public object? Resolve_MsDI() => _msdi.GetService(_lastType);

	[Benchmark]
	public object Resolve_Autofac() => _autofac.Resolve(_lastType);

	[Benchmark]
	public object? Resolve_Jab() => _jab.GetService(_lastType);

	[Benchmark]
	public object Resolve_PureDI() => _pure(_lastType);
}
