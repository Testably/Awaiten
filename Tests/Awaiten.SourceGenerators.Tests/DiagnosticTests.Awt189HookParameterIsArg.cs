using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt189HookParameterIsArg
	{
		[Fact]
		public async Task ReportsWhenAHookParameterIsMarkedArg()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service, [Arg] int count) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT189"))).IsTrue()
				.Because("a runtime [Arg] cannot be supplied to a lifecycle hook - its parameters resolve from the graph");
		}

		[Fact]
		public async Task DoesNotReportForAGraphResolvedHookParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }
			                                       public sealed class Settings { }

			                                       [Container]
			                                       [Singleton<Settings>]
			                                       [Transient<Service>(OnRelease = nameof(Released))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Released(Service service, Settings settings) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT189"))).IsFalse()
				.Because("a hook parameter resolved from the graph is not a runtime argument");
		}
	}
}
