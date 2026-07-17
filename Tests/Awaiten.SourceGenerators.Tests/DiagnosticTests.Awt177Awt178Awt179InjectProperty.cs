using System.Linq;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt177Awt178Awt179InjectProperty
	{
		[Fact]
		public async Task ReportsAwt177WhenInjectPropertyNamesAnUnknownProperty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           public Bus? Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Consumer>]
			                                       // Typo: there is no property 'Buss' on Consumer.
			                                       [InjectProperty<Consumer>("Buss")]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT177*").AsWildcard()
				.Because("an [InjectProperty] name must resolve to a settable property on the implementation");
		}

		[Fact]
		public async Task ReportsAwt177WhenInjectPropertyNamesAReadOnlyProperty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           // Get-only: there is no set/init accessor at all, so it is not a settable property.
			                                           public Bus? Bus { get; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Consumer>]
			                                       [InjectProperty<Consumer>(nameof(Consumer.Bus))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT177*").AsWildcard()
				.Because("a read-only (get-only) member is not a settable property, so [InjectProperty] cannot fill it");
		}

		[Fact]
		public async Task ReportsAwt178WhenInjectPropertyTargetsAFactoryProducedImplementation()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Widget
			                                       {
			                                           public Bus? Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Widget>(Factory = nameof(MakeWidget))]
			                                       [InjectProperty<Widget>(nameof(Widget.Bus))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static Widget MakeWidget() => new Widget();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT178*").AsWildcard()
				.Because("property injection only fills a container-constructed instance; a Factory produces it whole");
			await That(result.Diagnostics).DoesNotContain("*AWT180*").AsWildcard()
				.Because("a Factory-produced implementation is registered, so it is AWT178 rather than the unmatched AWT180");
		}

		[Fact]
		public async Task ReportsAwt178WhenInjectPropertyTargetsAnInstanceRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Widget
			                                       {
			                                           public Bus? Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Singleton<Widget>(Instance = nameof(TheWidget))]
			                                       [InjectProperty<Widget>(nameof(Widget.Bus))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static Widget TheWidget { get; } = new Widget();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT178*").AsWildcard()
				.Because("a pre-built Instance is produced whole by its source, so [InjectProperty] cannot fill it");
		}

		[Fact]
		public async Task ReportsAwt179WhenTwoInjectPropertyEntriesNameTheSameProperty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           public Bus? Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Consumer>]
			                                       [InjectProperty<Consumer>(nameof(Consumer.Bus))]
			                                       [InjectProperty<Consumer>(nameof(Consumer.Bus))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			// AWT179 is a warning, not an error: the graph is well-defined (the first entry wins).
			await That(result.Diagnostics).Contains("*warning AWT179*").AsWildcard()
				.Because("a property is injected once, so a duplicate [InjectProperty] entry is a suppressible warning");
		}

		[Fact]
		public async Task ReportsAwt180WhenInjectPropertyTargetsAnUnregisteredImplementation()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Unregistered
			                                       {
			                                           public int X { get; set; }
			                                       }

			                                       [Container]
			                                       // Unregistered has no registration, so the entry can never be applied.
			                                       [InjectProperty<Unregistered>(nameof(Unregistered.X))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT180*").AsWildcard()
				.Because("an [InjectProperty] whose implementation has no container-constructed registration is never applied");
		}

		[Fact]
		public async Task DoesNotReportAwt180ForADecoratorTypeTarget()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IGreeter { }
			                                       public sealed class Greeter : IGreeter { }
			                                       public sealed class LoggingGreeter : IGreeter
			                                       {
			                                           public LoggingGreeter(IGreeter inner) { Inner = inner; }
			                                           public IGreeter Inner { get; }
			                                           public string? Tag { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Greeter, IGreeter>]
			                                       [Decorate<LoggingGreeter, IGreeter>]
			                                       // The decorator IS constructed (via the chain), so this must not be flagged as unmatched.
			                                       [InjectProperty<LoggingGreeter>(nameof(LoggingGreeter.Tag))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT180*").AsWildcard()
				.Because("a decorator chain link is constructed (under a synthetic identity), so a decorator-type target must not be misreported as unmatched");
		}

		[Fact]
		public async Task ReportsAwt170WhenInjectPropertyKeyIsAnUnsupportedConstant()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           public Bus? Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Consumer>]
			                                       // A numeric key is not a supported key constant (string, enum or typeof).
			                                       [InjectProperty<Consumer>(nameof(Consumer.Bus), Key = 5)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT170*").AsWildcard()
				.Because("an [InjectProperty] Key must be a supported key constant, guarded like a registration's Key and [FromKey]");
		}

		[Fact]
		public async Task ReportsAwt181WhenAPropertyCarriesBothInjectAndInjectProperty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           [Inject] public Bus? Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Consumer>]
			                                       // The property already carries [Inject], which wins; the entry's flags are ignored.
			                                       [InjectProperty<Consumer>(nameof(Consumer.Bus), Optional = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT181*").AsWildcard()
				.Because("a property named by both [Inject] and [InjectProperty] warns that the [Inject] member wins");
		}

		[Fact]
		public async Task DoesNotReportAwt181WhenOnlyInjectPropertyApplies()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           // A plain POCO property: only the container-side entry declares injection.
			                                           public Bus? Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Consumer>]
			                                       [InjectProperty<Consumer>(nameof(Consumer.Bus))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT*").AsWildcard()
				.Because("a property named only by [InjectProperty] (no [Inject]) is filled cleanly, with no diagnostic");
		}

		[Fact]
		public async Task ContainerSideShapeDiagnostic_PointsAtTheAttribute_NotThePocoProperty()
		{
			const string source = """
			                      using Awaiten;

			                      namespace MyCode;

			                      public sealed class Dep { }
			                      public sealed class Consumer
			                      {
			                          public Dep? Dep { get; init; }
			                      }

			                      [Container]
			                      [Transient<Dep>]
			                      [Transient<Consumer>]
			                      [InjectProperty<Consumer>(nameof(Consumer.Dep), Deferred = true)]
			                      public static partial class MyContainer
			                      {
			                      }
			                      """;

			(_, GeneratorDriverRunResult run) = Generator.RunGenerator(source, [], []);

			// AWT144: a Deferred entry needs a plain set accessor, but Dep is init-only.
			Diagnostic diagnostic = run.Diagnostics.Single(d => d.Id == "AWT144");
			int line = diagnostic.Location.GetLineSpan().StartLinePosition.Line;
			string offendingLine = source.Split('\n')[line];

			await That(offendingLine).Contains("InjectProperty")
				.Because("a container-side entry's diagnostics point at the [InjectProperty] attribute the author edits, not the POCO property");
		}
	}
}
