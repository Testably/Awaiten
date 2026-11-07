using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt216RegisteredTypeLessAccessible
	{
		[Test]
		[Arguments("Singleton")]
		[Arguments("Scoped")]
		[Arguments("Transient")]
		public async Task PrivateNestedImplementation_DoesNotReport(string lifetime)
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         public interface IClock { }

			                                         [Container]
			                                         [{{lifetime}}<FakeClock, IClock>]
			                                         public static partial class Shop
			                                         {
			                                             private sealed class FakeClock : IClock { }
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the resolvers return the accessible service, and only their bodies, nested in the container, name the private type");
		}

		[Test]
		public async Task PrivateNestedDecorator_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Container]
			                                       [Singleton<SystemClock, IClock>]
			                                       [Decorate<LoggingClock, IClock>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class LoggingClock : IClock { public LoggingClock(IClock inner) { } }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a decorator link resolves as the decorated service like any other private implementation");
		}

		[Test]
		public async Task TypeNestedInPrivateNestedType_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       [Container]
			                                       [Singleton<Fakes.FakeClock, IClock>]
			                                       public static partial class Shop
			                                       {
			                                           private static class Fakes
			                                           {
			                                               internal sealed class FakeClock : IClock { }
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("an internal type nested in a private type is reachable from the container's own members");
		}

		[Test]
		public async Task InternalContainerBesidePrivateType_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       public partial class ShopTests
			                                       {
			                                           private sealed class FakeClock : IClock { }

			                                           [Container]
			                                           [Singleton<FakeClock, IClock>]
			                                           internal static partial class Shop
			                                           {
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the container is nested beside the private type, so its resolver bodies may construct it");
		}

		[Test]
		public async Task PrivateNestedTypeOfImportedModule_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       [Module]
			                                       [Singleton<FakeClock, IClock>]
			                                       public static partial class ClockModule
			                                       {
			                                           private sealed class FakeClock : IClock { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the container builds the module's private type through the factory the module generates");
		}

		[Test]
		public async Task PrivateNestedTypeOfModuleInAnotherAssembly_DoesNotReport()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			                                                                      using Awaiten;

			                                                                      namespace Lib;

			                                                                      public interface IClock { }

			                                                                      [Module]
			                                                                      [Singleton<FakeClock, IClock>]
			                                                                      public static partial class ClockModule
			                                                                      {
			                                                                          private sealed class FakeClock : IClock { }
			                                                                      }
			                                                                      """, """
			                                                                           using Awaiten;

			                                                                           namespace MyCode;

			                                                                           [Container]
			                                                                           [Import(typeof(Lib.ClockModule))]
			                                                                           public static partial class Shop
			                                                                           {
			                                                                           }
			                                                                           """);

			await That(result.Diagnostics).IsEmpty()
				.Because("another assembly finds the module's generated factory by name, exactly as for an internal implementation");
		}

		[Test]
		public async Task PrivateNestedTypeOfModuleWithPrivateHooks_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class Log { }

			                                       [Module]
			                                       [Singleton<FakeClock, IClock>(OnActivated = nameof(Start), OnRelease = nameof(Stop))]
			                                       public static partial class ClockModule
			                                       {
			                                           private sealed class FakeClock : IClock { }

			                                           private static void Start(FakeClock clock, Log log) { }

			                                           private static void Stop(IClock clock) { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       [Singleton<Log>]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the container runs the module's private hooks through the public wrappers the module generates");
			await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("global::MyCode.ClockModule.Awaiten__Hook_OnActivated_FakeClock_").And
				.Contains("global::MyCode.ClockModule.Awaiten__Hook_OnRelease_FakeClock_");
		}

		[Test]
		public async Task PrivateNestedTypeOfModuleWithUnmirrorableHook_ReportsOnlyTheModuleDiagnostic()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class Log { }

			                                       [Module]
			                                       [Singleton<FakeClock, IClock>(OnActivated = nameof(Start))]
			                                       public static partial class ClockModule
			                                       {
			                                           private sealed class FakeClock : IClock { }

			                                           private static void Start(FakeClock clock, [FromKey("audit")] Log log) { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       [Singleton<Log>(Key = "audit")]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT204*").AsWildcard()
				.Because("the wrapper cannot mirror the key metadata of the hook parameter");
			await That(result.Diagnostics).DoesNotContain("*AWT164*").AsWildcard()
				.Because("the container does not report again the hook its module already rejected");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard()
				.Because("the container calls no wrapper the module did not emit");
		}

		[Test]
		public async Task PrivateNestedImplementationWithInjectedProperty_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public interface ILogger { }
			                                       public sealed class Logger : ILogger { }

			                                       [Container]
			                                       [Singleton<Logger, ILogger>]
			                                       [Singleton<FakeClock, IClock>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class FakeClock : IClock
			                                           {
			                                               [Inject] public ILogger Logger { get; set; } = null!;
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the object initializer sits in the resolver body, which may name the private type");
		}

		[Test]
		public async Task PrivateNestedImplementationWithContainerSideInjectedProperty_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public interface ILogger { }
			                                       public sealed class Logger : ILogger { }

			                                       [Container]
			                                       [Singleton<Logger, ILogger>]
			                                       [Singleton<FakeClock, IClock>]
			                                       [InjectProperty<FakeClock>(nameof(FakeClock.Logger))]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class FakeClock : IClock
			                                           {
			                                               public ILogger Logger { get; set; } = null!;
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the container-side entry fills the property in the resolver body, which may name the private type");
		}

		[Test]
		public async Task ContextualBindingForAPrivateConsumer_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public interface IReceipt { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Container]
			                                       [Singleton<SystemClock, IClock>]
			                                       [Singleton<FakeClock, IClock>(WhenInjectedInto = typeof(FakeReceipt))]
			                                       [Transient<FakeReceipt, IReceipt>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class FakeClock : IClock { }

			                                           private sealed class FakeReceipt : IReceipt { public FakeReceipt(IClock clock) { } }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the private consumer only keys the binding, and both private types are built in resolver bodies");
		}

		[Test]
		public async Task PrivateNestedTypeOfInternalModule_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       internal interface IClock { }

			                                       [Module]
			                                       [Singleton<FakeClock, IClock>]
			                                       internal static partial class ClockModule
			                                       {
			                                           private sealed class FakeClock : IClock { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       internal static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a module-private type is self-compiled whatever the module's own accessibility");
		}

		[Test]
		public async Task PrivateNestedTypeOfNonPartialModule_ReportsAwt194Only()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       [Module]
			                                       [Singleton<FakeClock, IClock>]
			                                       public static class ClockModule
			                                       {
			                                           private sealed class FakeClock : IClock { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT194*").AsWildcard();
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard()
				.Because("the container drops the registration instead of calling a factory that was never emitted");
		}

		[Test]
		public async Task PrivateNestedTypeOfModuleUnderSeveralServices_ReportsAwt197Only()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public interface ITimer { }

			                                       [Module]
			                                       [Singleton<FakeClock, IClock>]
			                                       [Singleton<FakeClock, ITimer>]
			                                       public static partial class ClockModule
			                                       {
			                                           private sealed class FakeClock : IClock, ITimer { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT197*").AsWildcard()
				.Because("one generated factory returns one service and cannot share the instance across two");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard();
		}

		[Test]
		public async Task PrivateImplementationUnderSeveralServicesWithoutCommonType_Reports()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public interface ITimer { }

			                                       [Container]
			                                       [Singleton<FakeClock, IClock>]
			                                       [Singleton<FakeClock, ITimer>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class FakeClock : IClock, ITimer { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Shop.FakeClock'*no accessible type it converts to covers*register it under one service, or give it an accessible base type that covers its services*").AsWildcard()
				.Because("no accessible type converts to both services, so one resolver cannot serve the shared instance");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard();
		}

		[Test]
		public async Task PrivateImplementationUnderSeveralServicesWithAccessibleBase_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public interface ITimer { }
			                                       public abstract class ClockBase : IClock, ITimer { }

			                                       [Container]
			                                       [Singleton<FakeClock, IClock>]
			                                       [Singleton<FakeClock, ITimer>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class FakeClock : ClockBase { }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the resolver returns the accessible base class, which converts to both services");
		}

		[Test]
		public async Task PrivateImplementationAsItsOwnService_Reports()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       [Container]
			                                       [Singleton<FakeClock>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class FakeClock { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Shop.FakeClock' cannot be registered: 'MyCode.Shop.FakeClock' is private or protected and it is registered as itself; register it under an accessible service*").AsWildcard()
				.Because("a resolver returning the private service itself would expose it");
			await That(result.Diagnostics).DoesNotContain("*private service*").AsWildcard()
				.Because("calling the implementation its own private service names it twice and reads as if it were a second type");
		}

		[Test]
		public async Task PrivateImplementationUnderAnotherPrivateService_NamesTheService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       [Container]
			                                       [Singleton<FakeClock, IFakeClock>]
			                                       public static partial class Shop
			                                       {
			                                           private interface IFakeClock { }

			                                           private sealed class FakeClock : IFakeClock { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Shop.FakeClock' is private or protected and it is registered as the private service 'MyCode.Shop.IFakeClock'*").AsWildcard();
		}

		[Test]
		public async Task ModuleRegistrationFactory_DescribesWhoCannotConstructTheImplementation()
		{
			(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator("""
			                                                                                  using Awaiten;

			                                                                                  namespace MyCode;

			                                                                                  public interface IClock { }

			                                                                                  [Module]
			                                                                                  [Singleton<FakeClock, IClock>]
			                                                                                  public static partial class ClockModule
			                                                                                  {
			                                                                                      private sealed class FakeClock : IClock { }
			                                                                                  }
			                                                                                  """, [], []);
			string module = run.Results
				.SelectMany(r => r.GeneratedSources)
				.Single(s => s.HintName.Contains("ModuleScan"))
				.SourceText.ToString();

			await That(module).Contains("constructs an implementation a consuming container cannot construct")
				.Because("no container constructs a type nested privately in the module, not even one in the module's own assembly");
			await That(module).DoesNotContain("another assembly");
		}

		[Test]
		public async Task PrivateNestedTypeArgument_Reports()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Box<T> { }

			                                       [Container]
			                                       [Transient<Box<Content>>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class Content { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Shop.Content' is private*type argument*make 'MyCode.Shop.Content' internal*").AsWildcard()
				.Because("a constructed generic type is only as visible as its least visible type argument");
		}

		[Test]
		public async Task ServiceClosedOverPrivateType_Reports()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IHandler<T> { }
			                                       public sealed class Handler<T> : IHandler<T> { }

			                                       [Container]
			                                       [Transient<Handler<Message>, IHandler<Message>>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class Message { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Shop.Message' is private*type argument*make 'MyCode.Shop.Message' internal*").AsWildcard()
				.Because("every type the implementation converts to carries the private type argument, so none can be returned");
		}

		[Test]
		public async Task PrivateNestedArgType_Reports()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IReceipt { }

			                                       [Container]
			                                       [Transient<IReceipt>(Factory = nameof(Print))]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class Order { }

			                                           private static IReceipt Print([Arg] Order order) => null!;
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT216*[Arg] type 'MyCode.Shop.Order' is private*make 'MyCode.Shop.Order' internal*").AsWildcard()
				.Because("a parameterized resolver takes its runtime arguments as parameters of an internal member");
		}

		[Test]
		public async Task PrivateNestedTypeOfExtendedBase_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       [Container]
			                                       [Singleton<FakeClock, IClock>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class FakeClock : IClock { }
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the base builds its private type itself, and the extending container calls the factory the base generates for it");
		}

		[Test]
		public async Task PrivateContainerBesidePrivateType_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       public partial class ShopTests
			                                       {
			                                           private sealed class FakeClock : IClock { }

			                                           [Container]
			                                           [Singleton<FakeClock, IClock>]
			                                           private static partial class Shop
			                                           {
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a container nested privately beside the type exposes its resolvers no further than the type is visible");
		}

		[Test]
		public async Task PrivateContainerInDerivedClassBesideProtectedType_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       public abstract class ShopTestBase
			                                       {
			                                           protected sealed class FakeClock : IClock { }
			                                       }

			                                       public partial class ShopTests : ShopTestBase
			                                       {
			                                           [Container]
			                                           [Singleton<FakeClock, IClock>]
			                                           private static partial class Shop
			                                           {
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a protected type stays visible in the derived class whose private container uses it");
		}

		[Test]
		public async Task PrivateTypeProducedBehindAccessibleRegisteredType_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public interface IPrinter { }

			                                       [Container]
			                                       [Singleton<IClock>(Factory = nameof(CreateClock))]
			                                       [Singleton<IPrinter>(Instance = nameof(Printer))]
			                                       public static partial class Shop
			                                       {
			                                           private static readonly FakePrinter Printer = new();

			                                           private static FakeClock CreateClock() => new();

			                                           private sealed class FakeClock : IClock { }

			                                           private sealed class FakePrinter : IPrinter { }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a Factory or Instance registration resolves as its accessible registered type, so the private type it produces stays inside the container");
		}

		[Test]
		[Arguments("Singleton")]
		[Arguments("Scoped")]
		public async Task PrivateNestedServiceOfAccessibleImplementation_DoesNotReport(string lifetime)
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using System.Threading;
			                                         using System.Threading.Tasks;
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         [Container]
			                                         [{{lifetime}}<SystemClock, IClock>]
			                                         [{{lifetime}}<WarmClock, IClock>]
			                                         public static partial class Shop
			                                         {
			                                             private interface IClock { }

			                                             public sealed class SystemClock : IClock { }

			                                             public sealed class WarmClock : IClock, IAsyncInitializable
			                                             {
			                                                 public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                             }
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the resolvers return the accessible implementations, and the members that name the private service (the singleton's cached typed task and the async collection resolver) stay out of the generated signatures");
		}

		[Test]
		public async Task PrivateNestedTypeOfAClassEnclosingTheModule_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       public static partial class Fakes
			                                       {
			                                           private sealed class FakeClock : IClock { }

			                                           [Module]
			                                           [Singleton<FakeClock, IClock>]
			                                           public static partial class ClockModule
			                                           {
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(Fakes.ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the module may access the private types of the class it is nested in, so its generated factory builds the type for the container");
			await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("global::MyCode.Fakes.ClockModule.Awaiten__Registration_FakeClock_");
		}

		[Test]
		public async Task PrivateNestedTypeTwoLevelsOutOfTheModule_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       public static partial class Fakes
			                                       {
			                                           private sealed class FakeClock : IClock { }

			                                           public static partial class Clocks
			                                           {
			                                               [Module]
			                                               [Singleton<FakeClock, IClock>]
			                                               public static partial class ClockModule
			                                               {
			                                               }
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(Fakes.Clocks.ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a nested type may access the private types of every class around it, at any depth");
			await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("global::MyCode.Fakes.Clocks.ClockModule.Awaiten__Registration_FakeClock_");
		}

		[Test]
		public async Task ProtectedNestedTypeOfABaseOfAClassEnclosingTheModule_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       public abstract class FakesBase
			                                       {
			                                           protected sealed class FakeClock : IClock { }
			                                       }

			                                       public partial class Fakes : FakesBase
			                                       {
			                                           [Module]
			                                           [Singleton<FakeClock, IClock>]
			                                           public static partial class ClockModule
			                                           {
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(Fakes.ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a module nested in a derived class may access the protected types of its base");
			await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("global::MyCode.Fakes.ClockModule.Awaiten__Registration_FakeClock_");
		}

		[Test]
		public async Task PrivateNestedTypeOfAClassEnclosingAnInternalModule_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       internal interface IClock { }

			                                       internal static partial class Fakes
			                                       {
			                                           private sealed class FakeClock : IClock { }

			                                           [Module]
			                                           [Singleton<FakeClock, IClock>]
			                                           internal static partial class ClockModule
			                                           {
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(Fakes.ClockModule))]
			                                       internal static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the module self-compiles a type no other code of its assembly can construct, whatever the module's own accessibility");
		}

		[Test]
		public async Task PrivateNestedTypeOfAClassEnclosingTheModuleWithPrivateHooks_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class Log { }

			                                       public static partial class Fakes
			                                       {
			                                           private sealed class FakeClock : IClock { }

			                                           [Module]
			                                           [Singleton<FakeClock, IClock>(OnActivated = nameof(Start), OnRelease = nameof(Stop))]
			                                           public static partial class ClockModule
			                                           {
			                                               private static void Start(FakeClock clock, Log log) { }

			                                               private static void Stop(IClock clock) { }
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(Fakes.ClockModule))]
			                                       [Singleton<Log>]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the container runs the module's private hooks through the public wrappers the module generates");
			await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("global::MyCode.Fakes.ClockModule.Awaiten__Hook_OnActivated_FakeClock_").And
				.Contains("global::MyCode.Fakes.ClockModule.Awaiten__Hook_OnRelease_FakeClock_");
		}

		[Test]
		public async Task PrivateNestedTypeOfANonPartialClassEnclosingTheModule_ReportsAwt194Only()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       public static class Fakes
			                                       {
			                                           private sealed class FakeClock : IClock { }

			                                           [Module]
			                                           [Singleton<FakeClock, IClock>]
			                                           public static partial class ClockModule
			                                           {
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(Fakes.ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT194*'MyCode.Fakes.ClockModule'*").AsWildcard()
				.Because("the generated factory re-opens every class around the module");
			await That(result.Diagnostics).DoesNotContain("*AWT216*").AsWildcard()
				.Because("the container drops the registration whose factory the module could not receive");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard();
		}

		[Test]
		public async Task PrivateNestedTypeOfAClassEnclosingTheModuleAsAPrivateService_Reports()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public static partial class Fakes
			                                       {
			                                           private interface IClock { }

			                                           private sealed class FakeClock : IClock { }

			                                           [Module]
			                                           [Singleton<FakeClock, IClock>]
			                                           public static partial class ClockModule
			                                           {
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(Fakes.ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Fakes.FakeClock'*registered as the private service 'MyCode.Fakes.IClock'*register it under an accessible service*").AsWildcard()
				.Because("the module's public factory cannot return a private service, so the module cannot build the type for the container");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard();
		}

		[Test]
		public async Task PrivateTypeTheModuleCannotReach_ReportsTheCompilerError()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       public static partial class Fakes
			                                       {
			                                           private sealed class FakeClock : IClock { }
			                                       }

			                                       [Module]
			                                       [Singleton<Fakes.FakeClock, IClock>]
			                                       public static partial class ClockModule
			                                       {
			                                       }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error CS0122*").AsWildcard()
				.Because("the module cannot name a type private to a class it is not nested in");
			await That(result.Diagnostics).DoesNotContain("*nest the container beside it*").AsWildcard()
				.Because("moving the container would not help a type the module registers");
		}

		[Test]
		public async Task PrivateNestedStructOfAClassEnclosingTheModule_ReportsThatImplementationsMustBeClasses()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       public static partial class Fakes
			                                       {
			                                           private struct FakeClock : IClock { }

			                                           [Module]
			                                           [Singleton<FakeClock, IClock>]
			                                           public static partial class ClockModule
			                                           {
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(Fakes.ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error CS0452*").AsWildcard()
				.Because("the registration attributes constrain the implementation to a class");
			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Fakes.FakeClock'*an implementation must be a class, so wrap 'MyCode.Fakes.FakeClock' in a class, or register an interface it implements with a Factory or Instance that returns it*").AsWildcard()
				.Because("making a struct internal would not make it a valid implementation");
			await That(result.Diagnostics).DoesNotContain("*internal*").AsWildcard()
				.Because("no access modifier makes a struct a supported implementation");
		}

		[Test]
		public async Task PrivateNestedTypeOfInternalNonPartialModule_ReportsAwt194()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       internal interface IClock { }

			                                       [Module]
			                                       [Singleton<FakeClock, IClock>]
			                                       internal static class ClockModule
			                                       {
			                                           private sealed class FakeClock : IClock { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT194*'MyCode.ClockModule'*").AsWildcard()
				.Because("a module-private implementation is self-compiled whatever the module's accessibility, so the module must be partial to receive its factory");
		}

		[Test]
		public async Task PrivateNestedTypeOfNonStaticModule_ReportsWithoutCompilerErrors()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       [Module]
			                                       [Singleton<FakeClock, IClock>]
			                                       public partial class ClockModule
			                                       {
			                                           private sealed class FakeClock : IClock { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT152*").AsWildcard();
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard()
				.Because("the container does not call a factory the non-static module never receives");
		}

		[Test]
		public async Task PrivateNestedTypeOfModuleThatAlsoScans_YieldsTheScanMatchToTheRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public interface IPrinter { }
			                                       internal sealed class Printer : IPrinter { }

			                                       [Module]
			                                       [Scan<IPrinter>(As = ScanAs.Marker)]
			                                       [Singleton<FakeClock, IClock>]
			                                       public static partial class ShopModule
			                                       {
			                                           private sealed class FakeClock : IClock, IPrinter { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(ShopModule))]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).HasSingle().Matching(diagnostic => diagnostic.Contains("warning AWT193")
			                                                                  && diagnostic.Contains("'MyCode.ShopModule.FakeClock' is private or protected and the module registers it explicitly"))
				.Because("a scan never turns its former silent skip of a private type into the AWT197 error, so the match yields to the registration's factory");
			await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("Awaiten__Registration_FakeClock_");
		}

		[Test]
		public async Task PrivateNestedTypeOfModuleInheritedThroughExtends_DoesNotReport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }

			                                       [Module]
			                                       [Singleton<FakeClock, IClock>]
			                                       public static partial class ClockModule
			                                       {
			                                           private sealed class FakeClock : IClock { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the extending container calls the module's public factory just like the base does");
		}

		[Test]
		public async Task TypedResolveOfAPrivateImplementation_DoesNotWarn()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                           using System.Threading;
			                                                           using System.Threading.Tasks;
			                                                           using Awaiten;

			                                                           namespace MyCode;

			                                                           public interface IClock { }
			                                                           public interface ITimer { }

			                                                           [Container]
			                                                           [Singleton<FakeClock, IClock>]
			                                                           [Singleton<WarmTimer, ITimer>]
			                                                           public static partial class Shop
			                                                           {
			                                                               private sealed class FakeClock : IClock { }

			                                                               private sealed class WarmTimer : ITimer, IAsyncInitializable
			                                                               {
			                                                                   public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                                               }
			                                                           }

			                                                           public static class Usage
			                                                           {
			                                                               public static async Task Use()
			                                                               {
			                                                                   using Shop.Root root = new();
			                                                                   root.Resolve<IClock>();
			                                                                   root.Resolve(typeof(IClock));
			                                                                   await root.ResolveAsync<ITimer>();
			                                                                   root.Resolve<ITimer>();
			                                                               }
			                                                           }
			                                                           """);

			await That(diagnostics.Where(d => d.Contains("AWT218"))).IsEmpty()
				.Because("the private implementations are registered under the services the calls name");
			await That(diagnostics.Where(d => d.Contains("AWT219"))).HasCount(1)
				.Because("only the synchronous resolve of the async-initialized private implementation is withheld");
		}

		public sealed class ResolverType
		{
			[Test]
			public async Task PrefersARegisteredServiceThatCoversTheOthers()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IClock { }
				                                       public interface ITickingClock : IClock { }
				                                       public abstract class ClockBase : ITickingClock { }

				                                       [Container]
				                                       [Singleton<FakeClock, IClock>]
				                                       [Singleton<FakeClock, ITickingClock>]
				                                       public static partial class Shop
				                                       {
				                                           private sealed class FakeClock : ClockBase { }
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty();
				await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("internal static global::MyCode.ITickingClock ").And
					.DoesNotContain("internal static global::MyCode.ClockBase ")
					.Because("a registered service that converts to every other service wins over an unregistered base class");
			}

			[Test]
			public async Task PrefersTheMostDerivedAccessibleBaseClass()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IClock { }
				                                       public interface ITimer { }
				                                       public interface IBoth : IClock, ITimer { }
				                                       public abstract class ClockBase : IBoth { }
				                                       public abstract class FakeBase : ClockBase { }

				                                       [Container]
				                                       [Singleton<FakeClock, IClock>]
				                                       [Singleton<FakeClock, ITimer>]
				                                       public static partial class Shop
				                                       {
				                                           private sealed class FakeClock : FakeBase { }
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty();
				await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("internal static global::MyCode.FakeBase ")
					.Because("the most derived accessible base class covering every service wins over its bases and over an interface");
			}

			[Test]
			[Arguments("IFirst, ISecond", "IFirst")]
			[Arguments("ISecond, IFirst", "ISecond")]
			public async Task FallsBackToTheFirstDeclaredCoveringInterface(string interfaces, string expected)
			{
				GeneratorResult result = Generator.Run($$"""
				                                         using Awaiten;

				                                         namespace MyCode;

				                                         public interface IClock { }
				                                         public interface ITimer { }
				                                         public interface IFirst : IClock, ITimer { }
				                                         public interface ISecond : IClock, ITimer { }

				                                         [Container]
				                                         [Singleton<FakeClock, IClock>]
				                                         [Singleton<FakeClock, ITimer>]
				                                         public static partial class Shop
				                                         {
				                                             private sealed class FakeClock : {{interfaces}} { }
				                                         }
				                                         """);

				await That(result.Diagnostics).IsEmpty();
				await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains($"internal static global::MyCode.{expected} ")
					.Because("two covering interfaces are tied, and the one the implementation declares first wins");
			}

			[Test]
			public async Task IsTheSameForEveryRun()
			{
				const string source = """
				                      using Awaiten;

				                      namespace MyCode;

				                      public interface IClock { }
				                      public interface ITimer { }
				                      public interface IFirst : IClock, ITimer { }
				                      public interface ISecond : IClock, ITimer { }

				                      [Container]
				                      [Singleton<FakeClock, IClock>]
				                      [Singleton<FakeClock, ITimer>]
				                      public static partial class Shop
				                      {
				                          private sealed class FakeClock : ISecond, IFirst { }
				                      }
				                      """;

				string first = Generator.Run(source).Sources["Awaiten.MyCode.Shop.g.cs"];
				string second = Generator.Run(source).Sources["Awaiten.MyCode.Shop.g.cs"];

				await That(second).IsEqualTo(first)
					.Because("the resolver type is chosen by a fixed order, so generated code does not change between builds");
			}
		}
	}
}
