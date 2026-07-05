using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt139ScanNoImplementedInterfaces
	{
		[Fact]
		public async Task ReportsWhenAnInterfacesOnlyScanMatchesATypeWithNoAssignableInterface()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // A base-class marker: matches have no interface assignable to it, so an interfaces-only
			                                       // scan registers nothing for them.
			                                       public abstract class HandlerBase { }
			                                       public sealed class RealHandler : HandlerBase { }

			                                       [Container]
			                                       [Scan(typeof(HandlerBase), As = ScanAs.Marker)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT139*").AsWildcard()
				.And.Contains("*RealHandler*").AsWildcard()
				.Because("an interfaces-only scan matched a type with no interface assignable to the marker");
		}

		[Fact]
		public async Task DoesNotReportForSelfAndMarker()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public abstract class HandlerBase { }
			                                       public sealed class RealHandler : HandlerBase { }

			                                       [Container]
			                                       [Scan(typeof(HandlerBase), As = ScanAs.SelfAndMarker)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT139"))).IsFalse()
				.Because("SelfAndMarker still registers the match as its own concrete type");
		}

		[Fact]
		public async Task DoesNotReportWhenTheMatchImplementsAnAssignableInterface()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IHandler { }
			                                       public sealed class RealHandler : IHandler { }

			                                       [Container]
			                                       [Scan(typeof(IHandler), As = ScanAs.Marker)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT139"))).IsFalse()
				.Because("the match implements the marker interface, so it is registered under it");
		}
	}
}
