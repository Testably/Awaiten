using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt129OpenGenericRecursion
	{
		[Fact]
		public async Task ReportsAndTerminatesWhenAnOpenRegistrationRecursesWithoutBound()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public sealed class Seed { }
			                                       // Node<T> depends on Node<List<T>>: expanding Node<Seed> needs Node<List<Seed>>, then
			                                       // Node<List<List<Seed>>>, ... an unbounded chain of ever-larger closed generics. Expansion
			                                       // must be bounded and reported rather than looping until it exhausts memory.
			                                       public sealed class Node<T> { public Node(Node<List<T>> next) { } }
			                                       public sealed class Root { public Root(Node<Seed> node) { } }

			                                       [Container]
			                                       [Transient(typeof(Node<>))]
			                                       [Transient<Root>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT129"))).IsTrue()
				.Because("the open registration recurses into an ever-larger closed generic, so expansion is bounded and reported");
		}

		[Fact]
		public async Task DoesNotReportForADeepButFiniteOpenGenericChain()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Order { }
			                                       // A finite transitive chain (Handler<T> -> IValidator<T>) closes at a fixed depth and must
			                                       // not be mistaken for unbounded recursion.
			                                       public interface IValidator<T> { }
			                                       public sealed class Validator<T> : IValidator<T> { }
			                                       public sealed class Handler<T> { public Handler(IValidator<T> validator) { } }
			                                       public sealed class Root { public Root(Handler<Order> handler) { } }

			                                       [Container]
			                                       [Transient(typeof(Validator<>), typeof(IValidator<>))]
			                                       [Transient(typeof(Handler<>))]
			                                       [Transient<Root>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT129"))).IsFalse()
				.Because("a finite closed-generic chain converges, so no depth limit is reached");
		}
	}
}
