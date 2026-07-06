using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt172ScanFiltersMatchedNothing
	{
		[Fact]
		public async Task ReportsWhenFiltersRemoveEveryMatch()
		{
			GeneratorResult result = Generator.Run("""
				using Awaiten;

				namespace MyCode;

				public interface IPlugin { }
				public sealed class OrderPlugin : IPlugin { }

				[Container]
				[Scan(typeof(IPlugin), NamePatterns = new[] { "*Handler" })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT172*").AsWildcard()
				.Because("the marker matched OrderPlugin but the name filter removed it");
			await That(result.Diagnostics.Any(d => d.Contains("AWT138"))).IsFalse()
				.Because("AWT138 is for a marker that matched nothing, distinct from a filter that removed everything");
		}

		[Fact]
		public async Task DoesNotReportWhenAFilteredScanStillRegistersSomething()
		{
			GeneratorResult result = Generator.Run("""
				using Awaiten;

				namespace MyCode;

				public interface IPlugin { }
				public sealed class OrderHandler : IPlugin { }
				public sealed class OrderService : IPlugin { }

				[Container]
				[Scan(typeof(IPlugin), NamePatterns = new[] { "*Handler" })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics.Any(d => d.Contains("AWT172"))).IsFalse()
				.Because("OrderHandler still matches, so the filter did not remove everything");
		}
	}
}
