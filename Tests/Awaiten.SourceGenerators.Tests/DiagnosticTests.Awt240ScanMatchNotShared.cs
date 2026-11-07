namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt240ScanMatchNotShared
	{
		private static string Library(string modules, bool grant) => $$"""
			using Awaiten;

			{{(grant ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]" : "")}}

			namespace Lib;

			public interface IAppliance { }
			public interface IKettle { }
			internal interface IHiddenKettle { }
			internal sealed class Kettle : IAppliance, IKettle, IHiddenKettle { }

			{{modules}}

			[Module]
			[Scan<IAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
			public static partial class ScanModule { }
			""";

		private const string RegisteringModule = """
			[Module]
			[Singleton<Kettle, IKettle>]
			public static partial class KettleModule { }
			""";

		private const string KettleScanModule = """
			[Module]
			[Scan<IKettle>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
			public static partial class KettleModule { }
			""";

		private const string HandleModule = """
			[Module]
			[Singleton<Kettle, IHiddenKettle>]
			public static partial class KettleModule { }
			""";

		private const string Consumer = """
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof(KettleModule))]
			[Import(typeof(ScanModule))]
			public static partial class MyContainer { }
			""";

		[Test]
		public async Task AScanMatchOfAnImplementationAnotherModuleRegistersAsAnotherService_ReportsAwt240()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(RegisteringModule, grant: false), Consumer);

			await That(result.Diagnostics).HasCount(1);
			await That(result.Diagnostics).Contains("*AWT240*'Lib.Kettle' is matched by the [Scan] of the imported module 'Lib.ScanModule' as 'Lib.IAppliance', but this container builds its one instance of 'Lib.Kettle' as 'Lib.IKettle'*grant this assembly InternalsVisibleTo*exclude 'Lib.Kettle' from the scan").AsWildcard()
				.Because("the registration's factory returns IKettle, and the scan's factory would build a second instance of what the modules' own assembly shares");
		}

		[Test]
		public async Task ScanMatchesOfTwoModulesUnderDifferentServices_ReportsAwt240()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(KettleScanModule, grant: false), Consumer);

			await That(result.Diagnostics).HasCount(1);
			await That(result.Diagnostics).Contains("*AWT240*'Lib.Kettle' is matched by the [Scan] of the imported module 'Lib.ScanModule' as 'Lib.IAppliance', but this container builds its one instance of 'Lib.Kettle' as 'Lib.IKettle'*").AsWildcard()
				.Because("the first module's factory builds the shared instance, and its return type does not convert to the second match's service");
		}

		[Test]
		[Arguments(RegisteringModule)]
		[Arguments(KettleScanModule)]
		public async Task AContainerGrantedInternalsVisibleTo_DoesNotReportAwt240(string modules)
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(modules, grant: true), Consumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("the container constructs the implementation itself, once for every registration and scan match of it");
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("new global::Lib.Kettle(").Exactly(1)
				.And.DoesNotContain("Scan_Kettle_");
		}

		[Test]
		public async Task AScanMatchThatSkipUnconstructablePrunes_ReportsOnlyAwt141()
		{
			const string library = """
				using Awaiten;

				namespace Lib;

				public interface IAppliance { }
				public interface IKettle { }
				public interface IWater { }
				internal sealed class Kettle : IAppliance, IKettle
				{
				    public Kettle(IWater water) { }
				}

				[Module]
				[Scan<IKettle>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton, SkipUnconstructable = true)]
				public static partial class KettleModule { }

				[Module]
				[Scan<IAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton, SkipUnconstructable = true)]
				public static partial class ScanModule { }
				""";

			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(library, Consumer);

			await That(result.Diagnostics).HasCount(1);
			await That(result.Diagnostics).Contains("*AWT141*Lib.Kettle*").AsWildcard()
				.Because("the container drops both matches as unconstructable, so neither builds an instance that could fail to be shared");
		}

		[Test]
		public async Task AnOptedInMatchThatDependsOnlyOnTheDroppedMatch_IsSkippedWithAwt141()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(RegisteringModule, grant: false), Consumer.Replace("[Container]", """
				public interface IToaster { }
				public sealed class Toaster : IToaster
				{
				    public Toaster(IAppliance appliance) { }
				}

				[Container]
				[Scan<IToaster>(As = ScanAs.Marker, SkipUnconstructable = true)]
				"""));

			await That(result.Diagnostics).HasCount(2);
			await That(result.Diagnostics).Contains("*AWT240*").AsWildcard()
				.And.Contains("*AWT141*MyCode.Toaster*").AsWildcard()
				.Because("dropping the unshared match leaves nothing to resolve IAppliance, so the opted-in match is skipped instead of failing with AWT101");
		}

		[Test]
		public async Task AScanMatchOfAnImplementationAnotherModuleBuildsThroughAHandle_SharesTheHandle()
		{
			GeneratorResult same = Generator.Run(Library(HandleModule, grant: false) + Consumer.Replace("using Awaiten;", "").Replace("using Lib;", "").Replace("namespace MyCode;", ""));
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(HandleModule, grant: false), Consumer);

			await That(same.Diagnostics).IsEmpty();
			await That(result.Diagnostics).IsEmpty()
				.Because("the scanning module serves the match from the instance handle of the other module through an alias of its own, as a container in the modules' assembly shares one instance");
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("global::Lib.ScanModule.Internals.Alias_Kettle_")
				.And.DoesNotContain("Scan_Kettle_");
		}

		[Test]
		public async Task AScanMatchOfAPublicImplementationAnotherModuleBuildsThroughAHandle_SharesTheHandle()
		{
			const string library = """
				using Awaiten;

				namespace Lib;

				public interface IAppliance { }
				internal interface IHiddenKettle { }
				public sealed class Kettle : IAppliance, IHiddenKettle { }

				[Module]
				[Singleton<Kettle, IHiddenKettle>]
				public static partial class KettleModule { }

				[Module]
				[Scan<IAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
				public static partial class ScanModule { }
				""";
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(library, Consumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("the match serves the handle's instance through an alias, as a container in the modules' assembly shares one instance");
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("Internals.Alias_Kettle_")
				.And.DoesNotContain("new global::Lib.Kettle(")
				.And.DoesNotContain("Scan_Kettle_");
		}

		[Test]
		public async Task AScanMatchOfAnImplementationAnotherModuleBuildsThroughAHandleAndAnAliasOfTheMatchsService_SharesTheAlias()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(HandleModule.Replace("[Singleton<Kettle, IHiddenKettle>]", "[Singleton<Kettle, IHiddenKettle>]\n[Singleton<Kettle, IAppliance>]"), grant: false), Consumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("the match serves the handle's instance through the alias of its service");
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("Internals.Alias_Kettle_").Exactly(2)
				.And.DoesNotContain("Scan_Kettle_")
				.Because("each service of the handle has one alias, and the match shares the alias of its service");
		}

		[Test]
		public async Task ScanMatchesOfTwoModulesUnderOneService_DoNotReportAwt240()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(KettleScanModule.Replace("Scan<IKettle>", "Scan<IAppliance>"), grant: false), Consumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("the first module's factory returns the service both matches share");
		}
	}
}
