using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt176UnconsumedExternalService
	{
		[Fact]
		public async Task ReportsWhenADeclaredExternalTypeIsNeverConsumed()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ILogger { }
			                                       // Nothing in the graph depends on ILogger, so the declaration is dead.
			                                       public sealed class Service { }

			                                       [Container]
			                                       [ImportService<ILogger>]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT176*").AsWildcard()
				.And.Contains("*ILogger*").AsWildcard()
				.Because("a declared [ImportService<T>] no dependency consumes is dead");
		}

		[Fact]
		public async Task ReportsWhenTheExternalTypeIsReferencedOnlyThroughACollection_WhichIsInertNotRouted()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Collections.Generic;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IExternal { }
			                                       // IEnumerable<IExternal> is a collection over the external type: the resolver never yields
			                                       // collection elements, so the declaration routes nothing and yields an empty collection - inert.
			                                       public sealed class Service { public Service(IEnumerable<IExternal> items) { } }

			                                       [Container]
			                                       [ImportService<IExternal>]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT176*").AsWildcard()
				.And.Contains("*IExternal*").AsWildcard()
				.Because("a collection element reference is not routed and gives no signal, so the [ImportService<T>] declaration is genuinely dead");
		}

		[Fact]
		public async Task DoesNotReportWhenTheExternalTypeIsReferencedOnlyThroughARelationship_TheAwt101IsTheOneSignal()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IExternal { }
			                                       // Func<IExternal> is a relationship over the external type, which [ImportService<T>]
			                                       // does not route: it resolves from the graph and, unregistered, is AWT101.
			                                       public sealed class Service { public Service(Func<IExternal> factory) { } }

			                                       [Container]
			                                       [ImportService<IExternal>]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
				.And.Contains("*IExternal*").AsWildcard()
				.Because("a relationship over the external type is not routed, so the missing registration is AWT101");
			await That(result.Diagnostics.Any(d => d.Contains("AWT176"))).IsFalse()
				.Because("the external type is referenced (via the relationship), so it is not a dead declaration - the AWT101 is the one signal");
		}

		[Fact]
		public async Task DoesNotReportWhenTheExternalTypeIsConsumed()
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT176"))).IsFalse()
				.Because("a consumed [ImportService<T>] declaration is live");
		}

		[Fact]
		public async Task DoesNotReportWhenTheExternalTypeIsConsumedByALifecycleHookParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ILogger { }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [ImportService<ILogger>]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service, ILogger logger) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT176"))).IsFalse()
				.Because("an [ImportService<T>] consumed only by a lifecycle hook parameter is still live");
		}
	}
}
