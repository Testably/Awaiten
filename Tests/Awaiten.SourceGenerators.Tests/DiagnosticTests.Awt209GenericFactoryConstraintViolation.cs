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

		[Test]
		public async Task ReportsEachFactoryOfTheSameNameOnItsOwnDeclaringType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Collections.Generic;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }
			                                       public sealed class Root { public Root(IEnumerable<IRepository<int>> numbers) { } }

			                                       [Module]
			                                       [Transient(typeof(IRepository<>), Factory = nameof(Create))]
			                                       public static class RepositoryModule
			                                       {
			                                       	public static IRepository<T> Create<T>() where T : class => new Repository<T>();
			                                       }

			                                       [Container]
			                                       [Import(typeof(RepositoryModule))]
			                                       [Transient(typeof(IRepository<>), Factory = nameof(Create))]
			                                       [Transient<Root>]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static IRepository<T> Create<T>() where T : class => new Repository<T>();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT209*'MyCode.MyContainer.Create'*").AsWildcard()
				.Because("the container's Create<int> violates its class constraint");
			await That(result.Diagnostics).Contains("*AWT209*'MyCode.RepositoryModule.Create'*").AsWildcard()
				.Because("the module's Create is a different factory of the same name, so its violation is reported too");
		}

		[Test]
		public async Task ReportsOnlyTheFactoryWhoseConstraintsTheClosingViolates()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }
			                                       public sealed class Root { public Root(IRepository<int>[] numbers) { } }

			                                       [Container]
			                                       [Transient(typeof(IRepository<>), Factory = nameof(CreateA))]
			                                       [Transient(typeof(IRepository<>), Factory = nameof(CreateB))]
			                                       [Transient<Root>]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static IRepository<T> CreateA<T>() where T : class => new Repository<T>();
			                                       	private static IRepository<T> CreateB<T>() => new Repository<T>();
			                                       }
			                                       """);

			await That(result.Diagnostics).HasSingle().Matching(diagnostic => diagnostic.Contains("AWT209") && diagnostic.Contains("CreateA"))
				.Because("each open factory is checked against the closing on its own, so only CreateA<int> is rejected");
		}
	}
}
