using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt166ConflictingLifecycleDirectives
	{
		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
		public async Task ReportsWhenALaterRegistrationOptsIntoSuppressDisposalTheWinnerDoesNot()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRead { }
			                                       public interface IWrite { }
			                                       public sealed class Store : IRead, IWrite { }

			                                       [Container]
			                                       [Singleton<Store, IRead>]
			                                       [Singleton<Store, IWrite>(SuppressDisposal = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT166*").AsWildcard()
				.Because("the first registration wins and disposes the instance, so the later SuppressDisposal = true would be silently dropped");
		}

		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
		public async Task DoesNotReportForFactoriesWithoutAnImplementationTypeThatDifferInSuppressDisposal()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Module]
			                                       [Singleton<IClock>(Factory = nameof(Create))]
			                                       public static class ClockModule
			                                       {
			                                       	public static IClock Create() => new SystemClock();
			                                       }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       [Singleton<IClock>(Factory = nameof(Create), SuppressDisposal = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       	public static IClock Create() => new SystemClock();
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the two factories are different implementations, so each keeps its own SuppressDisposal");
		}

		[Test]
		public async Task ReportsForFactoriesWithoutAnImplementationTypeNamingTheSameMemberWithDifferentDirectives()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Container]
			                                       [Singleton<IClock>(Factory = nameof(Create))]
			                                       [Singleton<IClock>(Factory = nameof(Create), Key = "k", SuppressDisposal = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       	public static IClock Create() => new SystemClock();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT166*'MyCode.IClock'*SuppressDisposal*").AsWildcard()
				.Because("registrations naming the same factory are one implementation, which is disposed one way only");
		}

		public class HookIdentity
		{
			[Test]
			public async Task ReportsWhenAModuleAndTheContainerNameHooksOfTheSameName()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public sealed class Grinder { }

				                                       [Module]
				                                       [Singleton<Grinder>(OnActivated = nameof(Init))]
				                                       public static class GrinderModule
				                                       {
				                                       	public static void Init(Grinder grinder) { }
				                                       }

				                                       [Container]
				                                       [Import(typeof(GrinderModule))]
				                                       [Singleton<Grinder>(OnActivated = nameof(Init))]
				                                       public static partial class CoffeeShop
				                                       {
				                                       	private static void Init(Grinder grinder) { }
				                                       }
				                                       """);

				await That(result.Diagnostics).Contains("*AWT166*'MyCode.Grinder'*OnActivated*('MyCode.CoffeeShop.Init' and 'MyCode.GrinderModule.Init')*").AsWildcard()
					.Because("the two hooks share a name but are declared on different types, so coalescing would silently drop one of them");
			}

			[Test]
			public async Task ReportsWhenTwoModulesNameReleaseHooksOfTheSameName()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IRead { }
				                                       public interface IWrite { }
				                                       public sealed class Store : IRead, IWrite { }

				                                       [Module]
				                                       [Singleton<Store, IRead>(OnRelease = nameof(Close))]
				                                       public static class ReadModule
				                                       {
				                                       	public static void Close(Store store) { }
				                                       }

				                                       [Module]
				                                       [Singleton<Store, IWrite>(OnRelease = nameof(Close))]
				                                       public static class WriteModule
				                                       {
				                                       	public static void Close(Store store) { }
				                                       }

				                                       [Container]
				                                       [Import(typeof(ReadModule))]
				                                       [Import(typeof(WriteModule))]
				                                       public static partial class MyContainer
				                                       {
				                                       }
				                                       """);

				await That(result.Diagnostics).Contains("*AWT166*'MyCode.Store'*OnRelease*('MyCode.ReadModule.Close' and 'MyCode.WriteModule.Close')*").AsWildcard()
					.Because("each module's Close is a different method, and the coalesced instance runs only one of them");
			}

			[Test]
			public async Task QualifiesDifferentHooksOfOneContainerWithTheContainer()
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

				await That(result.Diagnostics).Contains("*AWT166*('MyCode.MyContainer.Started' and 'MyCode.MyContainer.Woken')*").AsWildcard()
					.Because("each hook is named with its declaring type");
			}

			[Test]
			public async Task DoesNotReportWhenRegistrationsNameTheSameModuleHook()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IRead { }
				                                       public interface IWrite { }
				                                       public sealed class Store : IRead, IWrite { }

				                                       [Module]
				                                       [Singleton<Store, IRead>(OnActivated = nameof(Init))]
				                                       [Singleton<Store, IWrite>(OnActivated = nameof(Init))]
				                                       public static class StoreModule
				                                       {
				                                       	public static void Init(Store store) { }
				                                       }

				                                       [Container]
				                                       [Import(typeof(StoreModule))]
				                                       public static partial class MyContainer
				                                       {
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("both registrations name the same member of the same module, so nothing is dropped");
			}

			[Test]
			public async Task DoesNotReportWhenTheContainerReplacesAFallbackModuleHookOfTheSameName()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public sealed class Grinder { }

				                                       [Module]
				                                       [Singleton<Grinder>(OnActivated = nameof(Init), Fallback = Fallback.Silent)]
				                                       public static class GrinderModule
				                                       {
				                                       	public static void Init(Grinder grinder) { }
				                                       }

				                                       [Container]
				                                       [Import(typeof(GrinderModule))]
				                                       [Singleton<Grinder>(OnActivated = nameof(Init))]
				                                       public static partial class CoffeeShop
				                                       {
				                                       	private static void Init(Grinder grinder) { }
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("a Fallback registration is replaced in full by the container's registration, hook included");
			}
		}
	}
}
