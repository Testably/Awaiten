namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt207OpenGenericNeedsClosedType
	{
		[Test]
		public async Task ReportsEagerOnAnOpenGeneric()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ICache<T> { }
			                                       public sealed class Cache<T> : ICache<T> { }

			                                       [Container]
			                                       [Singleton(typeof(Cache<>), typeof(ICache<>), Eager = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT207*Eager*").AsWildcard()
				.Because("the closings of an open generic are whatever the graph asks for, so none can be built eagerly by declaration");
		}

		[Test]
		public async Task ReportsInstanceOnAnOpenGeneric()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ICache<T> { }
			                                       public sealed class Cache<T> : ICache<T> { }

			                                       [Container]
			                                       [Singleton(typeof(Cache<>), typeof(ICache<>), Instance = nameof(Shared))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static readonly object Shared = new();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT207*Instance*").AsWildcard()
				.Because("one pre-built instance cannot be every closing of an open generic");
		}

		[Test]
		public async Task DoesNotReportEagerOrInstanceOnAClosedType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ICache<T> { }
			                                       public sealed class Cache<T> : ICache<T> { }

			                                       [Container]
			                                       [Singleton(typeof(Cache<int>), typeof(ICache<int>), Eager = true)]
			                                       [Singleton(typeof(Cache<string>), typeof(ICache<string>), Instance = nameof(Shared))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static readonly Cache<string> Shared = new();
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT207*").AsWildcard()
				.Because("a closed typeof registration names one concrete instance, so both options apply");
		}
	}
}
