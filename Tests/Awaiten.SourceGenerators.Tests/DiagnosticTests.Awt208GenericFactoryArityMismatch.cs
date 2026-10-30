namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt208GenericFactoryArityMismatch
	{
		[Test]
		public async Task ReportsANonGenericFactoryOnAnOpenGeneric()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }

			                                       [Container]
			                                       [Transient(typeof(IRepository<>), Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static IRepository<int> Create() => new Repository<int>();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT208*").AsWildcard()
				.Because("a non-generic factory can produce only one closing, not every closing of the open generic");
		}

		[Test]
		public async Task ReportsAFactoryWithMoreTypeParametersThanTheService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }

			                                       [Container]
			                                       [Transient(typeof(IRepository<>), Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static IRepository<T> Create<T, TExtra>() => new Repository<T>();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT208*").AsWildcard()
				.Because("only the service's type arguments are known per closing, so the extra parameter could not be bound");
		}

		[Test]
		public async Task DoesNotReportAFactoryWithTheServicesArity()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T> { }

			                                       [Container]
			                                       [Transient(typeof(IRepository<>), Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static IRepository<T> Create<T>() => new Repository<T>();
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT208*").AsWildcard()
				.Because("the factory's type parameter maps onto the service's in declaration order");
		}
	}
}
