using System.Collections.Generic;
using System.Linq;

namespace Awaiten.Tests;

/// <summary>
///     An open generic <c>Factory</c> registration that names only its service is identified per closing by its
///     member, as the closed form is: two open factories of one service are two implementations of every closing, the
///     container's wins over a module's, the first wins within one container, and both are members of the collection.
/// </summary>
public partial class OpenGenericFactoryIdentityTests
{
	public class InOneContainer
	{
		[Test]
		public async Task TwoFactories_FirstWinsAndBothAreCollectionMembers()
		{
			using TwoFactoriesContainer.Root container = new();

			Shelf shelf = container.Resolve<Shelf>();

			await That(shelf.Single.Name).IsEqualTo("a<Order>")
				.Because("the first registration wins single resolution");
			await That(Names(shelf.All)).IsEqualTo(["a<Order>", "b<Order>",])
				.Because("each factory is its own implementation of the closing, so both are collection members in registration order");
		}

		[Test]
		public async Task TwoFactories_KeepTheirOwnLifetimes()
		{
			using MixedLifetimeContainer.Root container = new();

			IRepository<Order>[] first = container.Resolve<Shelf>().All.ToArray();
			IRepository<Order>[] second = container.Resolve<Shelf>().All.ToArray();

			await That(first[0]).IsSameAs(second[0])
				.Because("the singleton factory's closing is cached");
			await That(first[1]).IsNotSameAs(second[1])
				.Because("the transient factory produces a new instance per resolution");
		}

		[Test]
		public async Task TheSameFactoryTwice_IsOneMember()
		{
			using SameFactoryContainer.Root container = new();

			Shelf shelf = container.Resolve<Shelf>();

			await That(Names(shelf.All)).IsEqualTo(["a<Order>",])
				.Because("registrations naming the same member of the same type are one implementation");
		}

		[Test]
		public async Task FactoryParameter_ReceivesBothFactories()
		{
			using FactoryParameterContainer.Root container = new();

			await That(Names(container.Resolve<Report>().Repositories)).IsEqualTo(["a<Order>", "b<Order>",])
				.Because("a closing requested only by a factory parameter is produced by every open factory");
		}

		[Test]
		public async Task DecoratorParameter_ReceivesBothFactories()
		{
			using DecoratorParameterContainer.Root container = new();

			await That(Names(((AuditedShop)container.Resolve<IShop>()).Repositories)).IsEqualTo(["a<Order>", "b<Order>",])
				.Because("a closing requested only by a decorator parameter is produced by every open factory");
		}

		[Test]
		public async Task Decorator_WrapsEachFactory()
		{
			using DecoratedContainer.Root container = new();

			Shelf shelf = container.Resolve<Shelf>();

			await That(shelf.Single.Name).IsEqualTo("logged a<Order>");
			await That(Names(shelf.All)).IsEqualTo(["logged a<Order>", "logged b<Order>",])
				.Because("each factory's closing is a separate base implementation, so the open decorator wraps each");
		}

		[Test]
		public async Task Composite_FansOutToEachFactory()
		{
			using CompositeContainer.Root container = new();

			await That(container.Resolve<Shelf>().Single.Name).IsEqualTo("all[a<Order>,b<Order>]")
				.Because("the open composite's collection holds the closing of each factory");
		}
	}

	public class OverAModule
	{
		[Test]
		public async Task ContainerFactory_WinsOverAModuleFactoryOfTheSameName()
		{
			using OverModuleContainer.Root container = new();

			Shelf shelf = container.Resolve<Shelf>();

			await That(shelf.Single.Name).IsEqualTo("container<Order>")
				.Because("the container's Make wins over the module's Make");
			await That(Names(shelf.All)).IsEqualTo(["container<Order>", "module<Order>",])
				.Because("the two members of the same name are different factories, so both are collection members");
		}

		[Test]
		public async Task FallbackModuleFactory_IsReplacedByTheContainerFactory()
		{
			using OverFallbackModuleContainer.Root container = new();

			Shelf shelf = container.Resolve<Shelf>();

			await That(shelf.Single.Name).IsEqualTo("container<Order>");
			await That(Names(shelf.All)).IsEqualTo(["container<Order>",])
				.Because("an overridden Fallback closing is dropped in full, even from the collection");
		}
	}

	public class KeyedAndContextual
	{
		[Test]
		public async Task KeyedFactories_ResolveTheirOwnMember()
		{
			using KeyedContainer.Root container = new();

			KeyedShelf shelf = container.Resolve<KeyedShelf>();

			await That(shelf.A.Name).IsEqualTo("a<Order>");
			await That(shelf.B.Name).IsEqualTo("b<Order>");
			await That(shelf.All.OrderBy(pair => pair.Key).Select(pair => pair.Value.Name)).IsEqualTo(["a<Order>", "b<Order>",])
				.Because("each key is produced by its own factory");
		}

		[Test]
		public async Task ContextualFactory_ReachesOnlyItsConsumer()
		{
			using ContextualContainer.Root container = new();

			await That(container.Resolve<Auditor>().Repository.Name).IsEqualTo("b<Order>")
				.Because("the named consumer receives the contextual factory's closing");
			await That(container.Resolve<Shelf>().Single.Name).IsEqualTo("a<Order>")
				.Because("every other consumer keeps the unconditional factory");
		}
	}

	private static string[] Names(IEnumerable<IRepository<Order>> repositories) => repositories.Select(repository => repository.Name).ToArray();

	public sealed class Order;

	public interface IRepository<T>
	{
		string Name { get; }
	}

	public sealed class Repository<T>(string name) : IRepository<T>
	{
		public string Name { get; } = $"{name}<{typeof(T).Name}>";
	}

	public sealed class LoggedRepository<T>(IRepository<T> inner) : IRepository<T>
	{
		public string Name { get; } = "logged " + inner.Name;
	}

	public sealed class CompositeRepository<T>(IEnumerable<IRepository<T>> inner) : IRepository<T>
	{
		public string Name { get; } = $"all[{string.Join(",", inner.Select(repository => repository.Name))}]";
	}

	public sealed class Shelf(IRepository<Order> single, IEnumerable<IRepository<Order>> all)
	{
		public IRepository<Order> Single { get; } = single;

		public IEnumerable<IRepository<Order>> All { get; } = all;
	}

	public sealed class KeyedShelf(
		[FromKey("a")] IRepository<Order> a,
		[FromKey("b")] IRepository<Order> b,
		IReadOnlyDictionary<string, IRepository<Order>> all)
	{
		public IRepository<Order> A { get; } = a;

		public IRepository<Order> B { get; } = b;

		public IReadOnlyDictionary<string, IRepository<Order>> All { get; } = all;
	}

	public sealed class Auditor(IRepository<Order> repository)
	{
		public IRepository<Order> Repository { get; } = repository;
	}

	public sealed class Report(IEnumerable<IRepository<Order>> repositories)
	{
		public IEnumerable<IRepository<Order>> Repositories { get; } = repositories;
	}

	[Container]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeA))]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeB))]
	[Transient<Shelf>]
	public static partial class TwoFactoriesContainer
	{
		public static IRepository<T> MakeA<T>() => new Repository<T>("a");

		public static IRepository<T> MakeB<T>() => new Repository<T>("b");
	}

	[Container]
	[Singleton(typeof(IRepository<>), Factory = nameof(MakeA))]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeB))]
	[Transient<Shelf>]
	public static partial class MixedLifetimeContainer
	{
		public static IRepository<T> MakeA<T>() => new Repository<T>("a");

		public static IRepository<T> MakeB<T>() => new Repository<T>("b");
	}

	[Container]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeA))]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeA))]
	[Transient<Shelf>]
	public static partial class SameFactoryContainer
	{
		public static IRepository<T> MakeA<T>() => new Repository<T>("a");
	}

	[Container]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeA))]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeB))]
	[Transient<Report>(Factory = nameof(CreateReport))]
	public static partial class FactoryParameterContainer
	{
		public static IRepository<T> MakeA<T>() => new Repository<T>("a");

		public static IRepository<T> MakeB<T>() => new Repository<T>("b");

		public static Report CreateReport(IEnumerable<IRepository<Order>> repositories) => new(repositories);
	}

	public interface IShop;

	public sealed class Shop : IShop;

	public sealed class AuditedShop(IShop inner, IEnumerable<IRepository<Order>> repositories) : IShop
	{
		public IShop Inner { get; } = inner;

		public IEnumerable<IRepository<Order>> Repositories { get; } = repositories;
	}

	[Container]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeA))]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeB))]
	[Transient<Shop, IShop>]
	[Decorate<AuditedShop, IShop>]
	public static partial class DecoratorParameterContainer
	{
		public static IRepository<T> MakeA<T>() => new Repository<T>("a");

		public static IRepository<T> MakeB<T>() => new Repository<T>("b");
	}

	[Container]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeA))]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeB))]
	[Decorate(typeof(LoggedRepository<>), typeof(IRepository<>))]
	[Transient<Shelf>]
	public static partial class DecoratedContainer
	{
		public static IRepository<T> MakeA<T>() => new Repository<T>("a");

		public static IRepository<T> MakeB<T>() => new Repository<T>("b");
	}

	[Container]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeA))]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeB))]
	[Composite(typeof(CompositeRepository<>), typeof(IRepository<>))]
	[Transient<Shelf>]
	public static partial class CompositeContainer
	{
		public static IRepository<T> MakeA<T>() => new Repository<T>("a");

		public static IRepository<T> MakeB<T>() => new Repository<T>("b");
	}

	[Module]
	[Transient(typeof(IRepository<>), Factory = nameof(Make))]
	public static class RepositoryModule
	{
		public static IRepository<T> Make<T>() => new Repository<T>("module");
	}

	[Container]
	[Import(typeof(RepositoryModule))]
	[Transient(typeof(IRepository<>), Factory = nameof(Make))]
	[Transient<Shelf>]
	public static partial class OverModuleContainer
	{
		public static IRepository<T> Make<T>() => new Repository<T>("container");
	}

	[Module]
	[Transient(typeof(IRepository<>), Factory = nameof(Make), Fallback = Fallback.Silent)]
	public static class FallbackRepositoryModule
	{
		public static IRepository<T> Make<T>() => new Repository<T>("module");
	}

	[Container]
	[Import(typeof(FallbackRepositoryModule))]
	[Transient(typeof(IRepository<>), Factory = nameof(Make))]
	[Transient<Shelf>]
	public static partial class OverFallbackModuleContainer
	{
		public static IRepository<T> Make<T>() => new Repository<T>("container");
	}

	[Container]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeA), Key = "a")]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeB), Key = "b")]
	[Transient<KeyedShelf>]
	public static partial class KeyedContainer
	{
		public static IRepository<T> MakeA<T>() => new Repository<T>("a");

		public static IRepository<T> MakeB<T>() => new Repository<T>("b");
	}

	[Container]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeA))]
	[Transient(typeof(IRepository<>), Factory = nameof(MakeB), WhenInjectedInto = typeof(Auditor))]
	[Transient<Auditor>]
	[Transient<Shelf>]
	public static partial class ContextualContainer
	{
		public static IRepository<T> MakeA<T>() => new Repository<T>("a");

		public static IRepository<T> MakeB<T>() => new Repository<T>("b");
	}
}
