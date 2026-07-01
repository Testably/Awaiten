using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt126OpenGenericConstraint
	{
		[Fact]
		public async Task ReportsWhenAClosedTypeArgumentViolatesAReferenceTypeConstraint()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> where T : class { }
			                                       // where T : class on the implementation: a value-type argument violates it.
			                                       public sealed class Repository<T> : IRepository<T> where T : class { }
			                                       public sealed class Root { public Root(IRepository<int> numbers) { } }

			                                       [Container]
			                                       [Transient(typeof(Repository<>), typeof(IRepository<>))]
			                                       [Transient<Root>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT126"))).IsTrue()
				.Because("Repository<int> violates the implementation's where T : class constraint");
		}

		[Fact]
		public async Task DoesNotReportWhenTheClosedTypeArgumentSatisfiesTheConstraint()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Order { }
			                                       public interface IRepository<T> where T : class { }
			                                       public sealed class Repository<T> : IRepository<T> where T : class { }
			                                       public sealed class Root { public Root(IRepository<Order> orders) { } }

			                                       [Container]
			                                       [Transient(typeof(Repository<>), typeof(IRepository<>))]
			                                       [Transient<Root>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT126"))).IsFalse()
				.Because("Order is a reference type, so it satisfies where T : class");
		}

		[Fact]
		public async Task DoesNotReportForASelfReferentialConstraintTheArgumentSatisfies()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // Order satisfies where T : IComparable<T>, but the constraint mentions the type parameter,
			                                       // so it cannot be checked without substitution - it must not be treated as a violation.
			                                       public sealed class Order : IComparable<Order> { public int CompareTo(Order other) => 0; }
			                                       public interface IRepository<T> where T : IComparable<T> { }
			                                       public sealed class Repository<T> : IRepository<T> where T : IComparable<T> { }
			                                       public sealed class Root { public Root(IRepository<Order> orders) { } }

			                                       [Container]
			                                       [Transient(typeof(Repository<>), typeof(IRepository<>))]
			                                       [Transient<Root>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT126"))).IsFalse()
				.Because("a constructed constraint mentioning the type parameter (IComparable<T>) is satisfied after substitution");
		}

		[Fact]
		public async Task ReportsWhenAnImplementationOnlySelfReferentialConstraintIsViolated()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // The service has no constraint, so IRepository<NotComparable> is legal in Root's signature;
			                                       // the implementation constrains T : IComparable<T>, which NotComparable does not satisfy.
			                                       // Without substituting the closed argument this would emit uncompilable code (CS0311); instead
			                                       // it must be reported cleanly as AWT126.
			                                       public sealed class NotComparable { }
			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> where T : IComparable<T> { }
			                                       public sealed class Root { public Root(IRepository<NotComparable> repo) { } }

			                                       [Container]
			                                       [Transient(typeof(Repository<>), typeof(IRepository<>))]
			                                       [Transient<Root>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT126"))).IsTrue()
				.Because("the implementation's self-referential constraint where T : IComparable<T> is violated by NotComparable after substitution");
		}
	}
}
