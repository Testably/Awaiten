namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt216RegisteredTypeLessAccessible
	{
		[Test]
		[Arguments("Singleton")]
		[Arguments("Scoped")]
		[Arguments("Transient")]
		public async Task PrivateNestedImplementation_Reports(string lifetime)
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

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Shop.FakeClock'*").AsWildcard()
				.Because("the generated resolver returns the implementation from an internal member, which C# rejects for a private type");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard()
				.Because("the registration is skipped, so the generated container still compiles around the diagnostic");
		}

		[Test]
		public async Task PrivateNestedDecorator_Reports()
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

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Shop.LoggingClock'*").AsWildcard()
				.Because("a decorator link is resolved like any implementation, so its resolver returns the private decorator type");
		}

		[Test]
		public async Task TypeNestedInPrivateNestedType_Reports()
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

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Shop.Fakes' is private*").AsWildcard()
				.Because("an internal type nested in a private type is only as visible as the private type");
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

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Shop.Content' is private*").AsWildcard()
				.Because("a constructed generic type is only as visible as its least visible type argument");
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

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.Shop.Order' is private*").AsWildcard()
				.Because("a parameterized resolver takes its runtime arguments as parameters of an internal member");
		}

		[Test]
		public async Task PrivateNestedTypeOfImportedModule_Reports()
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

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.ClockModule.FakeClock'*").AsWildcard()
				.Because("a container in another class cannot name a type private to the module at all");
		}

		[Test]
		public async Task InternalContainerBesidePrivateType_Reports()
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

			await That(result.Diagnostics).Contains("*error AWT216*'MyCode.ShopTests.FakeClock'*").AsWildcard()
				.Because("an internal container exposes its resolvers to the whole assembly, beyond the reach of the private type");
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
	}
}
