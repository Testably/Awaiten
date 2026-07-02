using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt125OpenGenericArity
	{
		[Fact]
		public async Task ReportsWhenTheImplementationAndServiceHaveDifferentArity()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> { }
			                                       // Two type parameters declared for a one-parameter service: no closed service can be mapped.
			                                       public sealed class Repository<TKey, TValue> { }

			                                       [Container]
			                                       [Transient(typeof(Repository<,>), typeof(IRepository<>))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT125"))).IsTrue()
				.Because("the open implementation's arity must match the service's, so a closed service can be re-mapped onto the implementation");
		}

		[Fact]
		public async Task DoesNotReportWhenTheArityMatches()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Order { }
			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }
			                                       public sealed class Root { public Root(IRepository<Order> orders) { } }

			                                       [Container]
			                                       [Transient(typeof(Repository<>), typeof(IRepository<>))]
			                                       [Transient<Root>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT125"))).IsFalse()
				.Because("the implementation and service both declare one type parameter");
		}
	}
}
