namespace Awaiten.Benchmarks.Helpers;

[Container]
[Singleton<Config>]
[Singleton<Logger>]
[Singleton<Cache>]
[Scoped<DbConnection>]
[Scoped<UnitOfWork>]
[Scoped<UserRepository>]
[Scoped<OrderRepository>]
[Scoped<UserService>]
[Scoped<OrderService>]
[Scoped<RequestHandler>]
[Transient<Mapper>]
[Transient<RequestValidator>]
public static partial class AwaitenRealisticContainer;
