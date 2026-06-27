using Jab;

namespace Awaiten.Benchmarks.Helpers;

[ServiceProvider]
[Singleton(typeof(Config))]
[Singleton(typeof(Logger))]
[Singleton(typeof(Cache))]
[Scoped(typeof(DbConnection))]
[Scoped(typeof(UnitOfWork))]
[Scoped(typeof(UserRepository))]
[Scoped(typeof(OrderRepository))]
[Scoped(typeof(UserService))]
[Scoped(typeof(OrderService))]
[Scoped(typeof(RequestHandler))]
[Transient(typeof(Mapper))]
[Transient(typeof(RequestValidator))]
public sealed partial class JabRealisticContainer;
