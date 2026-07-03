using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt143ScanAssembliesEmpty
	{
		[Fact]
		public async Task ReportsWhenInAssembliesOfResolvesToNoAssembly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), InAssembliesOf = new Type[0])]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT143*").AsWildcard()
				.Because("an empty InAssembliesOf is a malformed scan, not a mere empty result, and must fail the build");
			await That(result.Diagnostics.Any(d => d.Contains("AWT138"))).IsFalse()
				.Because("the empty assembly list is the one root cause; the scan matching nothing follows from it");

			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).DoesNotContain("AlphaPlugin")
				.Because("the scan registers nothing rather than silently scanning the container's own assembly");
		}

		[Fact]
		public async Task TreatsAnExplicitNullAsUnsetAndScansTheOwnAssembly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), InAssembliesOf = null)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("null is the property's default, meaning the container's own assembly is scanned");
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("new global::MyCode.AlphaPlugin()");
		}

		[Fact]
		public async Task DoesNotReportWhenInAssembliesOfIsUnset()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT143"))).IsFalse();
		}
	}
}
