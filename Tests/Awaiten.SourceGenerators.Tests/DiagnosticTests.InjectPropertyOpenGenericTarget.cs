using System.Linq;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	/// <summary>
	///     The existing <c>[InjectProperty]</c> diagnostics for the <c>typeof</c> form: a closed target is checked like
	///     the generic form, and an open-generic target is checked once per closing the container constructs.
	/// </summary>
	public class InjectPropertyOpenGenericTarget
	{
		[Test]
		public async Task CleanOpenTarget_ReportsNothing()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public sealed class Repository<T>
			                                       {
			                                           public Clock? Clock { get; set; }
			                                       }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       [Singleton<Repository<int>>]
			                                       [Singleton<Repository<string>>]
			                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT*").AsWildcard()
				.Because("a valid open-generic entry with constructed closings is applied cleanly");
		}

		[Test]
		public async Task ReportsAwt177OnceWhenThePropertyIsNotOnTheGenericDefinition()
		{
			const string source = """
			                      using Awaiten;

			                      namespace MyCode;

			                      public sealed class Clock { }
			                      public sealed class Repository<T>
			                      {
			                          public Clock? Clock { get; set; }
			                      }

			                      [Container]
			                      [Singleton<Clock>]
			                      [Singleton<Repository<int>>]
			                      [Singleton<Repository<string>>]
			                      [InjectProperty(typeof(Repository<>), "Clok")]
			                      public static partial class MyContainer
			                      {
			                      }
			                      """;

			(_, GeneratorDriverRunResult run) = Generator.RunGenerator(source, [], []);

			Diagnostic[] diagnostics = run.Diagnostics.Where(d => d.Id.StartsWith("AWT", StringComparison.Ordinal)).ToArray();
			await That(diagnostics.Select(d => d.Id)).IsEqualTo(["AWT177",])
				.Because("a name missing on the definition is missing on every closing, so it is reported once at the attribute rather than once per closing");
			await That(diagnostics[0].GetMessage()).Contains("Repository<T>")
				.Because("the message names the generic definition the attribute targets");
		}

		[Test]
		public async Task ReportsAwt177ForAReadOnlyPropertyOnTheDefinitionEvenWithoutClosings()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public sealed class Repository<T>
			                                       {
			                                           public Clock? Clock { get; }
			                                       }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT177*").AsWildcard()
				.Because("the definition is checked independently of its closings, so a read-only property is reported even when nothing is constructed");
			await That(result.Diagnostics).DoesNotContain("*AWT180*").AsWildcard()
				.Because("an entry rejected as AWT177 is dropped, so it is not reported a second time as never applied");
		}

		[Test]
		public async Task ReportsAwt177ForAClosedTypeofTargetLikeTheGenericForm()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public sealed class Report
			                                       {
			                                           public Clock? Clock { get; set; }
			                                       }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       [Singleton<Report>]
			                                       [InjectProperty(typeof(Report), "Clok")]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*error AWT177*").AsWildcard()
				.Because("a closed typeof target is checked exactly like [InjectProperty<Report>]");
		}

		[Test]
		public async Task ReportsAwt136ForEveryClosing()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public sealed class Repository<T>
			                                       {
			                                           public Clock? Clock { get; private set; }
			                                       }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       [Singleton<Repository<int>>]
			                                       [Singleton<Repository<string>>]
			                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT136*Repository<int>*").AsWildcard();
			await That(result.Diagnostics).Contains("*AWT136*Repository<string>*").AsWildcard()
				.Because("the setter accessibility check of the generic form applies to each closing");
		}

		[Test]
		public async Task ReportsAwt101OnlyForTheClosingWhoseSubstitutedPropertyTypeIsMissing()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IValidator<T> { }
			                                       public sealed class IntValidator : IValidator<int> { }
			                                       public sealed class Repository<T>
			                                       {
			                                           public IValidator<T>? Validator { get; set; }
			                                       }

			                                       [Container]
			                                       [Singleton<IntValidator, IValidator<int>>]
			                                       [Singleton<Repository<int>>]
			                                       [Singleton<Repository<string>>]
			                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Validator))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT101*IValidator<string>*").AsWildcard()
				.Because("Repository<string> needs IValidator<string>, which is not registered");
			await That(result.Diagnostics).DoesNotContain("*AWT101*IValidator<int>*").AsWildcard()
				.Because("Repository<int> needs IValidator<int>, which is registered, so the property type is substituted per closing");
		}

		[Test]
		public async Task ReportsAwt178ForAFactoryProducedClosing()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public sealed class Repository<T>
			                                       {
			                                           public Clock? Clock { get; set; }
			                                       }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       [Singleton<Repository<int>>(Factory = nameof(MakeRepository))]
			                                       [Singleton<Repository<string>>]
			                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static Repository<int> MakeRepository() => new();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT178*Repository<int>*").AsWildcard()
				.Because("a factory-produced closing is built whole by its source, so the open entry cannot fill it");
			await That(result.Diagnostics).DoesNotContain("*AWT178*Repository<string>*").AsWildcard()
				.Because("the container-constructed closing is filled normally");
		}

		[Test]
		public async Task TwoOpenEntriesNamingTheSameProperty_LeaveAwt179ToTheAnalyzer()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public sealed class Repository<T>
			                                       {
			                                           public Clock? Clock { get; set; }
			                                       }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       [Singleton<Repository<int>>]
			                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
			                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT179*").AsWildcard()
				.Because("AwaitenAnalyzer reports the duplicate open entry, so the generator must not report it a second time");
		}

		[Test]
		public async Task ClosedEntry_WinsOverAnOpenEntryForTheSameClosing()
		{
			const string source = """
			                      using Awaiten;

			                      namespace MyCode;

			                      public interface IChannel { }
			                      public sealed class Consumer<T>
			                      {
			                          public IChannel? Channel { get; set; }
			                      }

			                      [Container]
			                      [Singleton<Consumer<int>>]
			                      [InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Channel))]
			                      [InjectProperty<Consumer<int>>(nameof(Consumer<object>.Channel), Optional = true)]
			                      public static partial class MyContainer
			                      {
			                      }
			                      """;

			(_, GeneratorDriverRunResult run) = Generator.RunGenerator(source, [], []);

			Diagnostic[] diagnostics = run.Diagnostics.Where(d => d.Id.StartsWith("AWT", StringComparison.Ordinal)).ToArray();
			await That(diagnostics).IsEmpty()
				.Because("the closed Optional entry wins on Consumer<int>, so the unregistered IChannel is no AWT101, and as a deliberate specialisation it is no AWT179 either");
		}

		[Test]
		public async Task ReportsAwt180WhenNoClosingOfTheDefinitionIsConstructed()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T>
			                                       {
			                                           public Clock? Clock { get; set; }
			                                       }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       // No consumer requires a closing, so the open registration constructs none.
			                                       [Singleton(typeof(Repository<>), typeof(IRepository<>))]
			                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*warning AWT180*Repository<T>*").AsWildcard()
				.Because("an open entry is never applied when the container constructs no closing of its definition");
		}

		[Test]
		public async Task DoesNotReportAwt180WhenAClosingIsConstructedThroughAnOpenRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public interface IRepository<T> { }
			                                       public sealed class Repository<T> : IRepository<T>
			                                       {
			                                           public Clock? Clock { get; set; }
			                                       }
			                                       public sealed class Consumer
			                                       {
			                                           public Consumer(IRepository<int> repository) { }
			                                       }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       [Singleton(typeof(Repository<>), typeof(IRepository<>))]
			                                       [Transient<Consumer>]
			                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT*").AsWildcard()
				.Because("the consumer makes the container construct Repository<int>, which the open entry fills");
		}

		[Test]
		public async Task ReportsAwt181ForEveryClosingWhenTheDefinitionPropertyCarriesInject()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public sealed class Repository<T>
			                                       {
			                                           [Inject] public Clock? Clock { get; set; }
			                                       }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       [Singleton<Repository<int>>]
			                                       [Singleton<Repository<string>>]
			                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock), Optional = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT181*Repository<int>*").AsWildcard();
			await That(result.Diagnostics).Contains("*AWT181*Repository<string>*").AsWildcard()
				.Because("[Inject] on the definition's property wins on each closing, so the open entry's flags are ignored there");
		}

		[Test]
		public async Task ReportsAwt170ForAnUnsupportedKeyOnTheTypeofForm()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public sealed class Repository<T>
			                                       {
			                                           public Clock? Clock { get; set; }
			                                       }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       [Singleton<Repository<int>>]
			                                       [InjectProperty(typeof(Repository<>), nameof(Repository<object>.Clock), Key = 5)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT170*").AsWildcard()
				.Because("the Key of the typeof form is guarded like the Key of the generic form");
		}

		[Test]
		public async Task DoesNotFillADecoratorWrapperThatIsAClosingOfTheTarget()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public interface IGreeter<T> { }
			                                       public sealed class Greeter<T> : IGreeter<T> { }
			                                       public sealed class LoggingGreeter<T> : IGreeter<T>
			                                       {
			                                           public LoggingGreeter(IGreeter<T> inner) { }
			                                           public Clock? Clock { get; set; }
			                                       }

			                                       [Container]
			                                       [Singleton<Clock>]
			                                       [Transient<Greeter<int>, IGreeter<int>>]
			                                       [Decorate(typeof(LoggingGreeter<>), typeof(IGreeter<>))]
			                                       [InjectProperty(typeof(LoggingGreeter<>), nameof(LoggingGreeter<object>.Clock))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).DoesNotContain("Clock =")
				.Because("a decorator wrapper is built by the chain, not filled by an entry, exactly as with the generic form");
			await That(result.Diagnostics).DoesNotContain("*AWT180*").AsWildcard()
				.Because("the decorator closing is constructed, so the entry is not misreported as unmatched, like the generic form");
		}
	}
}
