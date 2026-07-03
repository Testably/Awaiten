using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt155CrossModuleDuplicate
	{
		[Fact]
		public async Task ReportsWhenTwoModulesStronglyRegisterTheSameServiceWithDifferentImplementations()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class ClockA : IClock { }
			                                       public sealed class ClockB : IClock { }

			                                       [Module]
			                                       [Singleton<ClockA, IClock>]
			                                       public static class ModuleA { }

			                                       [Module]
			                                       [Singleton<ClockB, IClock>]
			                                       public static class ModuleB { }

			                                       [Container]
			                                       [Import(typeof(ModuleA))]
			                                       [Import(typeof(ModuleB))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT155*IClock*ModuleA*ModuleB*").AsWildcard()
				.Because("which module wins is decided only by [Import] order, invisible at either module");
		}

		[Fact]
		public async Task DoesNotReportWhenTheContainerOverridesAModule()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class ModuleClock : IClock { }
			                                       public sealed class AppClock : IClock { }

			                                       [Module]
			                                       [Singleton<ModuleClock, IClock>]
			                                       public static class ClockModule { }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       [Singleton<AppClock, IClock>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT155"))).IsFalse()
				.Because("the container overriding a module is the intended override mechanism");
		}

		[Fact]
		public async Task DoesNotReportWhenBothModulesRegisterTheSameImplementation()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SharedClock : IClock { }

			                                       [Module]
			                                       [Singleton<SharedClock, IClock>]
			                                       public static class ModuleA { }

			                                       [Module]
			                                       [Singleton<SharedClock, IClock>]
			                                       public static class ModuleB { }

			                                       [Container]
			                                       [Import(typeof(ModuleA))]
			                                       [Import(typeof(ModuleB))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT155"))).IsFalse()
				.Because("the same implementation coalesces into one instance; there is no ambiguity");
		}

		[Fact]
		public async Task DoesNotReportWhenTheLaterModulesRegistrationIsAnOverridableDefault()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class ClockA : IClock { }
			                                       public sealed class ClockB : IClock { }

			                                       [Module]
			                                       [Singleton<ClockA, IClock>]
			                                       public static class ModuleA { }

			                                       [Module]
			                                       [Singleton<ClockB, IClock>(Default = true)]
			                                       public static class ModuleB { }

			                                       [Container]
			                                       [Import(typeof(ModuleA))]
			                                       [Import(typeof(ModuleB))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT155"))).IsFalse()
				.Because("an overridable default yields by design; being overridden is its purpose");
		}

		[Fact]
		public async Task KeyedCollisionsKeepReportingAwt117Instead()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class ClockA : IClock { }
			                                       public sealed class ClockB : IClock { }

			                                       [Module]
			                                       [Singleton<ClockA, IClock>(Key = "clock")]
			                                       public static class ModuleA { }

			                                       [Module]
			                                       [Singleton<ClockB, IClock>(Key = "clock")]
			                                       public static class ModuleB { }

			                                       [Container]
			                                       [Import(typeof(ModuleA))]
			                                       [Import(typeof(ModuleB))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT117*").AsWildcard()
				.Because("a keyed duplicate is already its own error");
			await That(result.Diagnostics.Any(d => d.Contains("AWT155"))).IsFalse()
				.Because("AWT155 is limited to unkeyed collisions to avoid double-reporting");
		}
	}
}
