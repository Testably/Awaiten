using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt175ContradictingExternalService
	{
		[Fact]
		public async Task ReportsWhenADeclaredExternalTypeIsAlsoRegistered()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ILogger { }
			                                       public sealed class ConsoleLogger : ILogger { }
			                                       public sealed class Service { public Service(ILogger logger) { } }

			                                       [Container]
			                                       [ImportService<ILogger>]
			                                       [Singleton<ConsoleLogger, ILogger>]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT175*").AsWildcard()
				.And.Contains("*ILogger*").AsWildcard()
				.Because("a type is either host-owned ([ImportService<T>]) or Awaiten-owned (registered), not both");
		}

		[Fact]
		public async Task DoesNotReportWhenTheExternalTypeIsNotRegistered()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ILogger { }
			                                       public sealed class Service { public Service(ILogger logger) { } }

			                                       [Container]
			                                       [ImportService<ILogger>]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT175*").AsWildcard()
				.Because("an [ImportService<T>] type with no registration is the ordinary, contradiction-free case");
		}
	}
}
