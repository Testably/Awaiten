namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     A module scan match and another registration or scan match of the same implementation share one instance in a
///     container of the module's own assembly. A container in another assembly shares it too, instead of calling the
///     match's generated factory for a second instance.
/// </summary>
public class SharedScanInstanceTests
{
	private const string Container = "Awaiten.MyCode.MyContainer.g.cs";

	private static string Library(string body, string access = "internal", bool grant = false) => $$"""
		using Awaiten;

		{{(grant ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]" : "")}}

		namespace Lib;

		public interface IAppliance { }
		public interface IKettle { }
		{{access}} sealed class Kettle : IAppliance, IKettle { }

		{{body}}
		""";

	private static string Consumer(string attributes) => $$"""
		using Awaiten;
		using Lib;

		namespace MyCode;

		{{attributes}}
		[Container]
		public static partial class MyContainer { }
		""";

	private static string SameAssembly(string library, string attributes) => $$"""
		{{library}}

		{{attributes}}
		[Container]
		public static partial class MyContainer { }
		""";

	private const string ScanningModule = """
		[Module]
		[Scan<IAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
		public static partial class ScanModule { }
		""";

	public class RegisteredThroughAFactoryAndScannedByTheModule
	{
		private const string Module = """
			[Module]
			[Scan<IAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
			[Singleton<Kettle, IKettle>(Factory = nameof(Create))]
			public static partial class KettleModule
			{
			    internal static Kettle Create() => new Kettle();
			}
			""";

		[Test]
		public async Task InTheModulesAssembly_TheScanMatchUsesTheFactory()
		{
			GeneratorResult result = Generator.Run(SameAssembly(Library(Module), "[Import(typeof(KettleModule))]"));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.Lib.MyContainer.g.cs"]).DoesNotContain("new global::Lib.Kettle(")
				.Because("the scan match coalesces with the registration on the implementation, which the factory builds");
		}

		[Test]
		[Arguments("public", false)]
		[Arguments("internal", true)]
		public async Task InAnotherAssembly_TheScanMatchUsesTheFactory(string access, bool grant)
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(Module.Replace("internal static", $"{access} static"), access, grant), Consumer("[Import(typeof(KettleModule))]"));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources[Container]).DoesNotContain("Scan_Kettle_")
				.Because("the scan match names its implementation, so the container shares the factory's instance instead of building a second one");
			await That(result.Sources[Container]).Contains("global::Lib.KettleModule.Create()").Exactly(1);
		}
	}

	public class RegisteredByAnotherModule
	{
		private const string Modules = $$"""
			[Module]
			[Singleton<Kettle, IKettle>]
			public static partial class KettleModule { }

			{{ScanningModule}}
			""";

		private const string Imports = "[Import(typeof(KettleModule))]\n[Import(typeof(ScanModule))]";

		[Test]
		public async Task InTheModulesAssembly_ConstructsOneInstance()
		{
			GeneratorResult result = Generator.Run(SameAssembly(Library(Modules), Imports));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.Lib.MyContainer.g.cs"]).Contains("new global::Lib.Kettle(").Exactly(1);
		}

		[Test]
		[Arguments("public", false)]
		[Arguments("internal", true)]
		public async Task InAnotherAssemblyThatConstructsIt_ConstructsOneInstance(string access, bool grant)
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(Modules, access, grant), Consumer(Imports));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources[Container]).Contains("new global::Lib.Kettle(").Exactly(1)
				.Because("the scan match coalesces with the other module's registration, as in the modules' own assembly");
			await That(result.Sources[Container]).DoesNotContain("Scan_Kettle_");
		}

	}

	public class RegisteredByTheContainer
	{
		[Test]
		public async Task InTheModulesAssembly_ConstructsOneInstance()
		{
			GeneratorResult result = Generator.Run(SameAssembly(Library(ScanningModule), "[Import(typeof(ScanModule))]\n[Singleton<Kettle, IKettle>]"));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.Lib.MyContainer.g.cs"]).Contains("new global::Lib.Kettle(").Exactly(1);
		}

		[Test]
		[Arguments("public", false)]
		[Arguments("internal", true)]
		public async Task InAnotherAssembly_ConstructsOneInstance(string access, bool grant)
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(ScanningModule, access, grant), Consumer("[Import(typeof(ScanModule))]\n[Singleton<Kettle, IKettle>]"));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources[Container]).Contains("new global::Lib.Kettle(").Exactly(1)
				.Because("the module's scan match coalesces with the container's registration");
			await That(result.Sources[Container]).DoesNotContain("Scan_Kettle_");
		}
	}

	public class RegisteredByAnExtendedBase
	{
		private const string Base = $$"""
			{{ScanningModule}}

			[Container]
			[Import(typeof(ScanModule))]
			[Singleton<Kettle, IKettle>]
			public static partial class BaseContainer { }
			""";

		[Test]
		[Arguments("public", false)]
		[Arguments("internal", true)]
		public async Task InAnotherAssembly_ConstructsOneInstance(string access, bool grant)
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(Base, access, grant), """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container(Extends = typeof(BaseContainer))]
				public static partial class MyContainer { }
				""");

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources[Container]).Contains("new global::Lib.Kettle(").Exactly(1)
				.Because("the inherited scan match coalesces with the inherited registration, as in the base itself");
			await That(result.Sources[Container]).DoesNotContain("Scan_Kettle_");
		}
	}

	public class ScannedByTwoModules
	{
		private static string Modules(string secondMarker) => $$"""
			[Module]
			[Scan<IAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
			public static partial class FirstModule { }

			[Module]
			[Scan<{{secondMarker}}>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
			public static partial class SecondModule { }
			""";

		private const string Imports = "[Import(typeof(FirstModule))]\n[Import(typeof(SecondModule))]";

		[Test]
		[Arguments("IAppliance")]
		[Arguments("IKettle")]
		public async Task InTheModulesAssembly_ConstructsOneInstance(string secondMarker)
		{
			GeneratorResult result = Generator.Run(SameAssembly(Library(Modules(secondMarker)), Imports));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.Lib.MyContainer.g.cs"]).Contains("new global::Lib.Kettle(").Exactly(1);
		}

		[Test]
		public async Task UnderOneService_CallsOneFactory()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(Modules("IAppliance")), Consumer(Imports));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources[Container]).Contains("Scan_Kettle_").Exactly(1)
				.Because("both matches share the instance the first module's factory builds");
			await That(result.Sources[Container]).DoesNotContain("SecondModule.Internals.Scan_Kettle_");
		}

		[Test]
		public async Task UnderTwoServicesInAnAssemblyThatConstructsIt_ConstructsOneInstance()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(Modules("IKettle"), grant: true), Consumer(Imports));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources[Container]).Contains("new global::Lib.Kettle(").Exactly(1)
				.Because("neither factory's return type converts to the other's service, so the container constructs the implementation once for both");
			await That(result.Sources[Container]).DoesNotContain("Scan_Kettle_");
		}
	}

	public class ScannedOnce
	{
		[Test]
		public async Task KeepsTheGeneratedFactoryAndItsIdentity()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(ScanningModule), Consumer("[Import(typeof(ScanModule))]"));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources[Container]).Contains("global::Lib.ScanModule.Internals.Scan_Kettle_").Exactly(1)
				.And.DoesNotContain("_kettle")
				.Because("a match no other registration names is built and named exactly as before");
		}
	}
}
