using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt190AmbiguousLifecycleHook
	{
		[Fact]
		public async Task ReportsWhenTwoOverloadsBothAcceptTheInstance()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }
			                                       public sealed class Settings { }

			                                       [Container]
			                                       [Singleton<Settings>]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service) { }
			                                       	private static void Started(Service service, Settings settings) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT190*").AsWildcard()
				.Because("two accessible methods named Started accept the instance, so the container cannot choose one");
		}

		[Fact]
		public async Task ReportsWhenOverloadsDifferOnlyInTheInstanceParameterType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnRelease = nameof(Stopping))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Stopping(Service service) { }
			                                       	private static void Stopping(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT190*").AsWildcard()
				.Because("both the exact and the object overload accept the instance, so the release hook choice is order-dependent");
		}

		[Fact]
		public async Task DoesNotReportWhenOnlyOneOverloadAcceptsTheInstance()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service) { }
			                                       	private static void Started(int unrelated) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT190*").AsWildcard()
				.Because("the int overload does not accept the instance, so there is exactly one usable hook and no ambiguity");
			await That(result.Diagnostics).DoesNotContain("*AWT164*").AsWildcard()
				.Because("the int overload does not accept the instance, so there is exactly one usable hook and no ambiguity");
		}

		[Fact]
		public async Task ReportsWhenAGenericScanHookOverloadBindsBesideANonGenericOne()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IView<TViewModel> { }
			                                       public sealed class MainWindow : IView<int> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Wire<TViewModel>(IView<TViewModel> view) { }
			                                       	private static void Wire(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT190*").AsWildcard()
				.Because("the generic overload binds the single closing and the object overload also accepts the match, so the container cannot choose one");
		}

		[Fact]
		public async Task ReportsWhenAnAmbiguousGenericScanHookOverloadSitsBesideANonGenericOne()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IView<TViewModel> { }
			                                       public sealed class DualView : IView<int>, IView<string> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Wire<TViewModel>(IView<TViewModel> view) { }
			                                       	private static void Wire(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT190*").AsWildcard()
				.Because("the generic overload could dispatch through either closing and the object overload also accepts the match, so silently preferring the sibling would let an extra closing change which method runs");
			await That(result.Diagnostics).DoesNotContain("*AWT198*").AsWildcard()
				.Because("the collision is between overloads, not closings: settling the closings would still leave two usable overloads");
		}

		[Fact]
		public async Task DoesNotReportWhenTheSameNameServesDistinctRegistrationsOneMatchEach()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Alpha { }
			                                       public sealed class Beta { }

			                                       [Container]
			                                       [Singleton<Alpha>(OnActivated = nameof(Started))]
			                                       [Singleton<Beta>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT190*").AsWildcard()
				.Because("a single shared object hook is one match per registration, so neither registration is ambiguous");
		}
	}
}
