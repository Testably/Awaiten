using Autofac;
using Awaiten.Benchmarks.Helpers;
using BenchmarkDotNet.Attributes;
using DryIoc;
using Microsoft.Extensions.DependencyInjection;
using SimpleInjector;
using SimpleInjector.Lifestyles;
using Container = SimpleInjector.Container;
using IContainer = Autofac.IContainer;

namespace Awaiten.Benchmarks;

/// <summary>
///     End-to-end per-request resolution: open a scope, resolve a deep mixed-lifetime object graph rooted at
///     <see cref="RequestHandler" />, then dispose the scope. Unlike <see cref="ResolveBenchmarks" /> - which
///     isolates a single by-<see cref="Type" /> lookup over a flat set of singletons - this measures the work
///     a real application does on every request: singletons are shared across scopes, scoped instances are
///     built once per scope and cached, transients are constructed fresh on each resolution. The graph (see
///     the Realistic helpers) is ~13 concrete classes wired by constructor injection, at least five levels
///     deep. Each container is built once in <see cref="Setup" /> and registers the same graph; the
///     compile-time containers (Awaiten, Jab, Pure.DI) are generated, the rest are built at runtime here so a
///     reader sees every framework's setup and measured call in one place.
/// </summary>
public class RealisticResolveBenchmarks : BenchmarksBase
{
	private IContainer _autofac = null!;
	private AwaitenRealisticContainer.Root _awaiten = null!;
	private DryIoc.Container _dryioc = null!;
	private JabRealisticContainer _jab = null!;
	private ServiceProvider _msdi = null!;
	private PureRealisticContainer _pure = null!;
	private Container _simpleInjector = null!;

	[GlobalSetup]
	public void Setup()
	{
		_awaiten = new AwaitenRealisticContainer.Root();
		_jab = new JabRealisticContainer();
		_pure = new PureRealisticContainer();

		ServiceCollection msdi = new();
		msdi.AddSingleton<Config>();
		msdi.AddSingleton<Logger>();
		msdi.AddSingleton<Cache>();
		msdi.AddScoped<DbConnection>();
		msdi.AddScoped<UnitOfWork>();
		msdi.AddScoped<UserRepository>();
		msdi.AddScoped<OrderRepository>();
		msdi.AddScoped<UserService>();
		msdi.AddScoped<OrderService>();
		msdi.AddScoped<RequestHandler>();
		msdi.AddTransient<Mapper>();
		msdi.AddTransient<RequestValidator>();
		_msdi = msdi.BuildServiceProvider();

		ContainerBuilder autofac = new();
		autofac.RegisterType<Config>().SingleInstance();
		autofac.RegisterType<Logger>().SingleInstance();
		autofac.RegisterType<Cache>().SingleInstance();
		autofac.RegisterType<DbConnection>().InstancePerLifetimeScope();
		autofac.RegisterType<UnitOfWork>().InstancePerLifetimeScope();
		autofac.RegisterType<UserRepository>().InstancePerLifetimeScope();
		autofac.RegisterType<OrderRepository>().InstancePerLifetimeScope();
		autofac.RegisterType<UserService>().InstancePerLifetimeScope();
		autofac.RegisterType<OrderService>().InstancePerLifetimeScope();
		autofac.RegisterType<RequestHandler>().InstancePerLifetimeScope();
		autofac.RegisterType<Mapper>().InstancePerDependency();
		autofac.RegisterType<RequestValidator>().InstancePerDependency();
		_autofac = autofac.Build();

		DryIoc.Container dryioc = new();
		dryioc.Register<Config>(Reuse.Singleton);
		dryioc.Register<Logger>(Reuse.Singleton);
		dryioc.Register<Cache>(Reuse.Singleton);
		dryioc.Register<DbConnection>(Reuse.Scoped);
		dryioc.Register<UnitOfWork>(Reuse.Scoped);
		dryioc.Register<UserRepository>(Reuse.Scoped);
		dryioc.Register<OrderRepository>(Reuse.Scoped);
		dryioc.Register<UserService>(Reuse.Scoped);
		dryioc.Register<OrderService>(Reuse.Scoped);
		dryioc.Register<RequestHandler>(Reuse.Scoped);
		dryioc.Register<Mapper>(Reuse.Transient);
		dryioc.Register<RequestValidator>(Reuse.Transient);
		_dryioc = dryioc;

		Container simpleInjector = new();
		simpleInjector.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();
		simpleInjector.Register<Config>(Lifestyle.Singleton);
		simpleInjector.Register<Logger>(Lifestyle.Singleton);
		simpleInjector.Register<Cache>(Lifestyle.Singleton);
		simpleInjector.Register<DbConnection>(Lifestyle.Scoped);
		simpleInjector.Register<UnitOfWork>(Lifestyle.Scoped);
		simpleInjector.Register<UserRepository>(Lifestyle.Scoped);
		simpleInjector.Register<OrderRepository>(Lifestyle.Scoped);
		simpleInjector.Register<UserService>(Lifestyle.Scoped);
		simpleInjector.Register<OrderService>(Lifestyle.Scoped);
		simpleInjector.Register<RequestHandler>(Lifestyle.Scoped);
		simpleInjector.Register<Mapper>(Lifestyle.Transient);
		simpleInjector.Register<RequestValidator>(Lifestyle.Transient);
		_simpleInjector = simpleInjector;
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_awaiten.Dispose();
		_msdi.Dispose();
		_autofac.Dispose();
		_jab.Dispose();
		_dryioc.Dispose();
		_simpleInjector.Dispose();
	}

	[Benchmark(Baseline = true)]
	public object Realistic_Awaiten()
	{
		using AwaitenRealisticContainer.Scope scope = _awaiten.CreateScope();
		return scope.Resolve<RequestHandler>();
	}

	[Benchmark]
	public object Realistic_MsDI()
	{
		using IServiceScope scope = _msdi.CreateScope();
		return scope.ServiceProvider.GetRequiredService<RequestHandler>();
	}

	[Benchmark]
	public object Realistic_Autofac()
	{
		using ILifetimeScope scope = _autofac.BeginLifetimeScope();
		return scope.Resolve<RequestHandler>();
	}

	[Benchmark]
	public object Realistic_Jab()
	{
		using JabRealisticContainer.Scope scope = _jab.CreateScope();
		return scope.GetService<RequestHandler>();
	}

	[Benchmark]
	public object Realistic_DryIoc()
	{
		using IResolverContext scope = _dryioc.OpenScope();
		return scope.Resolve<RequestHandler>();
	}

	[Benchmark]
	public object Realistic_SimpleInjector()
	{
		using (AsyncScopedLifestyle.BeginScope(_simpleInjector))
		{
			return _simpleInjector.GetInstance<RequestHandler>();
		}
	}

	[Benchmark]
	public object Realistic_PureDI()
	{
		using PureRealisticContainer scope = PureRealisticContainer.CreateScope(_pure, new PureRealisticContainer());
		return scope.Resolve<RequestHandler>();
	}
}
