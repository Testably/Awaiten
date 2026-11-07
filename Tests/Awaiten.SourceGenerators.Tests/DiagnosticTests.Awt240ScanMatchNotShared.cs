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
			internal sealed class Kettle : IAppliance, IKettle { }

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
		public async Task ScanMatchesOfTwoModulesUnderOneService_DoNotReportAwt240()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library(KettleScanModule.Replace("Scan<IKettle>", "Scan<IAppliance>"), grant: false), Consumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("the first module's factory returns the service both matches share");
		}
	}
}
