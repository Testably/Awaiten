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

		[Test]
		public async Task ProviderWhoseScanAlsoMatchesTheImplementation_IsNotTaken()
		{
			const string library = """
				using Awaiten;

				[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Friend")]

				namespace Lib;

				public interface IKettle { }
				public interface IBrewer { }
				internal interface IHeater { }
				internal sealed class Kettle : IKettle, IHeater { }
				internal sealed class Brewer : IBrewer { public Brewer(IHeater heater) { } }

				[Module]
				[Scan<IKettle>(As = ScanAs.Marker)]
				[Singleton<Kettle, IHeater>]
				public static partial class AModule { }

				[Module]
				[Singleton<Brewer, IBrewer>]
				public static partial class BModule { }
				""";
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(library, """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(AModule))]
				[Import(typeof(BModule))]
				public static partial class MyContainer;
				""");

			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.And.Contains("*AWT231*'Lib.Brewer'*").AsWildcard()
				.Because("the providing module builds no handle factory for an implementation its scan matches too, so the other module leaves the brewer to the assemblies it grants InternalsVisibleTo instead of taking a handle nothing serves");
		}

		public class ChainAcrossThreeModules
		{
			private const string Library = """
				using Awaiten;

				namespace Lib;

				public interface IUser { }
				internal interface IKettle { }
				internal sealed class Bean { }
				internal sealed class Kettle : IKettle { public Kettle(Bean bean) { } }
				internal sealed class User : IUser { public User(IKettle kettle) { } }

				[Module]
				[Singleton<Kettle, IKettle>]
				public static partial class AModule { }

				[Module]
				[Singleton<User, IUser>]
				public static partial class BModule { }

				[Module]
				[Singleton<Bean>]
				public static partial class CModule { }

				""";

			[Test]
			public async Task ProviderTakingAServiceOfAThirdModule_DoesNotReportAwt236()
			{
				GeneratorResult result = Generator.Run(Library);

				await That(result.Diagnostics).IsEmpty()
					.Because("the module that registers the kettle builds it from the bean the third module shares, as a container in the assembly resolves it");
			}

			[Test]
			public async Task ProviderTakingAServiceOfAThirdModule_ServesAContainerInAnotherAssembly()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, """
					using Awaiten;
					using Lib;

					namespace MyCode;

					[Container]
					[Import(typeof(AModule))]
					[Import(typeof(BModule))]
					[Import(typeof(CModule))]
					public static partial class MyContainer;
					""");

				await That(result.Diagnostics).IsEmpty();
			}

			[Test]
			public async Task ModulesTakingEachOthersServices_DoNotReportAwt236()
			{
				GeneratorResult result = Generator.Run(Library
					.Replace("internal sealed class Bean { }", "internal sealed class Bean { }\ninternal sealed class Grounds { public Grounds(IKettle kettle) { } }\ninternal sealed class Cup : IUser { public Cup(Grounds grounds) { } }")
					.Replace("[Singleton<Bean>]", "[Singleton<Bean>]\n[Singleton<Grounds>]")
					.Replace("[Singleton<User, IUser>]", "[Singleton<User, IUser>]\n[Singleton<Cup, IUser>]"));

				await That(result.Diagnostics).IsEmpty()
					.Because("the third module takes the kettle back from the first, a cycle between modules but not between services, which each module still builds");
			}
		}

		public class ModulesTakingEachOthersServicesThatOneCannotBuild
		{
			private static string Library(bool buildable, bool grant = false, bool fourth = false, bool cycle = true) => $$"""
				using Awaiten;

				{{(grant ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]" : "")}}

				namespace Lib;

				public interface IUser { }
				internal sealed class Missing { }
				internal interface IKettle { }
				internal interface ISilo { }
				internal interface IBean { }
				internal interface IGrain { }
				internal sealed class Kettle : IKettle { public Kettle(IBean bean) { } }
				internal sealed class Silo : ISilo { public Silo({{(buildable ? "" : "Missing missing")}}) { } }
				internal sealed class Bean : IBean { public Bean({{(fourth ? "IGrain grain" : "ISilo silo")}}) { } }
				internal sealed class Grain : IGrain { public Grain(ISilo silo) { } }
				internal sealed class User : IUser { public User(IKettle kettle) { } }

				[Module]
				{{(cycle ? "[Singleton<Kettle, IKettle>]" : "")}}
				[Singleton<Silo, ISilo>]
				public static partial class AModule { }

				[Module]
				[Singleton<Bean, IBean>]
				public static partial class BModule { }

				[Module]
				[Singleton<User, IUser>]
				public static partial class CModule { }

				[Module]
				{{(fourth ? "[Singleton<Grain, IGrain>]" : "")}}
				{{(cycle ? "" : "[Singleton<Kettle, IKettle>]")}}
				public static partial class DModule { }
				""";

			private static string Container(bool fourth) => $$"""
				[Container]
				[Import(typeof(AModule))]
				[Import(typeof(BModule))]
				[Import(typeof(CModule))]
				{{(fourth ? "[Import(typeof(DModule))]" : "")}}
				public static partial class MyContainer;
				""";

			private static string Consumer(bool fourth) => "using Awaiten;\nusing Lib;\n\nnamespace MyCode;\n\n" + Container(fourth);

			[Test]
			public async Task TheLibrary_ReportsAwt236AsWithoutTheCycle()
			{
				GeneratorResult acyclic = Generator.Run(Library(buildable: false, cycle: false));
				GeneratorResult cyclic = Generator.Run(Library(buildable: false));

				await That(acyclic.Diagnostics).Contains("*AWT236*'Lib.User'*'Lib.IKettle'*registered only by the module 'Lib.DModule'*").AsWildcard();
				await That(cyclic.Diagnostics).Contains("*AWT236*'Lib.User'*'Lib.IKettle'*registered only by the module 'Lib.AModule'*").AsWildcard()
					.Because("the module that registers the kettle cannot build it once the silo it needs through the bean of another module cannot be built, whether the modules form a cycle or not");
			}

			[Test]
			public async Task TheLibraryWithACycleThroughFourModules_ReportsAwt236()
			{
				GeneratorResult result = Generator.Run(Library(buildable: false, fourth: true));

				await That(result.Diagnostics).Contains("*AWT236*'Lib.User'*'Lib.IKettle'*registered only by the module 'Lib.AModule'*").AsWildcard()
					.Because("the cycle from the kettle through the bean and the grain back to the silo of the same module cannot be built either");
			}

			[Test]
			[Arguments(false)]
			[Arguments(true)]
			public async Task ContainerInTheModulesAssembly_ReportsTheMissingDependencyAndAwt236(bool fourth)
			{
				GeneratorResult result = Generator.Run(Library(buildable: false, fourth: fourth) + Container(fourth));

				await That(result.Diagnostics).Contains("*AWT101*'Lib.ISilo'*'Lib.Missing'*").AsWildcard()
					.And.Contains("*AWT236*'Lib.IKettle'*").AsWildcard()
					.Because("the container reports the dependency the silo misses and the module still reports why a container in another assembly could not use it");
			}

			[Test]
			[Arguments(false)]
			[Arguments(true)]
			public async Task ContainerInAnAssemblyGrantedInternalsVisibleTo_ReportsTheMissingDependency(bool fourth)
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(buildable: false, grant: true, fourth: fourth), Consumer(fourth));

				await That(result.Diagnostics).Contains("*AWT101*'Lib.ISilo'*'Lib.Missing'*").AsWildcard()
					.And.DoesNotContain("*Lib.IBean*").AsWildcard();
			}

			[Test]
			[Arguments(false)]
			[Arguments(true)]
			public async Task ACycleEveryModuleCanBuild_ReportsNothing(bool fourth)
			{
				await That(Generator.Run(Library(buildable: true, fourth: fourth)).Diagnostics).IsEmpty()
					.Because("each module of the cycle builds its service through the handles of the others");
				await That(Generator.Run(Library(buildable: true, fourth: fourth) + Container(fourth)).Diagnostics).IsEmpty();
				await That(Generator.RunWithGeneratedReferencedAssembly(Library(buildable: true, fourth: fourth), Consumer(fourth)).Diagnostics).IsEmpty();
				await That(Generator.RunWithGeneratedReferencedAssembly(Library(buildable: true, grant: true, fourth: fourth), Consumer(fourth)).Diagnostics).IsEmpty();
			}
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
				internal sealed class Store3 : IStore { }
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

				await That(result.Diagnostics).DoesNotContain("*AWT236*").AsWildcard()
					.Because("the module that registers the service and can build it shares the handle, as a container in the assembly that imports it resolves the service");
				await That(result.Diagnostics).Contains("*AWT195*'Lib.Store1'*'Lib.Silo'*").AsWildcard()
					.Because("a container in another assembly that imports the module would otherwise drop its registration silently");
			}

			[Test]
			public async Task OneThatCanBuildTheService_ServesAContainerInAnotherAssembly()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library("Store3"), """
					using Awaiten;
					using Lib;

					namespace MyCode;

					[Container]
					[Import(typeof(BModule))]
					[Import(typeof(CModule))]
					public static partial class MyContainer;
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("the imported module that registers the service provides the handle the chef's factory takes, which the module first by name declares");
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
