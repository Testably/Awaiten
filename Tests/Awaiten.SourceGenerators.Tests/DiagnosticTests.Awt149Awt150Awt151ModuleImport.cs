using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt149ImportNotAModule
	{
		[Test]
		public async Task ReportsWhenAnImportNamesATypeThatIsNotAModule()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       // Has registrations but is NOT marked [Module].
			                                       [Singleton<SystemClock, IClock>]
			                                       public sealed class NotAModule { }

			                                       [Container]
			                                       [Import(typeof(NotAModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT149*NotAModule*").AsWildcard()
				.Because("only [Module] types can be imported");
		}

		[Test]
		public async Task SkipsANonModuleImportEntirely_SoItsRegistrationsAreNotPulledIn()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Singleton<SystemClock, IClock>]
			                                       public sealed class NotAModule { }

			                                       [Container]
			                                       [Import(typeof(NotAModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

			await That(source).DoesNotContain("SystemClock")
				.Because("a non-module import is skipped, so its registrations are not imported");
		}

		[Test]
		public async Task DoesNotReportWhenTheImportNamesAProperModule()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Module]
			                                       [Singleton<SystemClock, IClock>]
			                                       public static class ClockModule { }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT149*").AsWildcard()
				.Because("a [Module] is a valid import target");
		}
	}

	public class Awt150NestedModuleImport
	{
		[Test]
		public async Task ReportsWhenAnImportedModuleHasItsOwnImport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }
			                                       public sealed class Logger { }

			                                       [Module]
			                                       [Singleton<Logger>]
			                                       public static class LoggingModule { }

			                                       [Module]
			                                       [Import(typeof(LoggingModule))]
			                                       [Singleton<SystemClock, IClock>]
			                                       public static class InfrastructureModule { }

			                                       [Container]
			                                       [Import(typeof(InfrastructureModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT150*InfrastructureModule*").AsWildcard()
				.Because("module imports are resolved one level deep, so a module's own [Import] is not followed");
		}

		[Test]
		public async Task DoesNotReportForAModuleWithoutItsOwnImport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Module]
			                                       [Singleton<SystemClock, IClock>]
			                                       public static class ClockModule { }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT150*").AsWildcard()
				.Because("a module with no [Import] of its own has nothing to follow");
		}
	}

	public class Awt151EmptyModule
	{
		[Test]
		public async Task ReportsWhenAnImportedModuleDeclaresNoRegistrations()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       [Module]
			                                       public static class EmptyModule { }

			                                       [Container]
			                                       [Import(typeof(EmptyModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT151*EmptyModule*").AsWildcard()
				.Because("an imported module that declares no registrations contributes nothing");
		}

		[Test]
		public async Task DoesNotReportForAModuleThatDeclaresRegistrations()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Module]
			                                       [Singleton<SystemClock, IClock>]
			                                       public static class ClockModule { }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT151*").AsWildcard()
				.Because("a module with registrations contributes something");
		}

		[Test]
		[Arguments("[ImportService<IClock>]")]
		[Arguments("[ImportService(typeof(IClock))]")]
		[Arguments("[ImportService(typeof(ILogger<>))]")]
		public async Task DoesNotReportForAModuleThatOnlyImportsServices(string importService)
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         public interface IClock { }
			                                         public interface ILogger<T> { }

			                                         [Module]
			                                         {{importService}}
			                                         public static class HostModule { }

			                                         [Container]
			                                         [Import(typeof(HostModule))]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).DoesNotContain("*AWT151*").AsWildcard()
				.Because("an imported service is a contribution: removing the module would turn the dependency into AWT101");
		}

		[Test]
		public async Task DoesNotReportForAReferencedModuleThatOnlyImportsServices()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IClock { }

				[Module]
				[ImportService<IClock>]
				public static class HostModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(HostModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).DoesNotContain("*AWT151*").AsWildcard()
				.Because("a module read from metadata carries its [ImportService] like one in the same compilation");
		}

		[Test]
		public async Task ReportsForAReferencedModuleThatDeclaresNothing()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				[Module]
				public static class EmptyModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(EmptyModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT151*EmptyModule*").AsWildcard()
				.Because("a module read from metadata that declares nothing still contributes nothing");
		}
	}
}
