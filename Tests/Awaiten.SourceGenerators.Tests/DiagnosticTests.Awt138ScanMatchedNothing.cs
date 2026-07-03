using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt138ScanMatchedNothing
	{
		[Fact]
		public async Task ReportsWhenAScanMatchesNoConcreteType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // No concrete class implements IPlugin, so the scan contributes nothing.
			                                       public interface IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT138*").AsWildcard()
				.And.Contains("*IPlugin*").AsWildcard()
				.Because("a scan whose marker matches no concrete type is a typo or an empty marker");
		}

		[Fact]
		public async Task DoesNotReportWhenAScanMatchesAtLeastOneConcreteType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT138"))).IsFalse()
				.Because("the scan matched a concrete type, so it contributes a registration");
		}

		[Fact]
		public async Task DoesNotReportWhenTheOnlyMatchIsOverriddenByAnExplicitRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin))]
			                                       [Transient<AlphaPlugin>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT138"))).IsFalse()
				.Because("an overridden match still means the scan found something");
		}
	}
}
