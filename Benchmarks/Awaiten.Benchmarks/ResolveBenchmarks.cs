using Autofac;
using Awaiten.Benchmarks.Helpers;
using BenchmarkDotNet.Attributes;
using DryIoc;
using Microsoft.Extensions.DependencyInjection;
using SimpleInjector;
using Container = SimpleInjector.Container;
using IContainer = Autofac.IContainer;

namespace Awaiten.Benchmarks;

/// <summary>
///     Steady-state resolution latency: resolve the last-registered service by <see cref="Type" /> from a
///     container of <see cref="Size" /> singletons. This is the worst case for a linear type-chain (every
///     earlier comparison misses first); Awaiten's dictionary+switch and the other containers' hash lookups
///     are size-independent. Each container is built once in <see cref="Setup" /> and registers the same
///     B0..B255 graph; the compile-time containers (Awaiten, Jab, Pure.DI) are generated, the rest are built
///     at runtime here so a reader sees every framework's setup and measured call in one place.
/// </summary>
public class ResolveBenchmarks : BenchmarksBase
{
	private IContainer _autofac = null!;
	private IAwaitenContainer _awaiten = null!;
	private DryIoc.Container _dryioc = null!;
	private IServiceProvider _jab = null!;
	private Type _last = null!;
	private ServiceProvider _msdi = null!;
	private Func<Type, object> _pure = null!;
	private Container _simpleInjector = null!;
	[Params(8, 256)] public int Size { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		Type[] types = Markers.ServiceTypes(Size);
		_last = types[^1];

		_awaiten = Size == 8 ? new AwaitenContainer8() : new AwaitenContainer256();
		_jab = Size == 8 ? new JabContainer8() : new JabContainer256();
		_pure = Size == 8 ? new PureContainer8().Resolve : new PureContainer256().Resolve;

		ServiceCollection msdi = new();
		foreach (Type type in types)
		{
			msdi.AddSingleton(type);
		}

		_msdi = msdi.BuildServiceProvider();

		ContainerBuilder autofac = new();
		foreach (Type type in types)
		{
			autofac.RegisterType(type).SingleInstance();
		}

		_autofac = autofac.Build();

		DryIoc.Container dryioc = new();
		foreach (Type type in types)
		{
			dryioc.Register(type, Reuse.Singleton);
		}

		_dryioc = dryioc;

		Container simpleInjector = new();
		foreach (Type type in types)
		{
			simpleInjector.Register(type, type, Lifestyle.Singleton);
		}

		_simpleInjector = simpleInjector;
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_awaiten.Dispose();
		_msdi.Dispose();
		_autofac.Dispose();
		(_jab as IDisposable)?.Dispose();
		_dryioc.Dispose();
		_simpleInjector.Dispose();
	}

	[Benchmark(Baseline = true)]
	public object Resolve_Awaiten() => _awaiten.Resolve(_last);

	[Benchmark]
	public object? Resolve_MsDI() => _msdi.GetService(_last);

	[Benchmark]
	public object Resolve_Autofac() => _autofac.Resolve(_last);

	[Benchmark]
	public object? Resolve_Jab() => _jab.GetService(_last);

	[Benchmark]
	public object Resolve_PureDI() => _pure(_last);

	[Benchmark]
	public object Resolve_DryIoc() => _dryioc.Resolve(_last);

	[Benchmark]
	public object Resolve_SimpleInjector() => _simpleInjector.GetInstance(_last);
}
