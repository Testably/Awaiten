using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt175ContradictingExternalService
	{
		[Test]
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

		[Test]
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

		[Test]
		public async Task ReportsWhenARoutedClosingOfAnOpenImportIsAlsoRegistered()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ILog<T> { }
			                                       public sealed class ServiceLog : ILog<Service> { }
			                                       public sealed class Service { public Service(ILog<Service> log) { } }

			                                       [Container]
			                                       [ImportService(typeof(ILog<>))]
			                                       [Singleton<ServiceLog, ILog<Service>>]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT175*ILog<MyCode.Service>*").AsWildcard()
				.Because("the closing is routed to the host by the open import while the container also registers it");
		}

		[Test]
		public async Task ReportsWhenARoutedClosingOfAnOpenImportIsAlsoRegisteredAsAnOpenGeneric()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ILog<T> { }
			                                       public sealed class Log<T> : ILog<T> { }
			                                       public sealed class Service { public Service(ILog<Service> log) { } }

			                                       [Container]
			                                       [ImportService(typeof(ILog<>))]
			                                       [Singleton(typeof(Log<>), typeof(ILog<>))]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT175*ILog<MyCode.Service>*").AsWildcard()
				.Because("the open registration closes ILog<Service> too, so the routed closing is Awaiten-owned as well");
		}

		[Test]
		public async Task DoesNotReportARegisteredClosingOfAnOpenImportThatIsNotRouted()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ILog<T> { }
			                                       public sealed class ServiceLog : ILog<Service> { }
			                                       public sealed class Service { public Service(Lazy<ILog<Service>> log, ILog<int> other) { } }

			                                       [Container]
			                                       [ImportService(typeof(ILog<>))]
			                                       [Singleton<ServiceLog, ILog<Service>>]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT175*").AsWildcard()
				.Because("a Lazy<T> over the closing resolves it from the graph, so nothing routes the registered closing to the host");
			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the registered closing satisfies the relationship and ILog<int> is routed");
		}
	}
}
