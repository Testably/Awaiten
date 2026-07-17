using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt188ScanInterfaceInaccessible
	{
		[Fact]
		public async Task ReportsWhenTheOnlyConventionInterfaceIsInaccessible()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace Lib;

				public interface IEquipment { }
				internal interface IRoaster { }
				public sealed class Roaster : IRoaster, IEquipment { }
				""", """
				using Awaiten;

				namespace MyCode;

				[Container]
				[Scan(typeof(Lib.IEquipment), As = ScanAs.MatchingInterface, InAssembliesOf = new[] { typeof(Lib.IEquipment) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT188*IRoaster*").AsWildcard()
				.Because("Roaster implements IRoaster, but the internal interface cannot be referenced by the container");
			await That(result.Diagnostics).DoesNotContain("*AWT182*").AsWildcard()
				.Because("claiming the interface is not implemented would mislead; AWT188 names the real cause");
		}

		[Fact]
		public async Task DoesNotReportWhenSelfKeepsTheMatchRegistered()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace Lib;

				public interface IEquipment { }
				internal interface IRoaster { }
				public sealed class Roaster : IRoaster, IEquipment { }
				""", """
				using Awaiten;

				namespace MyCode;

				[Container]
				[Scan(typeof(Lib.IEquipment), As = ScanAs.Self | ScanAs.MatchingInterface, InAssembliesOf = new[] { typeof(Lib.IEquipment) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).IsEmpty()
				.Because("the self registration keeps the match covered, matching the AWT139/AWT182 exemption");
		}
	}
}
