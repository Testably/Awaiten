namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of open generic registration: an open generic implementation registered with
///     <c>[Transient(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c> constructs the matching
///     closed implementation (<c>Repository&lt;Order&gt;</c>) when a closed service
///     (<c>IRepository&lt;Order&gt;</c>) is required. Expansion is driven at compile time by the closed
///     services an application actually needs, so the container declares a concrete <c>App</c> root that
///     depends on them. The containers and services are nested types, so the enclosing class is
///     <c>partial</c>.
/// </summary>
public partial class OpenGenericTests
{
	[Fact]
	public async Task ClosedService_ConstructsTheMatchingClosedImplementation()
	{
		using OpenGenericContainer.Root container = new();

		IRepository<Order> repository = container.Resolve<IRepository<Order>>();

		await That(repository).Is<Repository<Order>>();
		await That(repository.Describe()).IsEqualTo("Repository<Order>");
	}

	[Fact]
	public async Task DifferentTypeArguments_ConstructDistinctClosedImplementations()
	{
		using OpenGenericContainer.Root container = new();

		IRepository<Order> orders = container.Resolve<IRepository<Order>>();
		IRepository<Customer> customers = container.Resolve<IRepository<Customer>>();

		await That(orders.Describe()).IsEqualTo("Repository<Order>");
		await That(customers.Describe()).IsEqualTo("Repository<Customer>");
	}

	[Fact]
	public async Task TransitiveOpenChain_ResolvesAllClosedGenericDependencies()
	{
		using OpenGenericContainer.Root container = new();

		// Handler<Order> depends on IValidator<Order>; both are open registrations expanded transitively.
		Handler<Order> handler = container.Resolve<Handler<Order>>();

		await That(handler.Validator).Is<Validator<Order>>();
	}

	[Fact]
	public async Task SingletonClosedInstance_IsSharedAcrossResolutions()
	{
		using OpenGenericContainer.Root container = new();

		ICache<Order> first = container.Resolve<ICache<Order>>();
		ICache<Order> second = container.Resolve<ICache<Order>>();

		await That(first).IsSameAs(second);
	}

	[Fact]
	public async Task TransientClosedInstance_IsFreshOnEachResolution()
	{
		using OpenGenericContainer.Root container = new();

		IRepository<Order> first = container.Resolve<IRepository<Order>>();
		IRepository<Order> second = container.Resolve<IRepository<Order>>();

		await That(ReferenceEquals(first, second)).IsFalse();
	}

	public sealed class Order;

	public sealed class Customer;

	public interface IRepository<T>
	{
		string Describe();
	}

	public sealed class Repository<T> : IRepository<T>
	{
		public string Describe() => $"Repository<{typeof(T).Name}>";
	}

	public interface IValidator<T>;

	public sealed class Validator<T> : IValidator<T>;

	public sealed class Handler<T>
	{
		public Handler(IValidator<T> validator) => Validator = validator;

		public IValidator<T> Validator { get; }
	}

	public interface ICache<T>;

	public sealed class Cache<T> : ICache<T>;

	// A concrete root that depends on the closed generics, seeding open generic expansion for the
	// type arguments actually used by the application.
	public sealed class App
	{
		public App(
			IRepository<Order> orders,
			IRepository<Customer> customers,
			Handler<Order> handler,
			ICache<Order> cache)
		{
		}
	}

	[Container]
	[Transient(typeof(Repository<>), typeof(IRepository<>))]
	[Transient(typeof(Validator<>), typeof(IValidator<>))]
	[Transient(typeof(Handler<>))]
	[Singleton(typeof(Cache<>), typeof(ICache<>))]
	[Transient<App>]
	public static partial class OpenGenericContainer;
}
