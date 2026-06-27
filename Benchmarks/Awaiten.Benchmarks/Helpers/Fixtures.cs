using Autofac;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Benchmarks.Helpers;

/// <summary>
///     Builds the per-size containers the benchmarks resolve from, over the same B0..B255 marker services.
///     The Awaiten, Jab and Pure.DI containers are compile-time generated (see Containers.cs /
///     CompetitorContainers.cs); the MS.DI and Autofac ones are built at runtime from the same service
///     types so every framework registers the same graph. All six register their services as singletons,
///     so the resolution benchmark measures dispatch latency rather than per-resolve construction cost.
/// </summary>
internal static class Fixtures
{
	public static IAwaitenContainer Awaiten(int size) => size switch
	{
		8 => new GenContainer8(),
		64 => new GenContainer64(),
		_ => new GenContainer256(),
	};

	public static IServiceProvider Jab(int size) => size switch
	{
		8 => new JabContainer8(),
		64 => new JabContainer64(),
		_ => new JabContainer256(),
	};

	public static object PureNew(int size) => size switch
	{
		8 => new PureContainer8(),
		64 => new PureContainer64(),
		_ => new PureContainer256(),
	};

	public static Func<Type, object> PureResolve(int size) => size switch
	{
		8 => new PureContainer8().Resolve,
		64 => new PureContainer64().Resolve,
		_ => new PureContainer256().Resolve,
	};

	// The marker services are named B0..B255 in this assembly, in registration order, so the first
	// <paramref name="size" /> of them are exactly the types the GenContainer of that size registers.
	public static Type[] ServiceTypes(int size)
	{
		Type[] types = new Type[size];
		for (int i = 0; i < types.Length; i++)
		{
			types[i] = Type.GetType($"Awaiten.Benchmarks.B{i}", true)!;
		}

		return types;
	}

	public static ServiceProvider BuildMsDI(Type[] types)
	{
		ServiceCollection services = new();
		foreach (Type type in types)
		{
			services.AddSingleton(type);
		}

		return services.BuildServiceProvider();
	}

	public static IContainer BuildAutofac(Type[] types)
	{
		ContainerBuilder builder = new();
		foreach (Type type in types)
		{
			builder.RegisterType(type).SingleInstance();
		}

		return builder.Build();
	}
}
