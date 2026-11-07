using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt193ScanTypeInaccessible
	{
		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
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

		[Test]
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

		public class PrivateNestedMatches
		{
			[Test]
			public async Task MatchExposedUnderItsMarker_IsRegistered()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IPlugin { }

				                                       [Container]
				                                       [Scan<IPlugin>(As = ScanAs.Marker)]
				                                       public static partial class Shop
				                                       {
				                                           private sealed class FakePlugin : IPlugin { }
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("the resolvers return the accessible marker, and only their bodies, nested in the container, name the private type");
				await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("new global::MyCode.Shop.FakePlugin()");
			}

			[Test]
			public async Task MatchScannedAsSelf_ReportsTheReason()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IPlugin { }

				                                       [Container]
				                                       [Scan<IPlugin>]
				                                       public static partial class Shop
				                                       {
				                                           private sealed class FakePlugin : IPlugin { }
				                                       }
				                                       """);

				await That(result.Diagnostics).Contains("*warning AWT193*'MyCode.Shop.FakePlugin' matched the scan, but 'MyCode.Shop.FakePlugin' is private or protected and the scan registers it as itself, so it is not registered; expose it through ScanAs.Marker or ScanAs.MatchingInterface instead of ScanAs.Self*").AsWildcard()
					.Because("a resolver returning the private type itself would expose it, so the scan skips the match with a warning instead of the AWT216 error");
				await That(result.Diagnostics).DoesNotContain("*error*").AsWildcard();
			}

			[Test]
			public async Task MatchWithoutACommonAccessibleType_ReportsTheReason()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IPlugin { }
				                                       public interface IFakePlugin { }

				                                       [Container]
				                                       [Scan<IPlugin>(As = ScanAs.Marker | ScanAs.MatchingInterface)]
				                                       public static partial class Shop
				                                       {
				                                           private sealed class FakePlugin : IPlugin, IFakePlugin { }
				                                       }
				                                       """);

				await That(result.Diagnostics).Contains("*warning AWT193*'MyCode.Shop.FakePlugin'*no accessible type it converts to covers all of its services*narrow the scan to one interface*").AsWildcard()
					.Because("no accessible type converts to both interfaces, so one resolver cannot serve the shared instance");
				await That(result.Diagnostics).DoesNotContain("*error*").AsWildcard();
			}

			[Test]
			public async Task MatchAlsoRegisteredExplicitly_LeavesTheExplicitRegistration()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IPlugin { }
				                                       public interface IClock { }

				                                       [Container]
				                                       [Singleton<FakeClock, IClock>]
				                                       [Scan<IPlugin>(As = ScanAs.Marker)]
				                                       public static partial class Shop
				                                       {
				                                           private sealed class FakeClock : IClock, IPlugin { }
				                                       }
				                                       """);

				await That(result.Diagnostics).HasCount(1);
				await That(result.Diagnostics).Contains("*warning AWT193*'MyCode.Shop.FakeClock'*no accessible type it converts to covers*").AsWildcard()
					.Because("only the scan turns the registration into one no accessible type covers, so only the scan is dropped");
				await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("new global::MyCode.Shop.FakeClock()")
					.Because("the explicit registration stays exactly what it was without the scan");
			}

			[Test]
			public async Task MarkerPrivateToTheContainer_RegistersAnAccessibleMatch()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       [Container]
				                                       [Scan<IPlugin>(As = ScanAs.Marker)]
				                                       public static partial class Shop
				                                       {
				                                           private interface IPlugin { }

				                                           internal sealed class Plugin : IPlugin { }
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("a private service is fine as long as its implementation is accessible, so the marker is judged from the container, not its assembly");
				await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("new global::MyCode.Shop.Plugin()");
			}

			[Test]
			public async Task ContainerNestedPrivatelyBesideTheMatch_RegistersItAsItself()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IPlugin { }

				                                       public partial class ShopTests
				                                       {
				                                           private sealed class FakePlugin : IPlugin { }

				                                           [Container]
				                                           [Scan<IPlugin>]
				                                           private static partial class Shop
				                                           {
				                                           }
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("the container is no more visible than the private type, so its resolvers may return it");
				await That(result.Sources.Single(source => source.Key.Contains("Shop")).Value).Contains("new global::MyCode.ShopTests.FakePlugin()");
			}

			[Test]
			public async Task MatchPrivateToAnExtendedBase_DoesNotReport()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IPlugin { }

				                                       [Container]
				                                       [Scan<IPlugin>(As = ScanAs.Marker)]
				                                       public static partial class Shop
				                                       {
				                                           private sealed class FakePlugin : IPlugin { }
				                                       }

				                                       [Container(Extends = typeof(Shop))]
				                                       public static partial class TestShop
				                                       {
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("the extending container builds the base's private match through the factory the base generates for it");
				await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("new global::MyCode.Shop.FakePlugin()");
				await That(result.Sources["Awaiten.MyCode.TestShop.g.cs"]).Contains("global::MyCode.Shop.Internals.Registration_FakePlugin_");
			}

			[Test]
			public async Task MatchTheContainerCannotReach_KeepsTheInaccessibleMessage()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IPlugin { }

				                                       public sealed class Host
				                                       {
				                                           private sealed class HiddenPlugin : IPlugin { }
				                                       }

				                                       [Container]
				                                       [Scan<IPlugin>(As = ScanAs.Marker)]
				                                       public static partial class Shop
				                                       {
				                                       }
				                                       """);

				await That(result.Diagnostics).Contains("*warning AWT193*'MyCode.Host.HiddenPlugin'*not accessible to the generated container*").AsWildcard()
					.Because("the type is private to another class, so not even the container's own members can name it");
			}
		}
	}
}
