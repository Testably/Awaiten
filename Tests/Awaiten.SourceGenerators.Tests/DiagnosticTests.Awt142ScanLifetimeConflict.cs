using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt142ScanLifetimeConflict
	{
		[Fact]
		public async Task ReportsWhenTwoScansMatchTheSameTypeWithDifferentLifetimes()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IA { }
			                                       public interface IB { }
			                                       public sealed class Both : IA, IB { }

			                                       [Container]
			                                       [Scan(typeof(IA), Lifetime = AwaitenLifetime.Singleton)]
			                                       [Scan(typeof(IB), Lifetime = AwaitenLifetime.Transient)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT142*Both*Singleton*Transient*").AsWildcard()
				.Because("two scans fixing different lifetimes for one implementation contradict each other");
		}

		[Fact]
		public async Task DoesNotReportWhenTheScansAgreeOnTheLifetime()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IA { }
			                                       public interface IB { }
			                                       public sealed class Both : IA, IB { }

			                                       [Container]
			                                       [Scan(typeof(IA), Lifetime = AwaitenLifetime.Singleton)]
			                                       [Scan(typeof(IB), Lifetime = AwaitenLifetime.Singleton)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT142"))).IsFalse()
				.Because("agreeing scans coalesce without contradiction");
		}

		[Fact]
		public async Task DoesNotReportWhenAScanYieldsToAnExplicitRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), Lifetime = AwaitenLifetime.Singleton)]
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
