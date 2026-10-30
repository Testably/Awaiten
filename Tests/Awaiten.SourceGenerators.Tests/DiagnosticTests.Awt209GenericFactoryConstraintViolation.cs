namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt209GenericFactoryConstraintViolation
	{
		[Test]
		public async Task ReportsAClosingThatViolatesTheFactorysConstraints()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }
			                                       public sealed class Root { public Root(IRepository<int> numbers) { } }

			                                       [Container]
			                                       [Transient(typeof(IRepository<>), Factory = nameof(Create))]
			                                       [Transient<Root>]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static IRepository<T> Create<T>() where T : class => new Repository<T>();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT209*").AsWildcard()
				.Because("Create<int> violates the class constraint, so the closing cannot be produced");
			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the skipped closing is the root cause, so the consumer's missing dependency is not reported twice");
		}

		[Test]
		public async Task DoesNotReportAClosingThatSatisfiesTheConstraints()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Order { }
			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }
			                                       public sealed class Root { public Root(IRepository<Order> orders) { } }

			                                       [Container]
			                                       [Transient(typeof(IRepository<>), Factory = nameof(Create))]
			                                       [Transient<Root>]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static IRepository<T> Create<T>() where T : class => new Repository<T>();
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("Order satisfies the class constraint");
		}
	}
}
