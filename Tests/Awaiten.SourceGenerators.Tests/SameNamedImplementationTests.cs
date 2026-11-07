namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Two libraries that declare an implementation of the same full name declare two different types, so a container
///     that imports both keeps one instance of each.
/// </summary>
public class SameNamedImplementationTests
{
	private const string Container = "Awaiten.MyCode.MyContainer.g.cs";

	private static string Library(string name, string body, bool grant = false) => $$"""
		using Awaiten;

		{{(grant ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]" : "")}}

		namespace {{name}}
		{
		    public interface I{{name}}Clock { }
		}

		namespace Shared
		{
		    internal sealed class Clock : {{name}}.I{{name}}Clock { }
		}

		namespace {{name}}
		{
		    {{body}}
		}
		""";

	private static string Registering(string name) => $$"""
		[Module]
		[Singleton<Shared.Clock, I{{name}}Clock>]
		public static partial class ClockModule { }
		""";

	private static string Scanning(string name) => $$"""
		[Module]
		[Scan<I{{name}}Clock>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
		public static partial class ClockModule { }
		""";

	private const string Consumer = """
		using Awaiten;

		namespace MyCode;

		[Container]
		[Import(typeof(First.ClockModule))]
		[Import(typeof(Second.ClockModule))]
		public static partial class MyContainer { }
		""";

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RegisteredByModulesOfTwoAssemblies_StayTwoInstances(bool grant)
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssemblies(
			Library("First", Registering("First"), grant), Library("Second", Registering("Second")), Consumer);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources[Container]).Contains("global::Second.ClockModule.").Exactly(1)
			.Because("the second library's clock is a different type, which only its own module's factory can build");
		await That(result.Sources[Container]).Contains(grant ? "new global::Shared.Clock(" : "global::First.ClockModule.").Exactly(1)
			.Because("the first library's clock is built once, by its own module's factory or by the container it grants access");
	}

	[Test]
	public async Task RegisteredAndScannedByModulesOfOneAssembly_ShareOneInstanceThere()
	{
		string modules = $$"""
			{{Registering("Second")}}

			[Module]
			[Scan<ISecondClock>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
			public static partial class ClockScanModule { }
			""";

		GeneratorResult result = Generator.RunWithGeneratedReferencedAssemblies(
			Library("First", Registering("First")), Library("Second", modules), Consumer.Replace("[Container]", "[Container]\n[Import(typeof(Second.ClockScanModule))]"));

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources[Container]).DoesNotContain("Scan_Clock_")
			.Because("the scan match names the second library's clock, which the registration of its module already builds");
		await That(result.Sources[Container]).Contains("global::First.ClockModule.").Exactly(1)
			.And.Contains("global::Second.ClockModule.").Exactly(1);
	}

	[Test]
	public async Task RegisteredByOneModuleAndScannedByTheOther_StayTwoInstances()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssemblies(
			Library("First", Registering("First")), Library("Second", Scanning("Second")), Consumer);

		await That(result.Diagnostics).IsEmpty()
			.Because("the scan match of the second library's clock does not share the first library's instance, so there is nothing to report");
		await That(result.Sources[Container]).Contains("global::First.ClockModule.").Exactly(1)
			.And.Contains("global::Second.ClockModule.").Exactly(1);
	}
}
