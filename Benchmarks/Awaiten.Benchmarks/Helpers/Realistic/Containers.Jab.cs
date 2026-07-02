using Jab;

namespace Awaiten.Benchmarks.Helpers;

[ServiceProvider]
[Jab.Singleton(typeof(Config))]
[Jab.Singleton(typeof(Logger))]
[Jab.Singleton(typeof(Cache))]
[Jab.Scoped(typeof(DbConnection))]
[Jab.Scoped(typeof(UnitOfWork))]
[Jab.Scoped(typeof(UserRepository))]
[Jab.Scoped(typeof(OrderRepository))]
[Jab.Scoped(typeof(UserService))]
[Jab.Scoped(typeof(OrderService))]
[Jab.Scoped(typeof(RequestHandler))]
[Jab.Transient(typeof(Mapper))]
[Jab.Transient(typeof(RequestValidator))]
public sealed partial class JabRealisticContainer;
