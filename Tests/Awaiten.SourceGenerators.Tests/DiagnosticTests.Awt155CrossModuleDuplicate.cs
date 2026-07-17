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
		public async Task ReportsWhenTwoModulesOpenGenericTemplatesCollideOnTheSameClosedService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepo<T> { }
			                                       public sealed class RepoA<T> : IRepo<T> { }
			                                       public sealed class RepoB<T> : IRepo<T> { }
			                                       public sealed class Order { }
			                                       public sealed class Consumer
			                                       {
			                                           public Consumer(IRepo<Order> repo) { }
			                                       }

			                                       [Module]
			                                       [Singleton(typeof(RepoA<>), typeof(IRepo<>))]
			                                       public static class ModuleA { }

			                                       [Module]
			                                       [Singleton(typeof(RepoB<>), typeof(IRepo<>))]
			                                       public static class ModuleB { }

			                                       [Container]
			                                       [Import(typeof(ModuleA))]
			                                       [Import(typeof(ModuleB))]
			                                       [Singleton<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT155*IRepo*ModuleA*ModuleB*").AsWildcard()
				.Because("the closed registrations expanded from two modules' open templates collide exactly like two hand-written registrations - the winner is decided only by [Import] order");
		}

		[Fact]
		public async Task DoesNotReportWhenAnExplicitRegistrationBeatsAnotherModulesOpenGenericTemplate()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepo<T> { }
			                                       public sealed class Repo<T> : IRepo<T> { }
			                                       public sealed class Order { }
			                                       public sealed class SpecialRepo : IRepo<Order> { }
			                                       public sealed class Consumer
			                                       {
			                                           public Consumer(IRepo<Order> repo) { }
			                                       }

			                                       [Module]
			                                       [Singleton(typeof(Repo<>), typeof(IRepo<>))]
			                                       public static class TemplateModule { }

			                                       [Module]
			                                       [Singleton<SpecialRepo, IRepo<Order>>]
			                                       public static class SpecialModule { }

			                                       [Container]
			                                       [Import(typeof(TemplateModule))]
			                                       [Import(typeof(SpecialModule))]
			                                       [Singleton<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT155*").AsWildcard()
				.Because("an explicit registration beating another module's expanded template is deterministic regardless of [Import] order, so the collision is not ambiguous");
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

			await That(result.Diagnostics).DoesNotContain("*AWT155*").AsWildcard()
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

			await That(result.Diagnostics).DoesNotContain("*AWT155*").AsWildcard()
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
			                                       [Singleton<ClockB, IClock>(Fallback = Fallback.Warn)]
			                                       public static class ModuleB { }

			                                       [Container]
			                                       [Import(typeof(ModuleA))]
			                                       [Import(typeof(ModuleB))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT155*").AsWildcard()
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
			await That(result.Diagnostics).DoesNotContain("*AWT155*").AsWildcard()
				.Because("AWT155 is limited to unkeyed collisions to avoid double-reporting");
		}
	}
}
