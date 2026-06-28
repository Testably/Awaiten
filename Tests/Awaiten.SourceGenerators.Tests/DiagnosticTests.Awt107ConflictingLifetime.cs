using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt107ConflictingLifetime
	{
		[Fact]
		public async Task DoesNotReportForTheSameLifetimeAcrossServices()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IReader { }
			                                       public interface IWriter { }
			                                       public sealed class Store : IReader, IWriter { }

			                                       [Container]
			                                       [Singleton<Store, IReader>]
			                                       [Singleton<Store, IWriter>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT107"))).IsFalse()
				.Because("registering one implementation under several services with the same lifetime is valid");
		}

		[Fact]
		public async Task ReportsAcrossDifferentServiceTypes()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IReader { }
			                                       public interface IWriter { }
			                                       public sealed class Store : IReader, IWriter { }

			                                       [Container]
			                                       [Singleton<Store, IReader>]
			                                       [Scoped<Store, IWriter>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT107"))).IsTrue();
		}

		[Fact]
		public async Task ReportsForTheSameServiceTypeWithDifferentLifetimes()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IReader { }
			                                       public sealed class Store : IReader { }

			                                       [Container]
			                                       [Singleton<Store, IReader>]
			                                       [Scoped<Store, IReader>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT107"))).IsTrue()
				.Because("re-registering the same service with a different lifetime is still a conflict, not a silent drop");
		}
	}
}
