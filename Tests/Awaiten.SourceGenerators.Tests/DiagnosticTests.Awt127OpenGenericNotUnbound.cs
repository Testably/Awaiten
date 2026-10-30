using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt127OpenGenericNotUnbound
	{
		[Test]
		public async Task ReportsWhenAnOpenImplementationNamesAClosedService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }

			                                       [Container]
			                                       [Transient(typeof(Repository<>), typeof(IRepository<int>))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT127*").AsWildcard()
				.Because("an open implementation can only be mapped onto an unbound service");
		}

		[Test]
		public async Task ReportsWhenAClosedImplementationNamesAnOpenService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }

			                                       [Container]
			                                       [Transient(typeof(Repository<int>), typeof(IRepository<>))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT127*").AsWildcard()
				.Because("an open service needs an unbound implementation to close per closing");
		}

		[Test]
		public async Task DoesNotReportForAClosedPair()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }
			                                       public sealed class Widget { }

			                                       [Container]
			                                       [Transient(typeof(Repository<int>), typeof(IRepository<int>))]
			                                       [Transient(typeof(Widget))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT127*").AsWildcard()
				.Because("a closed or non-generic typeof pair registers like the generic attribute form");
		}

		[Test]
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

			await That(result.Diagnostics).DoesNotContain("*AWT127*").AsWildcard()
				.Because("typeof(Repository<>) is the intended unbound open generic form");
		}
	}
}
