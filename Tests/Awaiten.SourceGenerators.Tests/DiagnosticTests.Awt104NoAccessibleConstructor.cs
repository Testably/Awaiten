using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt104NoAccessibleConstructor
	{
		[Fact]
		public async Task ReportsWhenTheOnlyConstructorIsPrivate()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Foo { private Foo() { } }

			                                       [Container]
			                                       [Singleton<Foo>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT104"))).IsTrue();
		}
	}
}
