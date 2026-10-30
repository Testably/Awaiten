using System.Collections.Generic;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of the options on the <c>typeof</c> forms of <c>[Singleton]</c>/<c>[Scoped]</c>/<c>[Transient]</c>.
///     A closed pair behaves exactly like the generic form; an open generic applies each option per closing, with a
///     generic <c>Factory</c> and generic hooks closed over the closing's type arguments. Hooks and constructors record
///     into a container-owned <see cref="Log" />, so the tests share no static state.
/// </summary>
public partial class TypeofRegistrationTests
{
	public class Closed
	{
		[Test]
		public async Task Factory_ProducesTheService()
		{
			using ClosedContainer.Root container = new();

			await That(container.Resolve<IClock>()).Is<FactoryClock>();
		}

		[Test]
		public async Task Instance_ExposesThePreBuiltInstance()
		{
			using ClosedContainer.Root container = new();

			await That(container.Resolve<Settings>()).IsSameAs(ClosedContainer.SharedSettings);
		}

		[Test]
		public async Task Eager_ConstructsTheSingletonWithTheContainer()
		{
			using ClosedContainer.Root container = new();

			await That(container.Resolve<Log>().Entries).Contains("warm")
				.Because("an eager singleton is built when the container is, before anything resolves it");
		}

		[Test]
		public async Task Hooks_RunAndSuppressDisposalSkipsTheDisposal()
		{
			using ClosedContainer.Root container = new();
			Log log = container.Resolve<Log>();

			using (IAwaitenScope scope = container.CreateScope())
			{
				scope.Resolve<ISession>();
				await That(log.Entries).Contains("opened:Session");
			}

			await That(log.Entries).Contains("closed:Session")
				.Because("the release hook runs when the owning scope is disposed");
			await That(log.Entries).DoesNotContain("disposed:Session")
				.Because("SuppressDisposal opts out of the container's disposal");
		}

		[Test]
		public async Task Fallback_YieldsToTheExplicitRegistration()
		{
			using ClosedContainer.Root container = new();

			await That(container.Resolve<IGreeter>()).Is<CustomGreeter>();
			await That(container.Resolve<IGreeter[]>()).HasCount(1)
				.Because("an overridden default is dropped in full, even from the collection");
		}

		[Test]
		public async Task WhenInjectedInto_BindsOnlyForTheConsumer()
		{
			using ClosedContainer.Root container = new();

			await That(container.Resolve<Reception>().Greeter).Is<QuietGreeter>();
		}
	}

	public class Open
	{
		[Test]
		public async Task Factory_IsClosedPerClosing()
		{
			using OpenContainer.Root container = new();

			IStore<Order> orders = container.Resolve<IStore<Order>>();
			IStore<Customer> customers = container.Resolve<IStore<Customer>>();

			await That(orders).Is<Store<Order>>();
			await That(customers).Is<Store<Customer>>();
			await That(((Store<Order>)orders).Validator).Is<Validator<Order>>()
				.Because("the closed factory's parameters resolve from the graph for the same closing");
		}

		[Test]
		public async Task AsyncFactory_IsAwaitedPerClosing()
		{
			using OpenContainer.Root container = new();

			IFeed<Order> feed = await container.ResolveAsync<IFeed<Order>>();

			await That(feed).Is<Feed<Order>>();
		}

		[Test]
		public async Task Hooks_RunPerClosingAndSuppressDisposalSkipsTheDisposal()
		{
			using OpenContainer.Root container = new();
			Log log = container.Resolve<Log>();

			using (IAwaitenScope scope = container.CreateScope())
			{
				scope.Resolve<IConnection<Order>>();
				await That(log.Entries).Contains("opened:Order")
					.Because("the generic activation hook is closed over the closing's type argument");
			}

			await That(log.Entries).Contains("closed:Connection`1")
				.Because("the release hook runs for the closing when the owning scope is disposed");
			await That(log.Entries).DoesNotContain("disposed:Order")
				.Because("SuppressDisposal holds for every closing");
		}

		[Test]
		public async Task Fallback_YieldsPerClosing()
		{
			using OpenContainer.Root container = new();

			await That(container.Resolve<ICache<Order>>()).Is<OrderCache>()
				.Because("the closed registration replaces the module's open default for its closing");
			await That(container.Resolve<ICache<Customer>>()).Is<MemoryCache<Customer>>()
				.Because("the module's open default still fills every other closing");
		}

		[Test]
		public async Task Fallback_YieldsToAStrongerOpenRegistration()
		{
			using OverridingContainer.Root container = new();

			await That(container.Resolve<ICache<Order>>()).Is<DistributedCache<Order>>();
			await That(container.Resolve<ICache<Order>[]>()).HasCount(1)
				.Because("the overridden default closing is dropped in full, even from the collection");
		}

		[Test]
		public async Task WhenInjectedInto_BindsOnlyForTheConsumer()
		{
			using OpenContainer.Root container = new();

			await That(container.Resolve<Auditor>().Repository).Is<AuditedRepository<Order>>();
			await That(container.Resolve<IRepository<Order>>()).Is<Repository<Order>>()
				.Because("every other consumer keeps the unconditional closing");
		}
	}

	public sealed class Log
	{
		public List<string> Entries { get; } = new();
	}

	public sealed class Order;

	public sealed class Customer;

	public interface IClock;

	public sealed class FactoryClock : IClock;

	public sealed class Settings;

	public sealed class Warm
	{
		public Warm(Log log) => log.Entries.Add("warm");
	}

	public interface ISession;

	public sealed class Session : ISession, IDisposable
	{
		private readonly Log _log;

		public Session(Log log) => _log = log;

		public void Dispose() => _log.Entries.Add("disposed:Session");
	}

	public interface IGreeter;

	public sealed class DefaultGreeter : IGreeter;

	public sealed class CustomGreeter : IGreeter;

	public sealed class QuietGreeter : IGreeter;

	public sealed class Reception
	{
		public Reception(IGreeter greeter) => Greeter = greeter;

		public IGreeter Greeter { get; }
	}

	[Container]
	[Singleton(typeof(Log))]
	[Singleton(typeof(IClock), Factory = nameof(CreateClock))]
	[Singleton(typeof(Settings), Instance = nameof(SharedSettings))]
	[Singleton(typeof(Warm), Eager = true)]
	[Scoped(typeof(Session), typeof(ISession), OnActivated = nameof(Opened), OnRelease = nameof(Closed), SuppressDisposal = true)]
	[Singleton(typeof(DefaultGreeter), typeof(IGreeter), Fallback = Fallback.Warn)]
	[Singleton<CustomGreeter, IGreeter>]
	[Singleton(typeof(QuietGreeter), typeof(IGreeter), WhenInjectedInto = typeof(Reception))]
	[Transient(typeof(Reception))]
	public static partial class ClosedContainer
	{
		public static readonly Settings SharedSettings = new();

		private static IClock CreateClock() => new FactoryClock();

		private static void Opened(ISession session, Log log) => log.Entries.Add("opened:" + session.GetType().Name);

		private static void Closed(ISession session, Log log) => log.Entries.Add("closed:" + session.GetType().Name);
	}

	public interface IValidator<T>;

	public sealed class Validator<T> : IValidator<T>;

	public interface IStore<T>;

	public sealed class Store<T> : IStore<T>
	{
		public Store(IValidator<T> validator) => Validator = validator;

		public IValidator<T> Validator { get; }
	}

	public interface IFeed<T>;

	public sealed class Feed<T> : IFeed<T>;

	public interface IConnection<T>;

	public sealed class Connection<T> : IConnection<T>, IDisposable
	{
		private readonly Log _log;

		public Connection(Log log) => _log = log;

		public void Dispose() => _log.Entries.Add("disposed:" + typeof(T).Name);
	}

	public interface ICache<T>;

	public sealed class MemoryCache<T> : ICache<T>;

	public sealed class DistributedCache<T> : ICache<T>;

	public sealed class OrderCache : ICache<Order>;

	public interface IRepository<T>;

	public sealed class Repository<T> : IRepository<T>;

	public sealed class AuditedRepository<T> : IRepository<T>;

	public sealed class Auditor
	{
		public Auditor(IRepository<Order> repository) => Repository = repository;

		public IRepository<Order> Repository { get; }
	}

	public sealed class App
	{
		public App(
			IStore<Order> orders,
			IStore<Customer> customers,
			Task<IFeed<Order>> feed,
			IConnection<Order> connection,
			ICache<Order> orderCache,
			ICache<Customer> customerCache,
			IRepository<Order> repository)
		{
		}
	}

	[Module]
	[Singleton(typeof(MemoryCache<>), typeof(ICache<>), Fallback = Fallback.Warn)]
	public static class CacheModule;

	[Container]
	[Import(typeof(CacheModule))]
	[Singleton(typeof(Log))]
	[Scoped(typeof(IStore<>), Factory = nameof(CreateStore))]
	[Transient(typeof(Validator<>), typeof(IValidator<>))]
	[Singleton(typeof(IFeed<>), Factory = nameof(CreateFeedAsync))]
	[Scoped(typeof(Connection<>), typeof(IConnection<>), OnActivated = nameof(Opened), OnRelease = nameof(Closed), SuppressDisposal = true)]
	[Singleton(typeof(OrderCache), typeof(ICache<Order>))]
	[Transient(typeof(Repository<>), typeof(IRepository<>))]
	[Transient(typeof(AuditedRepository<>), typeof(IRepository<>), WhenInjectedInto = typeof(Auditor))]
	[Transient<Auditor>]
	[Transient<App>]
	public static partial class OpenContainer
	{
		private static IStore<T> CreateStore<T>(IValidator<T> validator) => new Store<T>(validator);

		private static Task<IFeed<T>> CreateFeedAsync<T>() => Task.FromResult<IFeed<T>>(new Feed<T>());

		private static void Opened<T>(IConnection<T> connection, Log log) => log.Entries.Add("opened:" + typeof(T).Name);

		private static void Closed(object connection, Log log) => log.Entries.Add("closed:" + connection.GetType().Name);
	}

	public sealed class CacheConsumer
	{
		public CacheConsumer(ICache<Order> cache, ICache<Order>[] all)
		{
		}
	}

	[Container]
	[Import(typeof(CacheModule))]
	[Singleton(typeof(DistributedCache<>), typeof(ICache<>))]
	[Transient<CacheConsumer>]
	public static partial class OverridingContainer;
}
