namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt215ImportServiceTypeNotClosedOrOpen
	{
		[Test]
		public async Task ReportsATypeNestedInAnUnboundGeneric()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Outer<T> { public sealed class Inner { } }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [ImportService(typeof(Outer<>.Inner))]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT215*Outer<>.Inner*").AsWildcard()
				.Because("Outer<>.Inner is neither one closed service nor an unbound generic definition whose closings could be matched");
		}

		[Test]
		public async Task ReportsANullType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [ImportService(null!)]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT215*null*").AsWildcard()
				.Because("a null type names no service at all");
		}

		[Test]
		public async Task ReportsOnAnImportedModule()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Outer<T> { public sealed class Inner { } }
			                                       public sealed class Service { }

			                                       [Module]
			                                       [ImportService(typeof(Outer<>.Inner))]
			                                       [Singleton<Service>]
			                                       public static class MyModule;

			                                       [Container]
			                                       [Import(typeof(MyModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT215*").AsWildcard()
				.Because("a module's [ImportService] is read for the importing container, so its argument is validated too");
		}

		[Test]
		[Arguments("typeof(ILog<>)")]
		[Arguments("typeof(IClock)")]
		[Arguments("typeof(ILog<int>)")]
		[Arguments("typeof(Outer<>.Inner<>)")]
		public async Task DoesNotReportAClosedTypeOrAnUnboundGenericDefinition(string argument)
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         public interface IClock { }
			                                         public interface ILog<T> { }
			                                         public sealed class Outer<T> { public sealed class Inner<U> { } }
			                                         public sealed class Service
			                                         {
			                                             public Service(IClock clock, ILog<int> log, Outer<int>.Inner<string> inner) { }
			                                         }

			                                         [Container]
			                                         [ImportServices]
			                                         [ImportService({{argument}})]
			                                         [Singleton<Service>]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).DoesNotContain("*AWT215*").AsWildcard()
				.Because("a closed type and an unbound generic definition are the two forms [ImportService(typeof(...))] accepts");
			await That(result.Diagnostics).DoesNotContain("*AWT176*").AsWildcard()
				.Because("each declaration matches a consumed dependency, which proves it was read as intended");
		}
	}
}
