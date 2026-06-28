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
			                                       public static partial class MyContainer
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
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT113"))).IsTrue()
				.Because("a parameterized service cannot be produced from a Func that supplies no runtime arguments");
			await That(result.Diagnostics.Single(d => d.Contains("AWT113"))).Contains("(none)")
				.Because("an empty runtime-argument list reads as 'none' rather than an empty '()'");
		}

		[Fact]
		public async Task PointsTheDiagnosticAtTheOffendingFuncParameterNotTheRegistration()
		{
			const string source = """
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
			                      public static partial class MyContainer
			                      {
			                      }
			                      """;
			GeneratorResult result = Generator.Run(source);

			string[] lines = source.Replace("\r\n", "\n").Split('\n');
			int funcLine = Array.FindIndex(lines, l => l.Contains("Func<int, Robot>")) + 1;
			string awt113 = result.Diagnostics.Single(d => d.Contains("AWT113"));

			await That(awt113).Contains($"({funcLine},")
				.Because("the diagnostic points at the Func parameter, not the [Singleton<Plant>] registration");
		}
	}
}
