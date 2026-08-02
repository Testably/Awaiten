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
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).Contains("*AWT103*").AsWildcard();
		}

		[Fact]
		public async Task PointsAtTheFactoryExitRatherThanOnlyDemandingAConcreteType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IBrewer { }

			                                       [Container]
			                                       [Singleton<IBrewer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Single(d => d.Contains("AWT103"))).Contains("Factory or Instance member")
				.Because("demanding a concrete type is wrong advice for a library factory whose implementation is internal, where the sanctioned answer is producing the instance through a member");
		}

		[Fact]
		public async Task DoesNotReportAnInterfaceWhoseInstanceComesFromAFactory()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IBrewer { }

			                                       public sealed class Brewer : IBrewer { }

			                                       [Container]
			                                       [Singleton<IBrewer>(Factory = nameof(MyContainer.CreateBrewer))]
			                                       public static partial class MyContainer
			                                       {
			                                       	public static IBrewer CreateBrewer() => new Brewer();
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT103"))).IsFalse()
				.Because("a factory produces the instance, so the type argument only names the service and instantiability is not required");
		}

		[Fact]
		public async Task DoesNotReportAnInterfaceWhoseInstanceComesFromAMember()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IBrewer { }

			                                       public sealed class Brewer : IBrewer { }

			                                       [Container]
			                                       [Singleton<IBrewer>(Instance = nameof(MyContainer.Brewer))]
			                                       public static partial class MyContainer
			                                       {
			                                       	public static IBrewer Brewer { get; } = new Brewer();
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT103"))).IsFalse()
				.Because("a pre-built member is exposed, not constructed, so the type argument only names the service and instantiability is not required");
		}
	}
}
