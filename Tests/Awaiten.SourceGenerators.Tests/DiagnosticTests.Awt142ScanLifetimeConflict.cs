using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt142ScanLifetimeConflict
	{
		[Test]
		public async Task ReportsWhenTwoScansMatchTheSameTypeWithDifferentLifetimes()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IA { }
			                                       public interface IB { }
			                                       public sealed class Both : IA, IB { }

			                                       [Container]
			                                       [Scan(typeof(IA), Lifetime = Lifetime.Singleton)]
			                                       [Scan(typeof(IB), Lifetime = Lifetime.Transient)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT142*Both*Singleton*Transient*").AsWildcard()
				.Because("two scans fixing different lifetimes for one implementation contradict each other");
		}

		[Test]
		public async Task DoesNotReportWhenTheScansAgreeOnTheLifetime()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IA { }
			                                       public interface IB { }
			                                       public sealed class Both : IA, IB { }

			                                       [Container]
			                                       [Scan(typeof(IA), Lifetime = Lifetime.Singleton)]
			                                       [Scan(typeof(IB), Lifetime = Lifetime.Singleton)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT142*").AsWildcard()
				.Because("agreeing scans coalesce without contradiction");
		}

		[Test]
		public async Task DoesNotReportWhenAScanYieldsToAnExplicitRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), Lifetime = Lifetime.Singleton)]
			                                       [Transient<AlphaPlugin>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a scan is overridable and deliberately yields to an explicit registration");
		}
	}
}
