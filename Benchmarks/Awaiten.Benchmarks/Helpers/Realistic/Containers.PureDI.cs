using Pure.DI;
using static Pure.DI.Lifetime;

namespace Awaiten.Benchmarks.Helpers;

// Pure.DI consumes the Setup() method at compile time; it is never invoked at runtime, so S1144 does
// not apply.
#pragma warning disable S1144

internal sealed partial class PureRealisticContainer
{
	private static void Setup() => DI.Setup(nameof(PureRealisticContainer))
		.Hint(Hint.ScopeMethodName, "CreateScope")
		.Bind<Config>().As(Singleton).To<Config>()
		.Bind<Logger>().As(Singleton).To<Logger>()
		.Bind<Cache>().As(Singleton).To<Cache>()
		.Bind<DbConnection>().As(Scoped).To<DbConnection>()
		.Bind<UnitOfWork>().As(Scoped).To<UnitOfWork>()
		.Bind<UserRepository>().As(Scoped).To<UserRepository>()
		.Bind<OrderRepository>().As(Scoped).To<OrderRepository>()
		.Bind<UserService>().As(Scoped).To<UserService>()
		.Bind<OrderService>().As(Scoped).To<OrderService>()
		.Bind<RequestHandler>().As(Scoped).To<RequestHandler>()
		.Bind<Mapper>().As(Transient).To<Mapper>()
		.Bind<RequestValidator>().As(Transient).To<RequestValidator>()
		.Root<RequestHandler>("Root");
}
#pragma warning restore S1144
