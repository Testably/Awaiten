using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The nested <c>Internals</c> class that holds every member the generator adds to a module or container, so the
///     generated plumbing stays apart from the user's own members.
/// </summary>
public class GeneratedInternalsClassTests
{
	[Test]
	public async Task AModule_HoldsItsGeneratedMembersInAHiddenPublicInternalsClass()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace Lib;

			public interface IGreeter { }
			internal sealed class Greeter : IGreeter { }

			[Module]
			[Singleton<Greeter, IGreeter>]
			public static partial class GreetingModule { }
			""");

		string module = result.Sources.Single(source => source.Key.StartsWith("Awaiten.ModuleScan.", StringComparison.Ordinal)).Value;
		await That(result.Diagnostics).IsEmpty();
		await That(module).Contains("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]*public static class Internals*{*public static global::Lib.IGreeter Registration_Greeter_*() => new global::Lib.Greeter();").AsWildcard()
			.Because("a consuming container in another assembly calls the factory, while IntelliSense hides it");
	}

	[Test]
	public async Task AModuleThatGeneratesOnlyTheScanMarker_HasNoInternalsClass()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace Lib;

			public interface IPlugin { }

			[Module]
			[Scan<IPlugin>]
			public static partial class PluginModule { }
			""");

		await That(result.Sources.Single(source => source.Key.StartsWith("Awaiten.ModuleScan.", StringComparison.Ordinal)).Value).DoesNotContain("Internals");
	}

	[Test]
	public async Task ABaseContainer_HoldsItsGeneratedMembersInAHiddenInternalInternalsClass()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public interface IGrinder { }

			[Container]
			[Singleton<Grinder, IGrinder>]
			public static partial class Shop
			{
			    private sealed class Grinder : IGrinder { }
			}

			[Container(Extends = typeof(Shop))]
			public static partial class TestShop
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]*internal static class Internals*{*internal static global::MyCode.IGrinder Registration_Grinder_*() => new global::MyCode.Shop.Grinder();").AsWildcard()
			.Because("an extending container reaches an internal class through InternalsVisibleTo, and a public one would add an empty type to a public container's API");
		await That(result.Sources["Awaiten.MyCode.TestShop.g.cs"]).Contains("global::MyCode.Shop.Internals.Registration_Grinder_");
	}
}
