namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt176UnconsumedExternalService
	{
		[Test]
		public async Task IsNotReportedForAClosedImportNothingInjects()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ILogger { }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [ImportService<ILogger>]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("AWT176 is retired: a direct Resolve<ILogger>() asks the host, so a declared import is live without an injecting consumer");
		}

		[Test]
		public async Task IsNotReportedForAClosedImportReferencedOnlyThroughACollection()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Collections.Generic;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IExternal { }
			                                       public sealed class Service { public Service(IEnumerable<IExternal> items) { } }

			                                       [Container]
			                                       [ImportService<IExternal>]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the collection is not routed, but a direct resolve of the import still is");
		}

		[Test]
		public async Task IsNotReportedForAnOpenImportNoClosingOfWhichIsInjected()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ILog<T> { }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [ImportService(typeof(ILog<>))]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a direct resolve of any closing of an open import asks the host, so no closing has to be injected");
		}

		[Test]
		public async Task IsNotReportedForAnOpenImportDeclaredOnAModule()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ILog<T> { }
			                                       public sealed class Service { }

			                                       [Module]
			                                       [ImportService(typeof(ILog<>))]
			                                       [Singleton<Service>]
			                                       public static class MyModule;

			                                       [Container]
			                                       [Import(typeof(MyModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a module's import is honored for the importing container's direct resolves too");
		}
	}
}
