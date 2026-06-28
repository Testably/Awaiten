using Autofac;
using Awaiten.Benchmarks.Helpers;
using BenchmarkDotNet.Attributes;
using DryIoc;
using Microsoft.Extensions.DependencyInjection;
using SimpleInjector;
using Container = DryIoc.Container;

namespace Awaiten.Benchmarks;

/// <summary>
///     Container construction (cold-start) cost for a graph of <see cref="Size" /> singletons. The
///     compile-time containers (Awaiten, Jab, Pure.DI) only allocate an instance — their wiring is baked in
///     at build time — whereas the runtime containers (MS.DI, Autofac, DryIoc, Simple Injector) register
///     every service and build a resolution map on construction, so their cost grows with the registration
///     count. Each benchmark builds the same B0..B255 graph it is named for.
/// </summary>
public class BuildBenchmarks : BenchmarksBase
{
	private Type[] _types = null!;
	[Params(8, 256)] public int Size { get; set; }

	[GlobalSetup]
	public void Setup() => _types = Markers.ServiceTypes(Size);

	[Benchmark(Baseline = true)]
	public object Build_Awaiten()
	{
		return Size == 8 ? new AwaitenContainer8.Root() : new AwaitenContainer256.Root();
	}

	[Benchmark]
	public object Build_MsDI()
	{
		ServiceCollection services = new();
		foreach (Type type in _types)
		{
			services.AddSingleton(type);
		}

		return services.BuildServiceProvider();
	}

	[Benchmark]
	public object Build_Autofac()
	{
		ContainerBuilder builder = new();
		foreach (Type type in _types)
		{
			builder.RegisterType(type).SingleInstance();
		}

		return builder.Build();
	}

	[Benchmark]
	public object Build_Jab()
	{
		return Size == 8 ? new JabContainer8() : new JabContainer256();
	}

	[Benchmark]
	public object Build_PureDI()
	{
		return Size == 8 ? new PureContainer8() : new PureContainer256();
	}

	[Benchmark]
	public object Build_DryIoc()
	{
		Container container = new();
		foreach (Type type in _types)
		{
			container.Register(type, Reuse.Singleton);
		}

		return container;
	}

	[Benchmark]
	public object Build_SimpleInjector()
	{
		SimpleInjector.Container container = new();
		foreach (Type type in _types)
		{
			container.Register(type, type, Lifestyle.Singleton);
		}

		return container;
	}
}
