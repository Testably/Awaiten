namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Open generic expansion seeded from injected members and lifecycle hook parameters: a closing that only an open
///     registration provides is constructed for them exactly as for a constructor parameter, under the same
///     constraint checks (AWT126) and recursion limits (AWT129).
/// </summary>
public class OpenGenericInjectedMemberTests
{
	[Test]
	public async Task InjectPropertyEntry_ClosesTheOpenRegistration()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T> { }
		                                       public sealed class OrderService
		                                       {
		                                           public IRepository<Order>? Orders { get; set; }
		                                       }

		                                       [Container]
		                                       [Singleton(typeof(Repository<>), typeof(IRepository<>))]
		                                       [Singleton<OrderService>]
		                                       [InjectProperty<OrderService>(nameof(OrderService.Orders))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
			.Because("the injected property requests IRepository<Order>, so the open registration is closed for it");
	}

	[Test]
	public async Task InjectAttributeProperty_ClosesTheOpenRegistration()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T> { }
		                                       public sealed class OrderService
		                                       {
		                                           [Inject]
		                                           public IRepository<Order>? Orders { get; set; }
		                                       }

		                                       [Container]
		                                       [Singleton(typeof(Repository<>), typeof(IRepository<>))]
		                                       [Singleton<OrderService>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
			.Because("an [Inject] property seeds expansion like a constructor parameter");
	}

	[Test]
	public async Task OpenTypeofEntry_ClosesTheOpenRegistrationPerClosing()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IValidator<T> { }
		                                       public sealed class Validator<T> : IValidator<T> { }
		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T>
		                                       {
		                                           public IValidator<T>? Validator { get; set; }
		                                       }
		                                       public sealed class Root { public Root(IRepository<Order> orders) { } }

		                                       [Container]
		                                       [Singleton(typeof(Repository<>), typeof(IRepository<>))]
		                                       [Singleton(typeof(Validator<>), typeof(IValidator<>))]
		                                       [Singleton<Root>]
		                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Validator))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
			.Because("the open entry applies to Repository<Order>, whose property requests IValidator<Order>");
	}

	[Test]
	public async Task EntryOnAFactoryProducedImplementation_DoesNotClose()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T> { }
		                                       public sealed class OrderService
		                                       {
		                                           [Inject]
		                                           public IRepository<Order>? Orders { get; set; }
		                                       }
		                                       public sealed class Root { public Root(OrderService service) { } }

		                                       [Container]
		                                       [Singleton(typeof(Repository<>), typeof(IRepository<>))]
		                                       [Singleton<OrderService>(Factory = nameof(CreateService))]
		                                       [Singleton<Root>]
		                                       public static partial class MyContainer
		                                       {
		                                           private static OrderService CreateService() => new();
		                                       }
		                                       """);

		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("Repository<global::MyCode.Order>")
			.Because("a factory-produced instance is not property-injected, so its [Inject] property needs no closing");
	}

	[Test]
	public async Task HookParameter_ClosesTheOpenRegistration()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order { }
		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T> { }
		                                       public sealed class OrderService { }

		                                       [Container]
		                                       [Singleton(typeof(Repository<>), typeof(IRepository<>))]
		                                       [Singleton<OrderService>(OnActivated = nameof(Activated))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static void Activated(OrderService service, IRepository<Order> orders) { }
		                                       }
		                                       """);

		await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
			.Because("a lifecycle hook parameter is resolved like a constructor parameter, so it seeds expansion too");
	}

	[Test]
	public async Task PropertySeed_StillReportsAConstraintViolation()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T> where T : class { }
		                                       public sealed class OrderService
		                                       {
		                                           [Inject]
		                                           public IRepository<int>? Numbers { get; set; }
		                                       }

		                                       [Container]
		                                       [Singleton(typeof(Repository<>), typeof(IRepository<>))]
		                                       [Singleton<OrderService>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT126*").AsWildcard()
			.Because("Repository<int> violates where T : class even when the closing is requested by a property");
		await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
			.Because("the constraint-rejected closing is reported once as AWT126, as for a constructor parameter");
	}

	[Test]
	public async Task PropertySeed_StillStopsAnUnboundedRecursion()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public sealed class Seed { }
		                                       public sealed class Node<T>
		                                       {
		                                           [Inject]
		                                           public Node<List<T>>? Next { get; set; }
		                                       }
		                                       public sealed class Root { public Root(Node<Seed> node) { } }

		                                       [Container]
		                                       [Transient(typeof(Node<>))]
		                                       [Transient<Root>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT129*").AsWildcard()
			.Because("a property that requests an ever-larger closing recurses without bound, like a constructor parameter");
	}
}
