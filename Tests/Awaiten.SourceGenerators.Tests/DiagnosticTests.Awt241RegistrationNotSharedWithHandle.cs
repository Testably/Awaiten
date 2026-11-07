namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt241RegistrationNotSharedWithHandle
	{
		private const string Library = """
			using Awaiten;

			namespace Lib;

			internal interface IStore { }
			public sealed class Store : IStore { }
			public interface IGrinder { }
			internal sealed class Grinder : IGrinder { public Grinder(IStore store) { } }

			[Module]
			[Singleton<Store, IStore>]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }
			""";

		private static string Consumer(string imports, string registration) => $$"""
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			{{imports}}
			{{registration}}
			public static partial class MyContainer
			{
			    internal static void Use(Store store) { }
			}

			[Module]
			[Singleton<Store>(OnActivated = nameof(Use))]
			public static class StoreModule
			{
			    internal static void Use(Store store) { }
			}
			""";

		[Test]
		[Arguments("[Singleton<Store>(OnActivated = nameof(MyContainer.Use))]", "OnActivated")]
		[Arguments("[Singleton<Store>(OnRelease = nameof(MyContainer.Use))]", "OnRelease")]
		[Arguments("[Singleton<Store>(SuppressDisposal = true)]", "SuppressDisposal")]
		public async Task DirectiveOfAnEarlierRegistration_Reports(string registration, string directive)
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, Consumer("[Import(typeof(CoffeeModule))]", registration));

			await That(result.Diagnostics).Contains($"*AWT241*'Lib.Store'*'Lib.CoffeeModule'*{directive}*").AsWildcard()
				.Because("the container's registration comes first, so a container in the module's own assembly applies its directive to the one instance, which the handle cannot");
		}

		[Test]
		public async Task DirectiveOfALaterRegistration_ReportsAwt166Instead()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, Consumer("[Import(typeof(CoffeeModule))]\n[Import(typeof(StoreModule))]", ""));

			await That(result.Diagnostics).Contains("*AWT166*'Lib.Store'*OnActivated*").AsWildcard()
				.And.DoesNotContain("*AWT241*").AsWildcard()
				.Because("the module's registration comes first and a later one sets a directive it does not, as a container in the module's own assembly reports");
		}

		[Test]
		public async Task RegistrationWithoutDirectives_DoesNotReport()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, Consumer("[Import(typeof(CoffeeModule))]", "[Singleton<Store>]"));

			await That(result.Diagnostics).IsEmpty()
				.Because("the registration is served from the handle's instance");
		}
	}
}
