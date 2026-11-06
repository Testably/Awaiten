namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt226InheritedMemberInaccessible
	{
		private const string ProductionAssembly = """
		                                          using Awaiten;

		                                          namespace Production;

		                                          public interface IClock { }
		                                          public sealed class SystemClock : IClock { }
		                                          public sealed class Report { public Report(IClock clock) { } }
		                                          public interface IPlugin { }
		                                          public sealed class PublicPlugin : IPlugin { }
		                                          internal sealed class InternalPlugin : IPlugin { }
		                                          public sealed class Warmup { internal Warmup() { } }
		                                          public sealed class AuditedClock : IClock { internal AuditedClock(IClock inner) { } }

		                                          [Container]
		                                          [Singleton<IClock>(Factory = nameof(CreateClock))]
		                                          [Singleton<Report>]
		                                          [Scan<IPlugin>(As = ScanAs.Marker)]
		                                          [Singleton<Warmup>(OnActivated = nameof(Warm))]
		                                          [Decorate<AuditedClock, IClock>]
		                                          public static partial class Shop
		                                          {
		                                              internal static IClock CreateClock() => new SystemClock();

		                                              internal static void Warm(Warmup warmup) { }
		                                          }
		                                          """;

		[Test]
		public async Task ReportsEachInternalMemberOfABaseInAnotherAssemblyWithoutInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly(ProductionAssembly, """
			                                                                                using Awaiten;

			                                                                                namespace MyCode;

			                                                                                [Container(Extends = typeof(Production.Shop))]
			                                                                                public static partial class TestShop
			                                                                                {
			                                                                                }
			                                                                                """);

			await That(result.Diagnostics).Contains("*(5,12)*AWT226*The factory 'Production.Shop.CreateClock'*TestShop*Production.Shop*InternalsVisibleTo*").AsWildcard()
				.Because("the internal factory is not even imported into the extending assembly, and the error points at the Extends argument");
			await That(result.Diagnostics).Contains("*AWT226*The scanned type 'Production.InternalPlugin'*").AsWildcard()
				.Because("the test graph would silently lack a scan match the production graph has");
			await That(result.Diagnostics).Contains("*AWT226*The constructor of 'Production.Warmup'*").AsWildcard();
			await That(result.Diagnostics).Contains("*AWT226*The constructor of 'Production.AuditedClock'*").AsWildcard()
				.Because("an inherited decorator is constructed by the extending container too");
			await That(result.Diagnostics).DoesNotContain("*AWT108*").AsWildcard()
				.Because("the hidden factory is one cause, reported once");
			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the consumers of the dropped service do not cascade into missing dependencies");
		}

		[Test]
		public async Task ReportsAnInternalHookOfABaseInAnotherAssembly()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
			                                                             using Awaiten;

			                                                             namespace Production;

			                                                             public sealed class Oven { }

			                                                             [Container]
			                                                             [Singleton<Oven>(OnActivated = nameof(Preheat))]
			                                                             public static partial class Shop
			                                                             {
			                                                                 internal static void Preheat(Oven oven) { }
			                                                             }
			                                                             """, """
			                                                                  using Awaiten;

			                                                                  namespace MyCode;

			                                                                  [Container(Extends = typeof(Production.Shop))]
			                                                                  public static partial class TestShop
			                                                                  {
			                                                                  }
			                                                                  """);

			await That(result.Diagnostics).Contains("*AWT226*The lifecycle hook 'Production.Shop.Preheat'*").AsWildcard();
			await That(result.Diagnostics).DoesNotContain("*AWT164*").AsWildcard();
		}

		[Test]
		public async Task DoesNotReportWhenTheBaseAssemblyGrantsInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly(
				ProductionAssembly.Replace("using Awaiten;", "using Awaiten;\n[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]"),
				"""
				using Awaiten;

				namespace MyCode;

				[Container(Extends = typeof(Production.Shop))]
				public static partial class TestShop
				{
				}
				""");

			await That(result.Diagnostics).IsEmpty()
				.Because("InternalsVisibleTo lets the extending container call every internal member its base names");
		}

		[Test]
		public async Task DoesNotReportAnInaccessibleRegistrationTheExtendingContainerReplaces()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
			                                                             using Awaiten;

			                                                             namespace Production;

			                                                             public interface IClock { }
			                                                             public sealed class SystemClock : IClock { }

			                                                             [Container]
			                                                             [Singleton<IClock>(Factory = nameof(CreateClock))]
			                                                             public static partial class Shop
			                                                             {
			                                                                 internal static IClock CreateClock() => new SystemClock();
			                                                             }
			                                                             """, """
			                                                                  using Awaiten;

			                                                                  namespace MyCode;

			                                                                  public sealed class FakeClock : Production.IClock { }

			                                                                  [Container(Extends = typeof(Production.Shop))]
			                                                                  [Singleton<FakeClock, Production.IClock>]
			                                                                  public static partial class TestShop
			                                                                  {
			                                                                  }
			                                                                  """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a replaced registration is gone before anything inherited is checked");
		}

		[Test]
		public async Task ReportsAPrivateMemberOfABaseInTheSameAssembly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }
			                                       public sealed class Cache { }
			                                       public sealed class Oven { }

			                                       [Container]
			                                       [Singleton<IClock>(Factory = nameof(CreateClock))]
			                                       [Singleton<Cache>(Instance = nameof(SharedCache))]
			                                       [Singleton<Oven>(OnActivated = nameof(Preheat))]
			                                       [Singleton<Hidden>]
			                                       public static partial class Shop
			                                       {
			                                           private static readonly Cache SharedCache = new();

			                                           private static IClock CreateClock() => new SystemClock();

			                                           private static void Preheat(Oven oven) { }

			                                           private sealed class Hidden { }
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*(26,12)*AWT226*The factory 'MyCode.Shop.CreateClock'*TestShop*").AsWildcard()
				.Because("the base's own generated code reaches its private members, the extending container's does not");
			await That(result.Diagnostics).Contains("*AWT226*The instance member 'MyCode.Shop.SharedCache'*").AsWildcard();
			await That(result.Diagnostics).Contains("*AWT226*The lifecycle hook 'MyCode.Shop.Preheat'*").AsWildcard();
			await That(result.Diagnostics).Contains("*AWT226*The type 'MyCode.Shop.Hidden'*").AsWildcard();
			await That(result.Diagnostics).DoesNotContain("*AWT153*").AsWildcard();
		}

		[Test]
		public async Task DoesNotReportInternalMembersOfABaseInTheSameAssembly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Container]
			                                       [Singleton<IClock>(Factory = nameof(CreateClock))]
			                                       public static partial class Shop
			                                       {
			                                           internal static IClock CreateClock() => new SystemClock();
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
		}
	}
}
