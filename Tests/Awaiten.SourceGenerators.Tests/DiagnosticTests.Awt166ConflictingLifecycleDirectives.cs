using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt166ConflictingLifecycleDirectives
	{
		[Fact]
		public async Task ReportsWhenCoalescedRegistrationsNameDifferentOnActivatedHooks()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRead { }
			                                       public interface IWrite { }
			                                       public sealed class Store : IRead, IWrite { }

			                                       [Container]
			                                       [Singleton<Store, IRead>(OnActivated = nameof(Started))]
			                                       [Singleton<Store, IWrite>(OnActivated = nameof(Woken))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Store store) { }
			                                       	private static void Woken(Store store) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT166*").AsWildcard()
				.Because("coalescing keeps the first OnActivated hook, so the second, different one must not be silently dropped");
		}

		[Fact]
		public async Task ReportsWhenCoalescedRegistrationsNameDifferentOnReleaseHooks()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRead { }
			                                       public interface IWrite { }
			                                       public sealed class Store : IRead, IWrite { }

			                                       [Container]
			                                       [Singleton<Store, IRead>(OnRelease = nameof(Closing))]
			                                       [Singleton<Store, IWrite>(OnRelease = nameof(Draining))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Closing(Store store) { }
			                                       	private static void Draining(Store store) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT166*").AsWildcard()
				.Because("coalescing keeps the first OnRelease hook, so the second, different one must not be silently dropped");
		}

		[Fact]
		public async Task ReportsWhenALaterRegistrationOptsIntoEagerTheWinnerDoesNot()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRead { }
			                                       public interface IWrite { }
			                                       public sealed class Store : IRead, IWrite { }

			                                       [Container]
			                                       [Singleton<Store, IRead>]
			                                       [Singleton<Store, IWrite>(Eager = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT166*").AsWildcard()
				.Because("the first registration wins and is not eager, so the later Eager = true would be silently dropped");
		}

		[Fact]
		public async Task ReportsWhenALaterRegistrationSetsAHookTheWinnerOmits()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRead { }
			                                       public interface IWrite { }
			                                       public sealed class Store : IRead, IWrite { }

			                                       [Container]
			                                       [Singleton<Store, IRead>]
			                                       [Singleton<Store, IWrite>(OnActivated = nameof(Woken))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Woken(Store store) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT166*").AsWildcard()
				.Because("the winning registration names no hook, so the later hook the coalesced instance will not run is a silent drop, not a permissible merge");
		}

		[Fact]
		public async Task DoesNotReportForIdenticalDirectivesAcrossServices()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRead { }
			                                       public interface IWrite { }
			                                       public sealed class Store : IRead, IWrite { }

			                                       [Container]
			                                       [Singleton<Store, IRead>(OnActivated = nameof(Started), OnRelease = nameof(Closing))]
			                                       [Singleton<Store, IWrite>(OnActivated = nameof(Started), OnRelease = nameof(Closing))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Store store) { }
			                                       	private static void Closing(Store store) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT166*").AsWildcard()
				.Because("registering one implementation under several services with the same directives drops nothing");
		}

		[Fact]
		public async Task DoesNotReportWhenALaterRegistrationOmitsADirectiveTheWinnerSets()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRead { }
			                                       public interface IWrite { }
			                                       public sealed class Store : IRead, IWrite { }

			                                       [Container]
			                                       [Singleton<Store, IRead>(OnActivated = nameof(Started), Eager = true)]
			                                       [Singleton<Store, IWrite>]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Store store) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT166*").AsWildcard()
				.Because("a registration that leaves a directive unset states no opinion and merges with the winner's directive rather than conflicting");
		}

		[Fact]
		public async Task PointsAtTheLosingRegistrationNotTheWinner()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRead { }
			                                       public interface IWrite { }
			                                       public sealed class Store : IRead, IWrite { }

			                                       [Container]
			                                       [Singleton<Store, IRead>(OnActivated = nameof(Started))]
			                                       [Singleton<Store, IWrite>(OnActivated = nameof(Woken))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Store store) { }
			                                       	private static void Woken(Store store) { }
			                                       }
			                                       """);

			string diagnostic = result.Diagnostics.Single(d => d.Contains("AWT166"));
			await That(diagnostic).Contains("(11,")
				.Because("the diagnostic points at the losing registration whose directive is silently dropped");
			await That(diagnostic).DoesNotContain("(10,")
				.Because("not the winning registration, whose directive survives");
		}

		[Fact]
		public async Task ReportsEachContradictedDirectiveOfOneRegistrationIndependently()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRead { }
			                                       public interface IWrite { }
			                                       public sealed class Store : IRead, IWrite { }

			                                       [Container]
			                                       [Singleton<Store, IRead>]
			                                       [Singleton<Store, IWrite>(OnActivated = nameof(Woken), Eager = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Woken(Store store) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Count(d => d.Contains("AWT166"))).IsEqualTo(2)
				.Because("a registration contradicting the winner on two directives drops each independently, so each is reported");
			await That(result.Diagnostics).Contains("*AWT166*OnActivated*").AsWildcard()
				.Because("the winner names no OnActivated, so the later hook it will not run is one dropped directive");
			await That(result.Diagnostics).Contains("*AWT166*Eager*").AsWildcard()
				.Because("the winner is not eager, so the later Eager = true is a second, separately dropped directive");
		}
	}
}
