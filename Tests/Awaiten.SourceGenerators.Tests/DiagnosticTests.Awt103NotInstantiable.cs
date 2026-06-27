using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt103NotInstantiable
	{
		[Theory]
		[InlineData("public interface Foo { }")]
		[InlineData("public abstract class Foo { }")]
		public async Task ReportsForAnInterfaceOrAbstractImplementation(string implementationDeclaration)
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         {{implementationDeclaration}}

			                                         [Container]
			                                         [Singleton<Foo>]
			                                         public partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT103"))).IsTrue();
		}
	}
}
