using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt149ImportNotAModule
	{
		[Fact]
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

		[Fact]
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

		[Fact]
		public async Task ReportsForTheGenericImportFormToo()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Singleton<SystemClock, IClock>]
			                                       public sealed class NotAModule { }

			                                       [Container]
			                                       [Import<NotAModule>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT149*NotAModule*").AsWildcard()
				.Because("the [Module] requirement applies to [Import<T>] as well as [Import(typeof(T))]");
		}

		[Fact]
		public async Task DoesNotReportWhenTheImportNamesAProperModule()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Module]
			                                       [Singleton<SystemClock, IClock>]
			                                       public sealed class ClockModule { }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT149"))).IsFalse()
				.Because("a [Module] is a valid import target");
		}
	}

	public class Awt150NestedModuleImport
	{
		[Fact]
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
			                                       public sealed class LoggingModule { }

			                                       [Module]
			                                       [Import(typeof(LoggingModule))]
			                                       [Singleton<SystemClock, IClock>]
			                                       public sealed class InfrastructureModule { }

			                                       [Container]
			                                       [Import(typeof(InfrastructureModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT150*InfrastructureModule*").AsWildcard()
				.Because("module imports are resolved one level deep, so a module's own [Import] is not followed");
		}

		[Fact]
		public async Task DoesNotReportForAModuleWithoutItsOwnImport()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Module]
			                                       [Singleton<SystemClock, IClock>]
			                                       public sealed class ClockModule { }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT150"))).IsFalse()
				.Because("a module with no [Import] of its own has nothing to follow");
		}
	}

	public class Awt151EmptyModule
	{
		[Fact]
		public async Task ReportsWhenAnImportedModuleDeclaresNoRegistrations()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       [Module]
			                                       public sealed class EmptyModule { }

			                                       [Container]
			                                       [Import(typeof(EmptyModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT151*EmptyModule*").AsWildcard()
				.Because("an imported module that declares no registrations contributes nothing");
		}

		[Fact]
		public async Task DoesNotReportForAModuleThatDeclaresRegistrations()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Module]
			                                       [Singleton<SystemClock, IClock>]
			                                       public sealed class ClockModule { }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT151"))).IsFalse()
				.Because("a module with registrations contributes something");
		}
	}
}
