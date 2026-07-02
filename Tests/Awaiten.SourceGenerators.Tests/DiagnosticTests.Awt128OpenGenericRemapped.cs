using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt128OpenGenericRemapped
	{
		[Fact]
		public async Task ReportsWhenTheImplementationReordersTheServiceTypeParameters()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<TA, TB> { }
			                                       // The implementation exposes the service with its type parameters swapped: constructing
			                                       // Repository<TKey, TValue> positionally from a closed IRepository<TKey, TValue> would yield
			                                       // a type that implements IRepository<TValue, TKey>, not the requested service.
			                                       public sealed class Repository<TKey, TValue> : IRepository<TValue, TKey> { }

			                                       [Container]
			                                       [Transient(typeof(Repository<,>), typeof(IRepository<,>))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT128"))).IsTrue()
				.Because("the implementation does not expose the service with its type parameters in declaration order");
		}

		[Fact]
		public async Task DoesNotReportWhenTheTypeParametersMapInOrder()
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT128"))).IsFalse()
				.Because("Repository<T> : IRepository<T> maps its single type parameter onto the service in order");
		}
	}
}
