namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt236InternalServiceInAnotherModule
	{
		private static string Library(string storage, string coffee, bool grant = false) => $$"""
			using Awaiten;

			{{(grant ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Friend\")]" : "")}}

			namespace Lib;

			public interface IGrinder { }
			internal sealed class BeanStore { }
			internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

			[Module]
			[Singleton<BeanStore>]
			{{storage}}

			[Module]
			{{coffee}}
			""";

		[Test]
		public async Task AModuleThatIsNotPartial_ReportsAwt236()
		{
			GeneratorResult result = Generator.Run(Library("public static class StorageModule { }", "[Singleton<Grinder, IGrinder>]\npublic static partial class CoffeeModule { }"));

			await That(result.Diagnostics).Contains("*AWT236*'Lib.BeanStore'*registered only by the module 'Lib.StorageModule'*register 'Lib.BeanStore' in 'Lib.CoffeeModule' too*").AsWildcard()
				.Because("a module that is not partial cannot declare the handle another module would take");
		}

		[Test]
		public async Task AScanMatchOfAModuleThatIsNotPartial_ReportsAwt236()
		{
			GeneratorResult result = Generator.Run(Library("public static class StorageModule { }", "[Scan<IGrinder>(As = ScanAs.Marker)]\npublic static partial class CoffeeModule { }"));

			await That(result.Diagnostics).Contains("*AWT236*'Lib.BeanStore'*registered only by the module 'Lib.StorageModule'*").AsWildcard()
				.Because("a scan match takes the handle of another module like a registration does");
		}

		[Test]
		public async Task AScanMatchOfAPartialModule_DoesNotReportAwt236()
		{
			GeneratorResult result = Generator.Run(Library("public static partial class StorageModule { }", "[Scan<IGrinder>(As = ScanAs.Marker)]\npublic static partial class CoffeeModule { }"));

			await That(result.Diagnostics).IsEmpty()
				.Because("a scan match takes the handle the other module declares");
		}

		[Test]
		public async Task APartialModuleThatBuildsTheService_DoesNotReportAwt236()
		{
			GeneratorResult result = Generator.Run(Library("public static partial class StorageModule { }", "[Singleton<Grinder, IGrinder>]\npublic static partial class CoffeeModule { }"));

			await That(result.Diagnostics).IsEmpty()
				.Because("the other module declares the handle, which the factory takes");
		}

		[Test]
		public async Task AModuleThatRegistersTheServiceItself_DoesNotReportAwt236()
		{
			GeneratorResult result = Generator.Run(Library("public static class StorageModule { }", "[Singleton<BeanStore>]\n[Singleton<Grinder, IGrinder>]\npublic static partial class CoffeeModule { }"));

			await That(result.Diagnostics).IsEmpty()
				.Because("the module builds the internal service through a handle of its own");
		}

		[Test]
		public async Task UnderAGrant_DoesNotReportAwt236()
		{
			GeneratorResult result = Generator.Run(Library("public static class StorageModule { }", "[Singleton<Grinder, IGrinder>]\npublic static partial class CoffeeModule { }", grant: true));

			await That(result.Diagnostics).DoesNotContain("*AWT236*").AsWildcard()
				.Because("an assembly granted InternalsVisibleTo constructs the implementation itself");
		}

		public class SeveralRegisteringModules
		{
			private static string Library(string first) => $$"""
				using Awaiten;

				namespace Lib;

				public interface IChef { }
				internal interface IStore { }
				internal sealed class Silo { }
				internal sealed class Store1 : IStore { public Store1(Silo silo) { } }
				internal sealed class Store2 : IStore { }
				internal sealed class Chef : IChef { public Chef(IStore store) { } }

				[Module]
				[Singleton<{{first}}, IStore>]
				public static partial class AModule { }

				[Module]
				[Singleton<Store2, IStore>]
				public static partial class BModule { }

				[Module]
				[Singleton<Chef, IChef>]
				public static partial class CModule { }
				""";

			[Test]
			public async Task OneThatCanBuildTheService_DoesNotReportAwt236()
			{
				GeneratorResult result = Generator.Run(Library("Store1") + """

					[Container]
					[Import(typeof(BModule))]
					[Import(typeof(CModule))]
					public static partial class LibContainer;
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("the module that registers the service and can build it shares the handle, as a container in the assembly that imports it resolves the service");
			}

			[Test]
			public async Task OneThatCanBuildTheService_ServesAContainerInAnotherAssembly()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library("Store1"), """
					using Awaiten;
					using Lib;

					namespace MyCode;

					[Container]
					[Import(typeof(BModule))]
					[Import(typeof(CModule))]
					public static partial class MyContainer;
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("the module that can build the service provides the handle the chef's factory takes");
			}

			[Test]
			public async Task NoneThatCanBuildTheService_NamesEveryRegisteringModule()
			{
				GeneratorResult result = Generator.Run(Library("Store1").Replace("[Singleton<Store2, IStore>]", "[Singleton<Store1, IStore>]"));

				await That(result.Diagnostics).Contains("*AWT236*'Lib.IStore'*registered only by the modules 'Lib.AModule', 'Lib.BModule'*").AsWildcard()
					.Because("no module that registers the service can build it, so the message names each of them");
			}
		}
	}
}
