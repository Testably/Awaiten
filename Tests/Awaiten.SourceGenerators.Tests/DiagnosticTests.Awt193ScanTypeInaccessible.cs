using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt193ScanTypeInaccessible
	{
		[Fact]
		public async Task ReportsWhenTheMatchedTypeIsInaccessible()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace Lib;

				public interface IEquipment { }
				internal sealed class Roaster : IEquipment { }
				""", """
				using Awaiten;

				namespace MyCode;

				[Container]
				[Scan(typeof(Lib.IEquipment), InAssembliesOf = new[] { typeof(Lib.IEquipment) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT193*Roaster*").AsWildcard()
				.Because("Roaster is assignable to the marker, but the container cannot name an internal type of another assembly");
			await That(result.Diagnostics).DoesNotContain("*AWT140*").AsWildcard()
				.Because("the assembly does hold a match, so hinting at a missing ProjectReference would mislead");
			await That(result.Diagnostics).DoesNotContain("*AWT138*").AsWildcard()
				.Because("the marker did match a type; the reason nothing registered is the accessibility, which AWT193 names");
		}

		[Fact]
		public async Task ReportsOnlyForTypesTheMarkerMatched()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace Lib;

				public interface IEquipment { }
				public sealed class Grinder : IEquipment { }
				internal sealed class Roaster : IEquipment { }

				// The internal plumbing every real library has, none of it assignable to the marker.
				internal sealed class Cache { }
				internal sealed class Buffer { }
				internal sealed class Formatter { }
				internal sealed class Poller { }
				""", """
				using Awaiten;

				namespace MyCode;

				[Container]
				[Scan(typeof(Lib.IEquipment), InAssembliesOf = new[] { typeof(Lib.IEquipment) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics.Count(diagnostic => diagnostic.Contains("AWT193"))).IsEqualTo(1)
				.Because("only Roaster is both inaccessible and assignable to the marker; the other internal types are none of the scan's business");
			await That(result.Diagnostics).Contains("*AWT193*Roaster*").AsWildcard()
				.Because("Roaster is the one match the container cannot name");
		}

		[Fact]
		public async Task DoesNotReportWhenTheTypeIsAccessible()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace Lib;

				public interface IEquipment { }
				public sealed class Grinder : IEquipment { }
				""", """
				using Awaiten;

				namespace MyCode;

				[Container]
				[Scan(typeof(Lib.IEquipment), InAssembliesOf = new[] { typeof(Lib.IEquipment) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).IsEmpty()
				.Because("the public Grinder registers, so there is nothing to warn about");
		}

		[Fact]
		public async Task DoesNotReportForAnInternalTypeOfTheContainersOwnAssembly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       internal sealed class OrderPlugin : IPlugin { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the generated container lives in the same assembly, so an internal type is perfectly nameable");
		}

		[Fact]
		public async Task DoesNotReportWhenNamePatternsExcludeTheType()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace Lib;

				public interface IEquipment { }
				public sealed class Grinder : IEquipment { }
				internal sealed class RoasterStub : IEquipment { }
				""", """
				using Awaiten;

				namespace MyCode;

				[Container]
				[Scan(typeof(Lib.IEquipment), NamePatterns = new[] { "!*Stub" }, InAssembliesOf = new[] { typeof(Lib.IEquipment) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).DoesNotContain("*AWT193*").AsWildcard()
				.Because("the author already said the stub is not part of the scan, so its accessibility is irrelevant");
			await That(result.Diagnostics).DoesNotContain("*AWT173*").AsWildcard()
				.Because("the exclusion did remove a candidate, so it is not stale");
		}

		[Fact]
		public async Task DoesNotReportWhenNamespacePatternsExcludeTheType()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace Lib.Public
				{
					public interface IEquipment { }
					public sealed class Grinder : IEquipment { }
				}

				namespace Lib.Internals
				{
					internal sealed class Roaster : Lib.Public.IEquipment { }
				}
				""", """
				using Awaiten;

				namespace MyCode;

				[Container]
				[Scan(typeof(Lib.Public.IEquipment), NamespacePatterns = new[] { "Lib.Public" }, InAssembliesOf = new[] { typeof(Lib.Public.IEquipment) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).DoesNotContain("*AWT193*").AsWildcard()
				.Because("the namespace filter never included Lib.Internals, so nothing there was asked for");
		}

		[Fact]
		public async Task ReportsForAMarkerlessScanInsideItsNamespace()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				namespace Lib.Equipment
				{
					public interface IRoaster { }
					internal sealed class Roaster : IRoaster { }
				}

				namespace Lib.Internals
				{
					public interface IPoller { }
					internal sealed class Poller : IPoller { }
				}
				""", """
				using Awaiten;

				namespace MyCode;

				[Container]
				[Scan(As = ScanAs.MatchingInterface, NamespacePatterns = new[] { "Lib.Equipment" }, InAssembliesOf = new[] { typeof(Lib.Equipment.IRoaster) })]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT193*Roaster*").AsWildcard()
				.Because("a markerless scan is narrowed by AWT183 to a namespace the author named, so a type it cannot register there is worth saying out loud");
			await That(result.Diagnostics).DoesNotContain("*AWT193*Poller*").AsWildcard()
				.Because("Lib.Internals is outside the scan's namespace filter");
			await That(result.Diagnostics).Contains("*AWT184*").AsWildcard()
				.Because("the scan did register nothing, and AWT193 saying why does not make that untrue; the pair is deliberate");
		}

		[Fact]
		public async Task DoesNotReportWhenInternalsVisibleToGrantsAccess()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TestAssembly")]

				namespace Lib;

				public interface IEquipment { }
				internal sealed class Roaster : IEquipment { }
				""", """
				using Awaiten;

				namespace MyCode;

				[Container]
				[Scan(typeof(Lib.IEquipment), InAssembliesOf = new[] { typeof(Lib.IEquipment) })]
				public static partial class MyContainer
				{
				}
				""");

			// The diagnostic offers InternalsVisibleTo as a remedy, so pin that taking it actually works. An empty
			// diagnostic set covers the generated source too, so this also proves the container can name Roaster.
			await That(result.Diagnostics).IsEmpty()
				.Because("the grant makes the internal type nameable from the container's assembly, so there is nothing to warn about");
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("new global::Lib.Roaster()")
				.Because("an internal type the container can see is registered like a public one");
		}
	}
}
