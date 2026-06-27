using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt108AmbiguousService
	{
		[Fact]
		public async Task ReportsForTheSameServiceRegisteredToDifferentImplementations()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IReader { }
			                                       public sealed class FileReader : IReader { }
			                                       public sealed class MemoryReader : IReader { }

			                                       [Container]
			                                       [Singleton<FileReader, IReader>]
			                                       [Singleton<MemoryReader, IReader>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT108"))).IsTrue()
				.Because("the second registration of IReader would be silently dropped, not resolved");
		}

		[Fact]
		public async Task DoesNotReportForTheSameImplementationRegisteredTwice()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IReader { }
			                                       public sealed class Store : IReader { }

			                                       [Container]
			                                       [Singleton<Store, IReader>]
			                                       [Singleton<Store, IReader>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT108"))).IsFalse()
				.Because("re-registering the same implementation under the same service is idempotent, not a drop");
		}

		[Fact]
		public async Task DoesNotReportForOneImplementationAcrossDifferentServices()
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
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT108"))).IsFalse()
				.Because("one implementation serving several service types maps each distinctly, nothing is dropped");
		}
	}
}
