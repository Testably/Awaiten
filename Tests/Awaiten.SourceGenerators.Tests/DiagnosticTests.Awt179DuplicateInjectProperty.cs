using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	/// <summary>
	///     AWT179 is reported by <see cref="AwaitenAnalyzer" /> rather than the generator, so it can be suppressed in
	///     source; these tests drive the analyzer.
	/// </summary>
	public class Awt179DuplicateInjectProperty
	{
		[Test]
		public async Task TwoClosedEntries_ReportsOnceAtTheSecondEntry()
		{
			string source = OpenGenericContainer("""
			                                     [InjectProperty<Consumer<int>>(nameof(Consumer<int>.Channel))]
			                                     [InjectProperty<Consumer<int>>(nameof(Consumer<int>.Channel), Optional = true)]
			                                     """);

			string[] reports = await Awt179Reports(source);

			await That(reports).HasCount(1)
				.Because("two closed entries for one property are a duplicate, whatever their options");
			await That(reports[0]).StartsWith($"({LineOf(source, "Optional = true")},")
				.Because("the warning points at the second entry, the one that is ignored");
		}

		[Test]
		public async Task TwoOpenEntries_ReportsOnceAtTheSecondEntry()
		{
			string source = OpenGenericContainer("""
			                                     [InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Channel))]
			                                     [InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Channel), Optional = true)]
			                                     """);

			string[] reports = await Awt179Reports(source);

			await That(reports).HasCount(1)
				.Because("two open entries for one property of a definition are a duplicate, reported once and not once per closing");
			await That(reports[0]).StartsWith($"({LineOf(source, "Optional = true")},")
				.Because("the warning points at the second entry, the one that is ignored");
			await That(reports[0]).Contains("Consumer<T>")
				.Because("the duplicate is on the generic definition");
		}

		[Test]
		public async Task ClosedEntryThatRepeatsTheOpenEntry_ReportsOnceAtTheOpenEntry()
		{
			string source = OpenGenericContainer("""
			                                     [InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Channel))]
			                                     [InjectProperty<Consumer<int>>(nameof(Consumer<int>.Channel))]
			                                     """);

			string[] reports = await Awt179Reports(source);

			await That(reports).HasCount(1)
				.Because("a closed entry that changes nothing adds nothing to the open entry, so it is a redundant duplicate on its closing only");
			await That(reports[0]).StartsWith($"({LineOf(source, "typeof(Consumer<>)")},")
				.Because("the warning points at the open entry, the one that is ignored for this closing");
			await That(reports[0]).Contains("Consumer<int>")
				.Because("the duplicate is on the overridden closing, not on Consumer<string>");
		}

		[Test]
		public async Task ClosedEntryWithAnExplicitNullKey_RepeatsAnOpenEntryWithoutKey()
		{
			string source = OpenGenericContainer("""
			                                     [InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Channel))]
			                                     [InjectProperty<Consumer<int>>(nameof(Consumer<int>.Channel), Key = null, Optional = false, Deferred = false)]
			                                     """);

			string[] reports = await Awt179Reports(source);

			await That(reports).HasCount(1)
				.Because("an explicit null Key and false flags are the defaults, so the closed entry changes nothing");
		}

		[Test]
		public async Task ClosedEntryWithTheSameKey_RepeatsTheOpenEntry()
		{
			string source = OpenGenericContainer("""
			                                     [InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Channel), Key = "fast")]
			                                     [InjectProperty<Consumer<int>>(nameof(Consumer<int>.Channel), Key = "fast")]
			                                     """);

			string[] reports = await Awt179Reports(source);

			await That(reports).HasCount(1)
				.Because("the closed entry selects the same keyed registration as the open entry");
		}

		[Test]
		[Arguments("Key = \"fast\"")]
		[Arguments("Optional = true")]
		[Arguments("Deferred = true")]
		public async Task ClosedEntryThatChangesAnOption_DoesNotReport(string option)
		{
			string source = OpenGenericContainer($"""
			                                      [InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Channel))]
			                                      [InjectProperty<Consumer<int>>(nameof(Consumer<int>.Channel), {option})]
			                                      """);

			string[] reports = await Awt179Reports(source);

			await That(reports).IsEmpty()
				.Because("a closed entry that changes an option is a deliberate specialisation of its closing, not a duplicate");
		}

		[Test]
		public async Task ClosedEntryThatDropsTheOpenEntrysKey_DoesNotReport()
		{
			string source = OpenGenericContainer("""
			                                     [InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Channel), Key = "fast")]
			                                     [InjectProperty<Consumer<int>>(nameof(Consumer<int>.Channel))]
			                                     """);

			string[] reports = await Awt179Reports(source);

			await That(reports).IsEmpty()
				.Because("selecting the unkeyed registration on one closing changes the Key, so it is a specialisation too");
		}

		[Test]
		public async Task PragmaAroundTheIgnoredEntry_SuppressesIt()
		{
			string source = OpenGenericContainer("""
			                                     [InjectProperty<Consumer<int>>(nameof(Consumer<int>.Channel))]
			                                     #pragma warning disable AWT179
			                                     [InjectProperty<Consumer<int>>(nameof(Consumer<int>.Channel))]
			                                     #pragma warning restore AWT179
			                                     """);

			string[] reports = await Awt179Reports(source);

			await That(reports).IsEmpty()
				.Because("an analyzer diagnostic carries a source location, so #pragma warning disable silences it");
		}

		[Test]
		public async Task SuppressMessageOnTheContainer_SuppressesIt()
		{
			string source = OpenGenericContainer("""
			                                     [System.Diagnostics.CodeAnalysis.SuppressMessage("Awaiten", "AWT179")]
			                                     [InjectProperty(typeof(Consumer<>), nameof(Consumer<object>.Channel))]
			                                     [InjectProperty<Consumer<int>>(nameof(Consumer<int>.Channel))]
			                                     """);

			string[] reports = await Awt179Reports(source);

			await That(reports).IsEmpty()
				.Because("an analyzer diagnostic carries a source location, so [SuppressMessage] on the container silences it");
		}

		private static async Task<string[]> Awt179Reports(string source)
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(source);
			return diagnostics.Where(diagnostic => diagnostic.Contains("AWT179")).ToArray();
		}

		private static int LineOf(string source, string fragment)
			=> Array.FindIndex(source.Split('\n'), line => line.Contains(fragment)) + 1;

		private static string OpenGenericContainer(string entries)
			=> $$"""
			     using Awaiten;

			     namespace MyCode;

			     public interface IChannel { }
			     public sealed class SlowChannel : IChannel { }
			     public sealed class FastChannel : IChannel { }
			     public sealed class Consumer<T>
			     {
			         public IChannel? Channel { get; set; }
			     }

			     [Container]
			     [Singleton<SlowChannel, IChannel>]
			     [Singleton<FastChannel, IChannel>(Key = "fast")]
			     [Singleton<Consumer<int>>]
			     [Singleton<Consumer<string>>]
			     {{entries}}
			     public static partial class MyContainer;
			     """;
	}
}
