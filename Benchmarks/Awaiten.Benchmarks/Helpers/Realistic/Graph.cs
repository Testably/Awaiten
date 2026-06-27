namespace Awaiten.Benchmarks.Helpers;

// A small, realistic object graph shared by every container in RealisticResolveBenchmarks: a request
// handler at the root, layered over services, repositories, a unit of work and infrastructure. Lifetimes
// are mixed deliberately - singletons live for the container, scoped instances for one request, transients
// are built fresh on every resolution - so the measured per-request resolve exercises singleton sharing,
// per-scope caching and transient construction in one pass. The scoped resources (DbConnection, UnitOfWork)
// are IDisposable, as real scope-owned resources are, so disposing the scope also exercises each container's
// disposal tracking and reverse-order teardown. Every type is a concrete class wired by constructor
// injection; each holds its collaborators so the parameters are genuinely used.

// Singletons - one instance for the lifetime of the container.

public sealed class Config;

public sealed class Logger;

public sealed class Cache(Config config)
{
	public Config Config { get; } = config;
}

// Scoped - one instance per request scope.

public sealed class DbConnection(Config config) : IDisposable
{
	public Config Config { get; } = config;
	public bool Disposed { get; private set; }
	public void Dispose() => Disposed = true;
}

public sealed class UnitOfWork(DbConnection connection, Logger logger) : IDisposable
{
	public DbConnection Connection { get; } = connection;
	public Logger Logger { get; } = logger;
	public bool Disposed { get; private set; }
	public void Dispose() => Disposed = true;
}

public sealed class UserRepository(UnitOfWork unitOfWork, Cache cache)
{
	public UnitOfWork UnitOfWork { get; } = unitOfWork;
	public Cache Cache { get; } = cache;
}

public sealed class OrderRepository(UnitOfWork unitOfWork, Cache cache)
{
	public UnitOfWork UnitOfWork { get; } = unitOfWork;
	public Cache Cache { get; } = cache;
}

public sealed class UserService(UserRepository repository, RequestValidator validator, Mapper mapper, Logger logger)
{
	public UserRepository Repository { get; } = repository;
	public RequestValidator Validator { get; } = validator;
	public Mapper Mapper { get; } = mapper;
	public Logger Logger { get; } = logger;
}

public sealed class OrderService(OrderRepository repository, UserService userService, Mapper mapper)
{
	public OrderRepository Repository { get; } = repository;
	public UserService UserService { get; } = userService;
	public Mapper Mapper { get; } = mapper;
}

/// <summary>The resolved root: depth >=5 down to <see cref="Config" /> via the service and repository layers.</summary>
public sealed class RequestHandler(UserService userService, OrderService orderService, UnitOfWork unitOfWork, Logger logger)
{
	public UserService UserService { get; } = userService;
	public OrderService OrderService { get; } = orderService;
	public UnitOfWork UnitOfWork { get; } = unitOfWork;
	public Logger Logger { get; } = logger;
}

// Transient - a fresh instance on every resolution.

public sealed class Mapper;

public sealed class RequestValidator(Logger logger)
{
	public Logger Logger { get; } = logger;
}
