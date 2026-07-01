using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt127OpenGenericNotUnbound
	{
		[Fact]
		public async Task ReportsWhenTheTypeofArgumentIsAClosedGeneric()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }

			                                       [Container]
			                                       [Transient(typeof(Repository<int>), typeof(IRepository<int>))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT127"))).IsTrue()
				.Because("a closed generic in the typeof-ctor form would silently drop its type arguments; the generic attribute form registers closed types");
		}

		[Fact]
		public async Task ReportsWhenTheTypeofArgumentIsNonGeneric()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Widget { }

			                                       [Container]
			                                       [Transient(typeof(Widget))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT127"))).IsTrue()
				.Because("a non-generic type matches no closed service through the open generic path; the generic attribute form registers it");
		}

		[Fact]
		public async Task DoesNotReportForAnUnboundOpenGeneric()
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT127"))).IsFalse()
				.Because("typeof(Repository<>) is the intended unbound open generic form");
		}
	}
}
