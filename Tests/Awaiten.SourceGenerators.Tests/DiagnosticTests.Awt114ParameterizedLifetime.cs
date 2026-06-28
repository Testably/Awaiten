using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt114ParameterizedLifetime
	{
		[Fact]
		public async Task ReportsWhenAParameterizedServiceIsRegisteredAsSingleton()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Robot
			                                       {
			                                       	public Robot([Arg] string name) { }
			                                       }

			                                       [Container]
			                                       [Singleton<Robot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT114"))).IsTrue()
				.Because("a parameterized service is built fresh per call, so a singleton lifetime cannot be honored");
		}

		[Fact]
		public async Task ReportsWhenAParameterizedServiceIsRegisteredAsScoped()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Robot
			                                       {
			                                       	public Robot([Arg] string name) { }
			                                       }

			                                       [Container]
			                                       [Scoped<Robot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT114"))).IsTrue()
				.Because("a scoped lifetime cannot be honored for a per-call parameterized service either");
		}

		[Fact]
		public async Task DoesNotReportForATransientParameterizedService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Robot
			                                       {
			                                       	public Robot([Arg] string name) { }
			                                       }

			                                       [Container]
			                                       [Transient<Robot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT114"))).IsFalse()
				.Because("transient is the only lifetime a parameterized service can honor");
		}
	}
}
