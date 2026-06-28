using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt115ParameterizedRequiresFunc
	{
		[Fact]
		public async Task ReportsWhenAParameterizedServiceIsRequiredAsAPlainDependency()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Robot
			                                       {
			                                       	public Robot([Arg] string name) { }
			                                       }
			                                       public sealed class Plant { public Plant(Robot robot) { } }

			                                       [Container]
			                                       [Transient<Robot>]
			                                       [Singleton<Plant>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT115"))).IsTrue()
				.Because("a parameterized service cannot be supplied its runtime arguments through a plain dependency");
		}

		[Fact]
		public async Task ReportsWhenAParameterizedServiceIsRequiredThroughLazy()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Robot
			                                       {
			                                       	public Robot([Arg] string name) { }
			                                       }
			                                       public sealed class Plant { public Plant(Lazy<Robot> robot) { } }

			                                       [Container]
			                                       [Transient<Robot>]
			                                       [Singleton<Plant>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT115"))).IsTrue()
				.Because("Lazy<T> cannot carry runtime arguments either, so it cannot build a parameterized service");
		}

		[Fact]
		public async Task DoesNotReportWhenTheParameterizedServiceIsRequestedThroughAMatchingFunc()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Robot
			                                       {
			                                       	public Robot([Arg] string name) { }
			                                       }
			                                       public sealed class Plant { public Plant(Func<string, Robot> robots) { } }

			                                       [Container]
			                                       [Transient<Robot>]
			                                       [Singleton<Plant>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT115"))).IsFalse()
				.Because("a Func<TArg…, T> supplies the runtime arguments, so it is the correct way to obtain it");
		}
	}
}
