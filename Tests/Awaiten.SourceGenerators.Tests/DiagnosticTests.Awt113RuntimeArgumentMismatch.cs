using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt113RuntimeArgumentMismatch
	{
		[Fact]
		public async Task ReportsWhenTheFuncArgumentTypesDoNotMatchTheArgParameters()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Robot
			                                       {
			                                       	public Robot([Arg] string name) { }
			                                       }
			                                       public sealed class Plant { public Plant(Func<int, Robot> robots) { } }

			                                       [Container]
			                                       [Transient<Robot>]
			                                       [Singleton<Plant>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT113"))).IsTrue()
				.Because("the Func requests an int but the [Arg] parameter expects a string");
		}

		[Fact]
		public async Task ReportsWhenAZeroArgumentFuncTargetsAParameterizedService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Robot
			                                       {
			                                       	public Robot([Arg] string name) { }
			                                       }
			                                       public sealed class Plant { public Plant(Func<Robot> robots) { } }

			                                       [Container]
			                                       [Transient<Robot>]
			                                       [Singleton<Plant>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT113"))).IsTrue()
				.Because("a parameterized service cannot be produced from a Func that supplies no runtime arguments");
		}
	}
}
