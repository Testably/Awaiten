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
	}
}
