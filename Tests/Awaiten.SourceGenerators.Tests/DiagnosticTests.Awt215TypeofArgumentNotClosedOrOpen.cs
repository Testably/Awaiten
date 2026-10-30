namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt215TypeofArgumentNotClosedOrOpen
	{
		[Test]
		[Arguments("ImportService(typeof(Outer<>.Leaf))", "ImportService")]
		[Arguments("InjectProperty(typeof(Outer<>.Leaf), \"Clock\")", "InjectProperty")]
		public async Task ReportsATypeNestedInAnUnboundGeneric(string attribute, string attributeName)
		{
			GeneratorResult result = Run(attribute);

			await That(result.Diagnostics).Contains($"*error AWT215*Outer<>.Leaf*[{attributeName}]*").AsWildcard()
				.Because("Outer<>.Leaf is neither one closed type nor an unbound generic definition whose closings could be matched");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard()
				.Because("C# accepts a type nested in an unbound generic, so AWT215 is the only error");
			await That(result.Diagnostics).DoesNotContain("*AWT1*").AsWildcard()
				.Because("the declaration is ignored, so no follow-up diagnostic such as AWT176 or AWT180 is reported");
		}

		[Test]
		[Arguments("ImportService(typeof(Outer<int>.Inner<>))", "ImportService")]
		[Arguments("ImportService(typeof(Outer<>.Inner<int>))", "ImportService")]
		[Arguments("ImportService(service: typeof(Outer<int>.Inner<>))", "ImportService")]
		[Arguments("InjectProperty(typeof(Outer<int>.Inner<>), \"Clock\")", "InjectProperty")]
		[Arguments("InjectProperty(typeof(Outer<>.Inner<int>), \"Clock\")", "InjectProperty")]
		[Arguments("InjectProperty(propertyName: \"Clock\", implementation: typeof(Outer<int>.Inner<>))", "InjectProperty")]
		public async Task ReportsAPartiallyOpenType(string attribute, string attributeName)
		{
			GeneratorResult result = Run(attribute);

			await That(result.Diagnostics).Contains($"*error AWT215*[{attributeName}]*").AsWildcard()
				.Because("a partially open type is neither a closed type nor an unbound generic definition");
			await That(result.Diagnostics).Contains("*error CS7003*").AsWildcard()
				.Because("C# rejects a partially open type as well, and the generator still runs over it");
			await That(result.Diagnostics).DoesNotContain("*AWT1*").AsWildcard()
				.Because("the declaration is ignored, so no follow-up diagnostic such as AWT176 or AWT180 is reported");
		}

		[Test]
		[Arguments("ImportService(null!)", "ImportService")]
		[Arguments("InjectProperty(null!, \"Clock\")", "InjectProperty")]
		public async Task ReportsANullType(string attribute, string attributeName)
		{
			GeneratorResult result = Run(attribute);

			await That(result.Diagnostics).Contains($"*error AWT215*'null'*[{attributeName}]*").AsWildcard()
				.Because("a null type names no type at all");
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
		public async Task DoesNotReportAClosedTypeOrAnUnboundGenericDefinitionOnImportService(string argument)
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

		[Test]
		[Arguments("typeof(Outer<>.Inner<>), \"Clock\"")]
		[Arguments("typeof(Outer<int>.Inner<string>), \"Clock\"")]
		[Arguments("typeof(Outer<int>.Leaf), \"Clock\"")]
		[Arguments("propertyName: \"Clock\", implementation: typeof(Outer<>.Inner<>)")]
		public async Task DoesNotReportAClosedTypeOrAnUnboundGenericDefinitionOnInjectProperty(string arguments)
		{
			GeneratorResult result = Run($"InjectProperty({arguments})");

			await That(result.Diagnostics).DoesNotContain("*AWT*").AsWildcard()
				.Because("a closed type and an unbound generic definition are the two forms [InjectProperty(typeof(...), name)] accepts, and each matches a registered closing");
		}

		private static GeneratorResult Run(string attribute)
			=> Generator.Run($$"""
			                   #nullable enable
			                   using Awaiten;

			                   namespace MyCode;

			                   public sealed class Clock { }
			                   public sealed class Outer<T>
			                   {
			                       public sealed class Inner<U>
			                       {
			                           public Clock? Clock { get; set; }
			                       }

			                       public sealed class Leaf
			                       {
			                           public Clock? Clock { get; set; }
			                       }
			                   }

			                   [Container]
			                   [Singleton<Clock>]
			                   [Singleton<Outer<int>.Inner<string>>]
			                   [Singleton<Outer<int>.Leaf>]
			                   [{{attribute}}]
			                   public static partial class MyContainer
			                   {
			                   }
			                   """);
	}
}
