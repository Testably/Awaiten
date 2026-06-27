using Awaiten.Benchmarks.Helpers;
using BenchmarkDotNet.Attributes;

namespace Awaiten.Benchmarks;

/// <summary>
///     Container construction (cold-start) cost for a graph of <see cref="Size" /> services. The
///     compile-time containers (Awaiten, Jab, Pure.DI) only allocate an instance — their wiring is baked in
///     at build time — whereas the runtime containers (MS.DI, Autofac) register every service and build a
///     resolution map on construction, so their cost grows with the registration count.
/// </summary>
public class BuildBenchmarks : BenchmarksBase
{
	private Type[] _types = null!;
	[Params(8, 64, 256)] public int Size { get; set; }

	[GlobalSetup]
	public void Setup() => _types = Fixtures.ServiceTypes(Size);

	[Benchmark(Baseline = true)]
	public object Build_Awaiten() => Fixtures.Awaiten(Size);

	[Benchmark]
	public object Build_MsDI() => Fixtures.BuildMsDI(_types);

	[Benchmark]
	public object Build_Autofac() => Fixtures.BuildAutofac(_types);

	[Benchmark]
	public object Build_Jab() => Fixtures.Jab(Size);

	[Benchmark]
	public object Build_PureDI() => Fixtures.PureNew(Size);
}
