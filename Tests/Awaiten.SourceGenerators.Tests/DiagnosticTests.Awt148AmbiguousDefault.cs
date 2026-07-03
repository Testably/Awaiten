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
			                                       public sealed class ModuleA { }

			                                       [Module]
			                                       [Singleton<ClockB, IClock>(Default = true)]
			                                       public sealed class ModuleB { }

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
			                                       public sealed class ModuleA { }

			                                       [Module]
			                                       [Singleton<ClockB, IClock>(Default = true)]
			                                       public sealed class ModuleB { }

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
			                                       public sealed class ModuleA { }

			                                       [Module]
			                                       [Singleton<ClockB, IClock>(TryAdd = true)]
			                                       public sealed class ModuleB { }

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
	}
}
