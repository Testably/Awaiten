using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt148AmbiguousDefault
	{
		[Fact]
		public async Task ReportsWhenTwoDefaultsProvideTheSameServiceWithNoStrongOverride()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class ClockA : IClock { }
			                                       public sealed class ClockB : IClock { }

			                                       [Module]
			                                       [Singleton<ClockA, IClock>(Default = true)]
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

			await That(result.Diagnostics).Contains("*AWT148*IClock*").AsWildcard()
				.Because("two overridable defaults provide IClock and neither is overridden by a strong registration");
		}

		[Fact]
		public async Task DoesNotReportWhenAStrongRegistrationOverridesTheDefaults()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class ClockA : IClock { }
			                                       public sealed class ClockB : IClock { }
			                                       public sealed class AppClock : IClock { }

			                                       [Module]
			                                       [Singleton<ClockA, IClock>(Default = true)]
			                                       public static class ModuleA { }

			                                       [Module]
			                                       [Singleton<ClockB, IClock>(Default = true)]
			                                       public static class ModuleB { }

			                                       [Container]
			                                       [Import(typeof(ModuleA))]
			                                       [Import(typeof(ModuleB))]
			                                       [Singleton<AppClock, IClock>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT148"))).IsFalse()
				.Because("a strong registration overrides both defaults, so which default would have won is moot");
		}

		[Fact]
		public async Task DoesNotReportForTryAddCollisions()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class ClockA : IClock { }
			                                       public sealed class ClockB : IClock { }

			                                       [Module]
			                                       [Singleton<ClockA, IClock>(TryAdd = true)]
			                                       public static class ModuleA { }

			                                       [Module]
			                                       [Singleton<ClockB, IClock>(TryAdd = true)]
			                                       public static class ModuleB { }

			                                       [Container]
			                                       [Import(typeof(ModuleA))]
			                                       [Import(typeof(ModuleB))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT148"))).IsFalse()
				.Because("TryAdd opts out of the ambiguity warning: the first contributor simply wins");
		}

		[Fact]
		public async Task DoesNotReportWhenADefaultLosesToAnEarlierTryAdd()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class ClockA : IClock { }
			                                       public sealed class ClockB : IClock { }

			                                       [Module]
			                                       [Singleton<ClockA, IClock>(TryAdd = true)]
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT148"))).IsFalse()
				.Because("the earlier TryAdd wins silently; AWT148 requires the current winner to be a Default");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("global::MyCode.ClockA")
				.Because("the earlier TryAdd claims the service");
			await That(source).DoesNotContain("ClockB")
				.Because("the losing default is dropped in full");
		}

		[Fact]
		public async Task ReportsOncePerLosingDefault_ForThreeCollidingDefaults()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class ClockA : IClock { }
			                                       public sealed class ClockB : IClock { }
			                                       public sealed class ClockC : IClock { }

			                                       [Module]
			                                       [Singleton<ClockA, IClock>(Default = true)]
			                                       public static class ModuleA { }

			                                       [Module]
			                                       [Singleton<ClockB, IClock>(Default = true)]
			                                       public static class ModuleB { }

			                                       [Module]
			                                       [Singleton<ClockC, IClock>(Default = true)]
			                                       public static class ModuleC { }

			                                       [Container]
			                                       [Import(typeof(ModuleA))]
			                                       [Import(typeof(ModuleB))]
			                                       [Import(typeof(ModuleC))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Count(d => d.Contains("AWT148"))).IsEqualTo(2)
				.Because("each losing default is reported once; the first-declared default wins");
		}

		[Fact]
		public async Task Keyed_ReportsOnlyWhenTheDefaultsShareTheKey()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class ClockA : IClock { }
			                                       public sealed class ClockB : IClock { }

			                                       [Module]
			                                       [Singleton<ClockA, IClock>(Default = true, Key = "a")]
			                                       public static class ModuleA { }

			                                       [Module]
			                                       [Singleton<ClockB, IClock>(Default = true, Key = "b")]
			                                       public static class ModuleB { }

			                                       [Container]
			                                       [Import(typeof(ModuleA))]
			                                       [Import(typeof(ModuleB))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT148"))).IsFalse()
				.Because("defaults under different keys claim different service slots and do not collide");
		}
	}
}
