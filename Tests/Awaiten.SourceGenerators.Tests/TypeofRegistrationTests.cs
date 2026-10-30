namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The <c>typeof</c> forms of <c>[Singleton]</c>/<c>[Scoped]</c>/<c>[Transient]</c> carry the options of the generic
///     forms. A closed pair registers exactly like the generic form; an open generic applies each option per closing,
///     with a generic <c>Factory</c> closed over the closing's type arguments and generic hooks bound to the closed
///     service.
/// </summary>
public class TypeofRegistrationTests
{
	private const string Model = """
	                             public sealed class Order { }
	                             public sealed class Customer { }
	                             public interface IRepository<T> { }
	                             public sealed class Repository<T> : IRepository<T> { }
	                             public sealed class AuditedRepository<T> : IRepository<T> { }
	                             public interface IClock { }
	                             public sealed class Clock : IClock { }
	                             public sealed class FakeClock : IClock { }
	                             """;

	public class Closed
	{
		[Test]
		public async Task Pair_RegistersLikeTheGenericForm()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}

			                                         [Container]
			                                         [Singleton(typeof(Clock), typeof(IClock))]
			                                         [Transient(typeof(Repository<Order>), typeof(IRepository<Order>))]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("new global::MyCode.Clock()")
				.Because("a closed typeof pair is a normal registration of the implementation");
			await That(source).Contains("new global::MyCode.Repository<global::MyCode.Order>()")
				.Because("a closed generic pair keeps its type arguments");
		}

		[Test]
		public async Task Factory_CallsTheFactoryMethod()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}

			                                         [Container]
			                                         [Singleton(typeof(IClock), Factory = nameof(CreateClock))]
			                                         public static partial class MyContainer
			                                         {
			                                         	private static IClock CreateClock() => new Clock();
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("CreateClock()")
				.Because("the closed typeof form produces the instance through its factory");
		}

		[Test]
		public async Task Instance_ExposesTheMember()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}

			                                         [Container]
			                                         [Singleton(typeof(Clock), typeof(IClock), Instance = nameof(Shared))]
			                                         public static partial class MyContainer
			                                         {
			                                         	private static readonly Clock Shared = new();
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("new global::MyCode.Clock()")
				.Because("a pre-built instance is handed back, never constructed");
		}

		[Test]
		public async Task Eager_ConstructsInTheRootConstructor()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}

			                                         [Container]
			                                         [Singleton(typeof(Clock), Eager = true)]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("ResolveClock(this);")
				.Because("a closed typeof singleton honors Eager like the generic form");
		}

		[Test]
		public async Task Hooks_AreCalled()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}

			                                         [Container]
			                                         [Scoped(typeof(Clock), typeof(IClock), OnActivated = nameof(Start), OnRelease = nameof(Stop))]
			                                         public static partial class MyContainer
			                                         {
			                                         	private static void Start(IClock clock) { }
			                                         	private static void Stop(IClock clock) { }
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("Start(").Because("the activation hook runs after construction");
			await That(source).Contains("Stop(").Because("the release hook runs on teardown");
		}

		[Test]
		public async Task Fallback_YieldsToAnExplicitRegistration()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}

			                                         [Container]
			                                         [Singleton(typeof(Clock), typeof(IClock), Fallback = Fallback.Warn)]
			                                         [Singleton<FakeClock, IClock>]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("new global::MyCode.Clock()")
				.Because("the overridable default is dropped in full when another registration provides the service");
		}

		[Test]
		public async Task SuppressDisposal_IsAccepted()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         public interface IConnection { }
			                                         public sealed class Connection : IConnection, System.IDisposable { public void Dispose() { } }

			                                         [Container]
			                                         [Singleton(typeof(Connection), typeof(IConnection), SuppressDisposal = true)]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("__disposables ??=")
				.Because("the container does not dispose an instance whose disposal is suppressed");
		}

		[Test]
		public async Task WhenInjectedInto_BindsOnlyForTheConsumer()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public sealed class Audit { public Audit(IClock clock) { } }

			                                         [Container]
			                                         [Singleton<Clock, IClock>]
			                                         [Singleton(typeof(FakeClock), typeof(IClock), WhenInjectedInto = typeof(Audit))]
			                                         [Transient<Audit>]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("new global::MyCode.FakeClock()")
				.Because("the contextual registration is built for its consumer");
		}
	}

	public class Open
	{
		[Test]
		public async Task Factory_IsClosedPerClosing()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public sealed class Root { public Root(IRepository<Order> orders, IRepository<Customer> customers) { } }

			                                         [Container]
			                                         [Scoped(typeof(IRepository<>), Factory = nameof(Create))]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         	private static IRepository<T> Create<T>() => new Repository<T>();
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("Create<global::MyCode.Order>()")
				.Because("the generic factory is closed with the Order closing's type argument");
			await That(source).Contains("Create<global::MyCode.Customer>()")
				.Because("each closing calls its own closed factory");
		}

		[Test]
		public async Task Factory_ResolvesItsParametersPerClosing()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public interface IValidator<T> { }
			                                         public sealed class Validator<T> : IValidator<T> { }
			                                         public sealed class Root { public Root(IRepository<Order> orders) { } }

			                                         [Container]
			                                         [Transient(typeof(Repository<>), typeof(IRepository<>), Factory = nameof(Create))]
			                                         [Transient(typeof(Validator<>), typeof(IValidator<>))]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         	private static Repository<T> Create<T>(IValidator<T> validator) => new();
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("new global::MyCode.Validator<global::MyCode.Order>()")
				.Because("an open generic the closed factory depends on is expanded for the same closing");
		}

		[Test]
		public async Task AsyncFactory_IsAwaited()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using System.Threading.Tasks;
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public sealed class Root { public Root(IRepository<Order> orders) { } }

			                                         [Container]
			                                         [Singleton(typeof(IRepository<>), Factory = nameof(CreateAsync))]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         	private static Task<IRepository<T>> CreateAsync<T>() => Task.FromResult<IRepository<T>>(new Repository<T>());
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("await CreateAsync<global::MyCode.Order>().ConfigureAwait(false)")
				.Because("an async generic factory taints its closing like a non-generic async factory");
		}

		[Test]
		public async Task Factory_FollowsTheNonGenericParameterRules()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using System;
			                                         using System.Threading;
			                                         using System.Threading.Tasks;
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public interface INamed<T> { }
			                                         public sealed class Named<T> : INamed<T> { }
			                                         public interface IFeed<T> { }
			                                         public sealed class Feed<T> : IFeed<T> { }
			                                         public sealed class Root
			                                         {
			                                         	public Root(IRepository<Order> orders, Func<string, INamed<Order>> named, Task<IFeed<Order>> feed) { }
			                                         }

			                                         [Container]
			                                         [Transient(typeof(IRepository<>), Factory = nameof(Create))]
			                                         [Transient(typeof(INamed<>), Factory = nameof(CreateNamed))]
			                                         [Singleton(typeof(IFeed<>), Factory = nameof(CreateFeedAsync))]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         	private static IRepository<T> Create<T>([RequestingType] Type requester) => new Repository<T>();
			                                         	private static INamed<T> CreateNamed<T>([Arg] string name) => new Named<T>();
			                                         	private static Task<IFeed<T>> CreateFeedAsync<T>(CancellationToken cancellationToken) => Task.FromResult<IFeed<T>>(new Feed<T>());
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty()
				.Because("[RequestingType], a runtime [Arg] through Func<..., T> and a forwarded CancellationToken work on a generic factory as on a non-generic one");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("Create<global::MyCode.Order>(__requestingType!)")
				.Because("the requesting type is passed in by the consumer's construction site, as for a non-generic factory");
			await That(source).Contains("typeof(global::MyCode.Root)");
			await That(source).Contains("CreateNamed<global::MyCode.Order>(");
			await That(source).Contains("CreateFeedAsync<global::MyCode.Order>(cancellationToken)");
		}

		[Test]
		public async Task OverloadedFactory_ReportsAwt112()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public sealed class Root { public Root(IRepository<Order> orders) { } }

			                                         [Container]
			                                         [Transient(typeof(IRepository<>), Factory = nameof(Create))]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         	private static IRepository<T> Create<T>() => new Repository<T>();
			                                         	private static IRepository<T> Create<T>(Order order) => new Repository<T>();
			                                         }
			                                         """);

			await That(result.Diagnostics).Contains("*AWT112*").AsWildcard()
				.Because("an overloaded generic factory is as ambiguous as an overloaded non-generic one");
		}

		[Test]
		public async Task FactoryReturningAnotherType_ReportsAwt108()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public sealed class Root { public Root(IRepository<Order> orders) { } }

			                                         [Container]
			                                         [Transient(typeof(IRepository<>), Factory = nameof(Create))]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         	private static IClock Create<T>() => new Clock();
			                                         }
			                                         """);

			await That(result.Diagnostics).Contains("*AWT108*").AsWildcard()
				.Because("the closed factory must produce the closing");
		}

		[Test]
		public async Task Hooks_AreBoundToTheClosing()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public sealed class Root { public Root(IRepository<Order> orders) { } }

			                                         [Container]
			                                         [Transient(typeof(Repository<>), typeof(IRepository<>), OnActivated = nameof(Track), OnRelease = nameof(Untrack))]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         	private static void Track<T>(IRepository<T> repository) { }
			                                         	private static void Untrack(object repository) { }
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("Track<global::MyCode.Order>(")
				.Because("a generic hook binds its type argument from the closed service");
			await That(source).Contains("Untrack(")
				.Because("a non-generic hook works on every closing");
		}

		[Test]
		public async Task Fallback_YieldsToAStrongerOpenRegistration()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public sealed class Root { public Root(IRepository<Order> orders) { } }

			                                         [Container]
			                                         [Transient(typeof(Repository<>), typeof(IRepository<>), Fallback = Fallback.Warn)]
			                                         [Transient(typeof(AuditedRepository<>), typeof(IRepository<>))]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("new global::MyCode.AuditedRepository<global::MyCode.Order>()");
			await That(source).DoesNotContain("new global::MyCode.Repository<global::MyCode.Order>()")
				.Because("the overridable default closing is dropped in full, even from the collection");
		}

		[Test]
		public async Task Fallback_YieldsPerClosingToAClosedRegistration()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public sealed class OrderRepository : IRepository<Order> { }
			                                         public sealed class Root { public Root(IRepository<Order> orders, IRepository<Customer> customers) { } }

			                                         [Container]
			                                         [Transient(typeof(Repository<>), typeof(IRepository<>), Fallback = Fallback.Silent)]
			                                         [Transient<OrderRepository, IRepository<Order>>]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).DoesNotContain("new global::MyCode.Repository<global::MyCode.Order>()")
				.Because("the closed registration replaces the default for its closing");
			await That(source).Contains("new global::MyCode.Repository<global::MyCode.Customer>()")
				.Because("the default still fills every other closing");
		}

		[Test]
		public async Task TwoWarnFallbacks_ReportAwt148()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public sealed class Root { public Root(IRepository<Order> orders) { } }

			                                         [Container]
			                                         [Transient(typeof(Repository<>), typeof(IRepository<>), Fallback = Fallback.Warn)]
			                                         [Transient(typeof(AuditedRepository<>), typeof(IRepository<>), Fallback = Fallback.Warn)]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).Contains("*AWT148*").AsWildcard()
				.Because("two overridable defaults compete for the closing with nothing stronger to decide");
		}

		[Test]
		public async Task SuppressDisposal_IsAccepted()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         public sealed class Order { }
			                                         public interface IConnection<T> { }
			                                         public sealed class Connection<T> : IConnection<T>, System.IDisposable { public void Dispose() { } }
			                                         public sealed class Root { public Root(IConnection<Order> connection) { } }

			                                         [Container]
			                                         [Singleton(typeof(Connection<>), typeof(IConnection<>), SuppressDisposal = true)]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("__disposables ??=")
				.Because("the container does not dispose a closing whose disposal is suppressed");
		}

		[Test]
		public async Task WhenInjectedInto_BindsOnlyForTheConsumer()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public sealed class Audit { public Audit(IRepository<Order> orders) { } }
			                                         public sealed class Root { public Root(IRepository<Order> orders, Audit audit) { } }

			                                         [Container]
			                                         [Transient(typeof(Repository<>), typeof(IRepository<>))]
			                                         [Transient(typeof(AuditedRepository<>), typeof(IRepository<>), WhenInjectedInto = typeof(Audit))]
			                                         [Transient<Audit>]
			                                         [Transient<Root>]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("new global::MyCode.AuditedRepository<global::MyCode.Order>()")
				.Because("the contextual closing is built for its consumer");
			await That(source).Contains("new global::MyCode.Repository<global::MyCode.Order>()")
				.Because("every other consumer keeps the unconditional closing");
		}

		[Test]
		public async Task WhenInjectedIntoWithKey_ReportsAwt168()
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{Model}}
			                                         public sealed class Audit { public Audit(IRepository<Order> orders) { } }

			                                         [Container]
			                                         [Transient(typeof(Repository<>), typeof(IRepository<>), Key = "audit", WhenInjectedInto = typeof(Audit))]
			                                         [Transient<Audit>]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).Contains("*AWT168*").AsWildcard()
				.Because("the contextual key overrides the explicit Key on an open generic too");
		}
	}
}
