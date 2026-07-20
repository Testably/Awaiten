using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt164InvalidLifecycleHook
	{
		[Fact]
		public async Task ReportsWhenTheHookMethodHasTheWrongParameterType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnActivated = nameof(Missing))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Missing(int wrongType) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT164*").AsWildcard()
				.Because("the named method does not accept the registered implementation type");
		}

		[Fact]
		public async Task ReportsWhenNoMethodOfThatNameExists()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnRelease = nameof(Service))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT164*").AsWildcard()
				.Because("no container method carries the named release hook");
		}

		[Fact]
		public async Task ReportsWhenTheHookMethodReturnsAValue()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static int Started(Service service) => 0;
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT164*").AsWildcard()
				.Because("a lifecycle hook must be a void method");
		}

		[Fact]
		public async Task ReportsWhenTheFirstParameterDoesNotAcceptTheImplementation_EvenWithALaterParameterThatWould()
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
			                                       	private static void Started(Settings settings, Service service) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT164*").AsWildcard()
				.Because("the hook's first parameter is the instance, so it must accept the implementation type");
		}

		[Fact]
		public async Task DoesNotReportForAHookTakingTheInstancePlusGraphParameters()
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
			                                       	private static void Started(Service service, Settings settings) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT164*").AsWildcard()
				.Because("the instance parameter is first and the extra Settings parameter is registered on the graph");
			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the instance parameter is first and the extra Settings parameter is registered on the graph");
		}

		[Fact]
		public async Task ReportsAwt101ForAnUnregisteredHookParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }
			                                       public sealed class Missing { }

			                                       [Container]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service, Missing missing) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
				.Because("a lifecycle hook parameter after the instance is a graph dependency that must be registered");
		}

		[Fact]
		public async Task DoesNotReportForAVoidMethodAcceptingTheImplementation()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnActivated = nameof(Started), OnRelease = nameof(Stopping))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service) { }
			                                       	private static void Stopping(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT164*").AsWildcard()
				.Because("a void method accepting the implementation (or a base type) is a usable hook");
		}

		[Fact]
		public async Task ReportsForAScanHookThatNamesNoMethod()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), OnActivated = "DoesNotExist")]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT164*").AsWildcard()
				.Because("a scan's OnActivated is resolved against the container like any other hook");
		}

		[Fact]
		public async Task DoesNotReportForAScanHookAcceptingTheMarker()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }
			                                       public sealed class BetaPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), OnActivated = nameof(Started), OnRelease = nameof(Stopping))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(IPlugin plugin) { }
			                                       	private static void Stopping(IPlugin plugin) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT164*").AsWildcard()
				.Because("a hook typed as the scanned marker accepts every match");
		}

		[Fact]
		public async Task ReportsForAGenericScanHookWhoseConstraintRejectsTheClosing()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IView<TViewModel> { }
			                                       public sealed class IntView : IView<int> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Wire<TViewModel>(IView<TViewModel> view) where TViewModel : class { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT164*").AsWildcard()
				.Because("the class constraint rejects the int closing, so the hook cannot be constructed for IntView");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard()
				.Because("the violation is caught before construction rather than surfacing as a compiler error inside the generated source");
		}

		[Fact]
		public async Task DoesNotReportForAGenericScanHookConstraintContainingAnArrayOfTheTypeParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface IView<TViewModel> { }
			                                       public sealed class Rows : IEnumerable<Rows[]>
			                                       {
			                                       	public IEnumerator<Rows[]> GetEnumerator() => null!;
			                                       	IEnumerator IEnumerable.GetEnumerator() => null!;
			                                       }
			                                       public sealed class RowsView : IView<Rows> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Wire<TViewModel>(IView<TViewModel> view) where TViewModel : IEnumerable<TViewModel[]> { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT164*").AsWildcard()
				.Because("Rows satisfies IEnumerable<Rows[]>, so the constraint check must substitute the type parameter inside the array element type too");
			await That(result.Diagnostics).IsEmpty()
				.Because("the closing binds cleanly, so the hook is constructed as Wire<Rows> without any diagnostic");
		}

		[Fact]
		public async Task ReportsForAGenericScanHookClosingAtAnInaccessibleTypeArgument()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace Lib;

				public interface IView<TViewModel> { }
				internal sealed class SecretVm { }
				public sealed class TheView : IView<SecretVm> { }
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

			await That(result.Diagnostics).Contains("*AWT164*").AsWildcard()
				.Because("the container cannot name the internal SecretVm as a type argument, so the closing is unbindable and the hook unusable");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard()
				.Because("the inaccessible closing is rejected before construction rather than leaking CS0122 into the generated source");
		}

		[Fact]
		public async Task ReportsForAGenericScanHookConstraintSatisfiedOnlyByAUserDefinedConversion()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public class Widget { }
			                                       public sealed class WidgetModel
			                                       {
			                                       	public static implicit operator Widget(WidgetModel model) => new Widget();
			                                       }
			                                       public interface IView<TViewModel> { }
			                                       public sealed class WidgetView : IView<WidgetModel> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Wire<TViewModel>(IView<TViewModel> view) where TViewModel : Widget { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT164*").AsWildcard()
				.Because("constraint satisfaction admits only identity, reference, and boxing conversions, so the user-defined operator does not make WidgetModel satisfy the Widget constraint");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard()
				.Because("the violation is caught before construction rather than leaking CS0311 into the generated source");
		}

		[Fact]
		public async Task DoesNotReportForAGenericScanHookConstraintNamingANestedTypeOfTheTypeParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public class Outer<T>
			                                       {
			                                       	public interface IInner { }
			                                       }
			                                       public sealed class Model : Outer<Model>.IInner { }
			                                       public interface IView<TViewModel> { }
			                                       public sealed class ModelView : IView<Model> { }

			                                       [Container]
			                                       [Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Wire<TViewModel>(IView<TViewModel> view) where TViewModel : Outer<TViewModel>.IInner { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT164*").AsWildcard()
				.Because("Model satisfies Outer<Model>.IInner, so the constraint check must substitute the type parameter through the nested type's containing type too");
			await That(result.Diagnostics).IsEmpty()
				.Because("the closing binds cleanly, so the hook is constructed as Wire<Model> without any diagnostic");
		}
	}
}
