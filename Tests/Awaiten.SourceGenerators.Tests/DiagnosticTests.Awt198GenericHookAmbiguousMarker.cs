using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt198GenericHookAmbiguousMarker
	{
		[Fact]
		public async Task ReportsWhenAMatchClosesTheMarkerMoreThanOnce()
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
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT198*").AsWildcard()
				.Because("DualView closes IView<> at both int and string, so a generic hook's type argument is ambiguous");
		}

		[Fact]
		public async Task DoesNotReportForASingleClosedForm()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IView<TViewModel> { }
			                                       public interface IMainViewModel { }
			                                       public sealed class MainViewModel : IMainViewModel { }
			                                       public sealed class MainWindow : IView<IMainViewModel> { }

			                                       [Container]
			                                       [Singleton<MainViewModel, IMainViewModel>]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Wire<TViewModel>(IView<TViewModel> view, TViewModel viewModel) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT198*").AsWildcard()
				.Because("MainWindow closes IView<> exactly once, so the generic hook's type argument is unambiguous");
			await That(result.Diagnostics).DoesNotContain("*AWT164*").AsWildcard()
				.Because("the generic hook binds its type argument from the single closed marker form and is usable");
		}

		[Fact]
		public async Task DoesNotReportForANonGenericHookOnAMatchClosingTheMarkerMoreThanOnce()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IView<TViewModel> { }
			                                       public sealed class DualView : IView<int>, IView<string> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Log))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Log(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT198*").AsWildcard()
				.Because("a non-generic hook needs no type argument, so a match closing the marker several times is not ambiguous");
			await That(result.Diagnostics).DoesNotContain("*AWT164*").AsWildcard()
				.Because("the non-generic hook accepts every match as object and is a usable hook");
		}

		[Fact]
		public async Task ReportsWhenTwoOpenMarkersBindOneGenericHookToDifferentClosings()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IView<TViewModel> { }
			                                       public interface IEditor<TModel> { }
			                                       public sealed class Dual : IView<int>, IEditor<string> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       [Scan(typeof(IEditor<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Wire<TArg>(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT198*").AsWildcard()
				.Because("two open-generic scans bind the same generic hook to different closings of Dual, so its type argument is ambiguous rather than silently the first-seen one");
		}

		[Fact]
		public async Task DoesNotReportWhenTwoMarkersConstructTheIdenticalMethod()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IView<TViewModel> { }
			                                       public interface IEditor<TModel> { }
			                                       public sealed class Dual : IView<int>, IEditor<int> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       [Scan(typeof(IEditor<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Wire<TArg>(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("both closings construct the identical Wire<int>, so the dispatch is the same and nothing is ambiguous");
			await That(result.Sources.Values.Any(source => source.Contains("Wire<int>"))).IsTrue()
				.Because("the single constructed dispatch is emitted");
		}

		[Fact]
		public async Task DoesNotReportWhenNoHookIsNamed()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IView<TViewModel> { }
			                                       public sealed class DualView : IView<int>, IView<string> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT198*").AsWildcard()
				.Because("a match closing the marker several times is ordinary without a hook to bind a type argument for");
		}

		[Fact]
		public async Task DoesNotReportWhenTwoScansHookDifferentSlots()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IView<TViewModel> { }
			                                       public interface IEditor<TModel> { }
			                                       public sealed class Dual : IView<int>, IEditor<string> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       [Scan(typeof(IEditor<>), As = ScanAs.Marker, OnRelease = nameof(Cleanup))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Wire<TViewModel>(IView<TViewModel> view) { }
			                                       	private static void Cleanup(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("each hook binds through its own scan's marker: Wire sees only IView<int>, so nothing is ambiguous and both hooks apply");
		}

		[Fact]
		public async Task ReportsTheAmbiguousClosedFormsInTheMessage()
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
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT198*'Wire'*MyCode.IView<int>, MyCode.IView<string>*").AsWildcard()
				.Because("the message names the hook and every closed form it could bind, which is what makes the ambiguity actionable");
		}

		[Fact]
		public async Task DoesNotReportWhenAConstraintLeavesASingleBindableForm()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IView<TViewModel> { }
			                                       public sealed class Payments { }
			                                       public sealed class DualView : IView<int>, IView<Payments> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Wire<TViewModel>(IView<TViewModel> view) where TViewModel : class { }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the class constraint rules out the int closing, so only IView<Payments> can bind the hook and nothing is ambiguous");
		}

		[Fact]
		public async Task DoesNotReportWhenAccessibilityLeavesASingleBindableForm()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace Lib;

				public interface IView<TViewModel> { }
				public sealed class PublicVm { }
				internal sealed class SecretVm { }
				public sealed class DualView : IView<PublicVm>, IView<SecretVm> { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Scan(typeof(IView<>), InAssembliesOf = new[] { typeof(IView<>) }, OnActivated = nameof(Wire))]
				public static partial class MyContainer
				{
					private static void Wire<TViewModel>(IView<TViewModel> view) { }
				}
				""");

			await That(result.Diagnostics).IsEmpty()
				.Because("the container cannot name the internal SecretVm, so like a constraint the accessibility check settles the family on IView<PublicVm>");
			await That(result.Sources.Values.Any(source => source.Contains("Wire<global::Lib.PublicVm>"))).IsTrue()
				.Because("the single accessible closing is the one dispatched");
		}

		[Fact]
		public async Task ReportsAwt164WhenTheGenericHookAritiesNeverMatchTheMarker()
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
			                                       	private static void Wire<TFirst, TSecond>(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT164*").AsWildcard()
				.Because("a two-parameter generic hook can never bind a one-argument marker closing, so the name is unusable, not ambiguous");
			await That(result.Diagnostics).DoesNotContain("*AWT198*").AsWildcard()
				.Because("ambiguity only applies to a hook that could actually bind more than one closed form");
		}
	}
}
