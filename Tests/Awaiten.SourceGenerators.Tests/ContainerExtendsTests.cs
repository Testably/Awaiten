using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Generator behavior of <c>[Container(Extends = typeof(Base))]</c>: the extending container re-checks the whole
///     inherited graph, but reports only the problems it introduces, and its replacements raise none of the
///     collisions the same registrations would raise side by side.
/// </summary>
public class ContainerExtendsTests
{
	public class BaseDiagnostics
	{
		[Test]
		public async Task AProblemOfAnUnchangedBase_IsReportedOnlyOnTheBase()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class Barista { public Barista(IClock clock) { } }

			                                       [Container]
			                                       [Singleton<Barista>]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Count(diagnostic => diagnostic.Contains("AWT101"))).IsEqualTo(1)
				.Because("the missing clock is the base's problem, and the extending container does not repeat it");
		}

		[Test]
		public async Task AProblemOfAnInheritedRegistration_IsReportedOnlyOnTheBase()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public sealed class Cache { }

			                                       [Module]
			                                       [Singleton<Cache>(Factory = nameof(CreateCache))]
			                                       public static class CacheModule
			                                       {
			                                           private static Cache CreateCache() => new();
			                                       }

			                                       [Container]
			                                       [Import(typeof(CacheModule))]
			                                       [Singleton<Clock>(Factory = "Missing")]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Count(diagnostic => diagnostic.Contains("AWT108"))).IsEqualTo(1)
				.Because("the base's missing factory reads the same in the extending container and is reported once");
			await That(result.Diagnostics.Count(diagnostic => diagnostic.Contains("AWT153"))).IsEqualTo(1)
				.Because("a private module member is inaccessible to the base as well, so it is the base's problem and no AWT226");
			await That(result.Diagnostics).DoesNotContain("*AWT226*").AsWildcard();
		}

		[Test]
		public async Task AProblemTheExtendingContainerIntroduces_IsReported()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }
			                                       public sealed class ScopedClock : IClock { }
			                                       public sealed class Barista { public Barista(IClock clock) { } }

			                                       [Container]
			                                       [Singleton<SystemClock, IClock>]
			                                       [Singleton<Barista>]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       [Scoped<ScopedClock, IClock>]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).HasSingle().Matching(diagnostic => diagnostic.Contains("AWT105") && diagnostic.Contains("Barista"))
				.Because("the replacement turns the inherited singleton into a captive of a scoped clock, which the whole graph is validated again for");
		}

		[Test]
		public async Task AWarningOfABaseInAnotherAssembly_IsNotRepeated()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
			                                                             using Awaiten;

			                                                             namespace Production;

			                                                             public interface IClock { }
			                                                             public sealed class ClockA : IClock { }
			                                                             public sealed class ClockB : IClock { }
			                                                             public sealed class Barista { public Barista(IClock clock) { } }

			                                                             [Module]
			                                                             [Singleton<ClockA, IClock>(Fallback = Fallback.Warn)]
			                                                             public static class ModuleA { }

			                                                             [Module]
			                                                             [Singleton<ClockB, IClock>(Fallback = Fallback.Warn)]
			                                                             public static class ModuleB { }

			                                                             [Container]
			                                                             [Import(typeof(ModuleA))]
			                                                             [Import(typeof(ModuleB))]
			                                                             [Singleton<Barista>]
			                                                             public static partial class Shop { }
			                                                             """, """
			                                                                  using Awaiten;

			                                                                  namespace MyCode;

			                                                                  [Container(Extends = typeof(Production.Shop))]
			                                                                  public static partial class TestShop
			                                                                  {
			                                                                  }
			                                                                  """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the ambiguous defaults are the production build's warning, not the test's");
		}

		[Test]
		public async Task AnAnalyzerProblemOfAnUnchangedBase_IsReportedOnlyOnTheBase()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                           using Awaiten;
			                                                           using System;

			                                                           namespace MyCode;

			                                                           public sealed class Tool : IDisposable { public void Dispose() { } }
			                                                           public sealed class Depot { public Depot(Func<Tool> tools) { } }

			                                                           [Container(LifetimeSafety = LifetimeSafety.Loose)]
			                                                           [Transient<Tool>]
			                                                           [Singleton<Depot>]
			                                                           public static partial class Shop
			                                                           {
			                                                           }

			                                                           [Container(Extends = typeof(Shop))]
			                                                           public static partial class TestShop
			                                                           {
			                                                           }
			                                                           """);

			await That(diagnostics.Count(diagnostic => diagnostic.Contains("AWT118"))).IsEqualTo(1);
		}

		[Test]
		public async Task AnAnalyzerProblemTheExtendingContainerIntroduces_IsReported()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                           using Awaiten;
			                                                           using System;

			                                                           namespace MyCode;

			                                                           public interface ITool { }
			                                                           public sealed class Tool : ITool { }
			                                                           public sealed class DisposableTool : ITool, IDisposable { public void Dispose() { } }
			                                                           public sealed class Depot { public Depot(Func<ITool> tools) { } }

			                                                           [Container(LifetimeSafety = LifetimeSafety.Loose)]
			                                                           [Transient<Tool, ITool>]
			                                                           [Singleton<Depot>]
			                                                           public static partial class Shop
			                                                           {
			                                                           }

			                                                           [Container(Extends = typeof(Shop))]
			                                                           [Transient<DisposableTool, ITool>]
			                                                           public static partial class TestShop
			                                                           {
			                                                           }
			                                                           """);

			await That(diagnostics).HasSingle().Matching(diagnostic => diagnostic.Contains("AWT118"))
				.Because("the replacement makes the transient the singleton's Func builds disposable, which the base's clean graph does not report");
		}
	}

	public class Replacement
	{
		[Test]
		public async Task AReplacementRaisesNoCollisionWithTheInheritedRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }
			                                       public sealed class FakeClock : IClock { }
			                                       public sealed class UtcClock : IClock { }

			                                       [Container]
			                                       [Singleton<IClock>(Factory = nameof(CreateClock))]
			                                       [Singleton<SystemClock>]
			                                       [Singleton<FakeClock, IClock>(Key = "spare")]
			                                       public static partial class Shop
			                                       {
			                                           internal static IClock CreateClock() => new SystemClock();
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       [Transient<SystemClock>]
			                                       [Singleton<IClock>(Instance = nameof(Clock))]
			                                       [Singleton<UtcClock, IClock>(Key = "spare")]
			                                       public static partial class TestShop
			                                       {
			                                           public static readonly IClock Clock = new FakeClock();
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a replaced registration is gone before coalescing, so it meets the replacement in no lifetime (AWT107), production (AWT111) or key (AWT117) conflict");
		}

		[Test]
		public async Task ARegistrationAndAnImportServiceReplaceEachOther()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public interface ISettings { }
			                                       public sealed class SystemClock : IClock { }
			                                       public sealed class FakeSettings : ISettings { }
			                                       public sealed class Reader { public Reader(IClock clock, ISettings settings) { } }

			                                       [Container]
			                                       [Singleton<SystemClock, IClock>]
			                                       [ImportService<ISettings>]
			                                       [Singleton<Reader>]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       [ImportService<IClock>]
			                                       [Singleton<FakeSettings, ISettings>]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("each side replaces the other's declaration of the type, so nothing is both host-owned and Awaiten-owned (AWT175)");
		}

		[Test]
		public async Task AnInheritedDeclarationWhoseConsumerWasReplaced_DoesNotWarn()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ILogger { }
			                                       public interface IClock { }
			                                       public sealed class UtcClock : IClock { }
			                                       public sealed class SystemClock : IClock
			                                       {
			                                           public SystemClock(ILogger logger, IClock inner) { }
			                                           public IClock Fallback { get; set; }
			                                       }
			                                       public sealed class FakeClock : IClock { }

			                                       [Container]
			                                       [ImportService<ILogger>]
			                                       [Singleton<SystemClock, IClock>]
			                                       [Singleton<UtcClock, IClock>(WhenInjectedInto = typeof(SystemClock))]
			                                       [InjectProperty<SystemClock>(nameof(SystemClock.Fallback), Optional = true)]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       [Singleton<FakeClock, IClock>]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a contextual binding (AWT167) and an [InjectProperty] (AWT180) that only the replaced production clock used fall away with it");
		}

		[Test]
		public async Task TheBaseWinningOverItsOwnModule_IsNoCrossModuleDuplicate()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }
			                                       public sealed class ModuleClock : IClock { }
			                                       public sealed class Logger { }

			                                       [Module]
			                                       [Singleton<ModuleClock, IClock>]
			                                       [Singleton<Logger>]
			                                       public static class ClockModule { }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       [Singleton<SystemClock, IClock>]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the base wins over its module like any container, which is no collision between two modules (AWT155)");
		}

		[Test]
		public async Task TheGeneratedContainerDoesNotReferenceTheBasesGeneratedCode()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }
			                                       public sealed class FakeClock : IClock { }
			                                       public sealed class Greeter { public Greeter(string text) { } }

			                                       [Container]
			                                       [Singleton<SystemClock, IClock>]
			                                       [Singleton<Greeter>(Factory = nameof(CreateGreeter))]
			                                       public static partial class Shop
			                                       {
			                                           internal static Greeter CreateGreeter() => new("production");
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       [Singleton<FakeClock, IClock>]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			string generated = result.Sources["Awaiten.MyCode.TestShop.g.cs"];

			await That(result.Diagnostics).IsEmpty();
			await That(generated).Contains("global::MyCode.Shop.CreateGreeter()")
				.Because("the inherited factory is the user's own member, called like an imported module's");
			await That(generated).DoesNotContain("Shop.Root").And.DoesNotContain("SystemClock")
				.Because("the extending container has its own Root and never constructs what it replaced");
		}
	}

	public class PrivateImplementations
	{
		[Test]
		public async Task AnExplicitPrivateRegistration_IsBuiltThroughTheBaseFactory()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IBeans { }
			                                       public interface IGrinder { }
			                                       public sealed class Beans : IBeans { }

			                                       [Container]
			                                       [Singleton<Beans, IBeans>]
			                                       [Singleton<Grinder, IGrinder>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class Grinder : IGrinder
			                                           {
			                                               public Grinder(IBeans beans) { }
			                                           }
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the base builds its private implementation for the extending container");
			await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("internal static global::MyCode.IGrinder Awaiten__Registration_Grinder_").And.Contains("=> new global::MyCode.Shop.Grinder(@beans);")
				.Because("the factory sits in the base, which may construct its private type, and returns the accessible service");
			await That(result.Sources["Awaiten.MyCode.TestShop.g.cs"]).Contains("global::MyCode.Shop.Awaiten__Registration_Grinder_").And.DoesNotContain("new global::MyCode.Shop.Grinder")
				.Because("the extending container calls the factory instead of the constructor it cannot reach");
		}

		[Test]
		public async Task APrivateScanMatch_IsBuiltThroughTheBaseFactory()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }

			                                       [Container]
			                                       [Scan<IPlugin>(As = ScanAs.Marker)]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class AuditPlugin : IPlugin { }
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the extending container no longer skips a scan match private to the base");
			await That(result.Sources["Awaiten.MyCode.TestShop.g.cs"]).Contains("global::MyCode.Shop.Awaiten__Registration_AuditPlugin_");
		}

		[Test]
		public async Task ATypePrivateToAClassContainingTheBase_IsBuiltThroughTheBaseFactory()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IGrinder { }

			                                       public static partial class Production
			                                       {
			                                           private sealed class Grinder : IGrinder { }

			                                           [Container]
			                                           [Singleton<Grinder, IGrinder>]
			                                           public static partial class Shop
			                                           {
			                                           }
			                                       }

			                                       [Container(Extends = typeof(Production.Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.TestShop.g.cs"]).Contains("global::MyCode.Production.Shop.Awaiten__Registration_Grinder_")
				.Because("the base may access a type private to a class that contains it, so its factory builds it");
		}

		[Test]
		public async Task PrivateHooks_RunThroughTheBaseWrappers()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IGrinder { }
			                                       public sealed class Tracker { }

			                                       [Container]
			                                       [Singleton<Tracker>]
			                                       [Singleton<Grinder, IGrinder>(OnActivated = nameof(Warm), OnRelease = nameof(Clean))]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class Grinder : IGrinder { }

			                                           private static void Warm(Grinder grinder, Tracker tracker) { }

			                                           private static void Clean(Grinder grinder) { }
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).Contains("internal static void Awaiten__Hook_OnActivated_Grinder_").And.Contains("Warm((global::MyCode.Shop.Grinder)@awaiten__instance, @tracker);")
				.Because("the wrapper takes the accessible service and casts it back inside the base");
			await That(result.Sources["Awaiten.MyCode.TestShop.g.cs"]).Contains("global::MyCode.Shop.Awaiten__Hook_OnActivated_Grinder_").And.Contains("global::MyCode.Shop.Awaiten__Hook_OnRelease_Grinder_");
		}

		[Test]
		public async Task AReplacedPrivateRegistration_CallsNoFactory()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IGrinder { }
			                                       public sealed class FakeGrinder : IGrinder { }

			                                       [Container]
			                                       [Singleton<Grinder, IGrinder>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class Grinder : IGrinder { }
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       [Singleton<FakeGrinder, IGrinder>]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.TestShop.g.cs"]).DoesNotContain("Awaiten__Registration")
				.Because("the replaced registration is gone before the factory would be called");
		}

		[Test]
		public async Task AContainerWithoutPrivateImplementations_GeneratesNoFactory()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IGrinder { }
			                                       public sealed class Grinder : IGrinder { }

			                                       [Container]
			                                       [Singleton<Grinder, IGrinder>]
			                                       public static partial class Shop
			                                       {
			                                       }
			                                       """);

			await That(result.Sources["Awaiten.MyCode.Shop.g.cs"]).DoesNotContain("Awaiten__Registration")
				.Because("only a private implementation needs a factory, so other containers generate the same code as before");
		}

		[Test]
		public async Task AnExtendingContainer_GeneratesNoFactoryForItsOwnPrivateImplementations()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IGrinder { }
			                                       public sealed class Grinder : IGrinder { }

			                                       [Container]
			                                       [Singleton<Grinder, IGrinder>]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       [Singleton<FakeGrinder, IGrinder>]
			                                       public static partial class TestShop
			                                       {
			                                           private sealed class FakeGrinder : IGrinder { }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.TestShop.g.cs"]).DoesNotContain("Awaiten__Registration")
				.Because("extending is one level deep, so no container can extend a container that extends another");
		}

		[Test]
		public async Task ABaseInAnotherAssemblyThatGrantsInternalsVisibleTo_IsBuiltThroughItsFactory()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			                                                                      using Awaiten;
			                                                                      [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TestAssembly")]

			                                                                      namespace Production;

			                                                                      public interface IGrinder { }
			                                                                      public sealed class Tracker { }

			                                                                      [Container]
			                                                                      [Singleton<Tracker>]
			                                                                      [Singleton<Grinder, IGrinder>(OnActivated = nameof(Warm))]
			                                                                      public static partial class Shop
			                                                                      {
			                                                                          private sealed class Grinder : IGrinder, System.IDisposable
			                                                                          {
			                                                                              public void Dispose() { }
			                                                                          }

			                                                                          private static void Warm(Grinder grinder, Tracker tracker) { }
			                                                                      }
			                                                                      """, """
			                                                                           using Awaiten;

			                                                                           namespace MyCode;

			                                                                           [Container(Extends = typeof(Production.Shop))]
			                                                                           public static partial class TestShop
			                                                                           {
			                                                                           }
			                                                                           """);

			await That(result.Diagnostics).IsEmpty()
				.Because("InternalsVisibleTo lets the extending container call the internal factory and hook wrapper of its base");
			await That(result.Sources["Awaiten.MyCode.TestShop.g.cs"]).Contains("global::Production.Shop.Awaiten__Registration_Grinder_").And.Contains("global::Production.Shop.Awaiten__Hook_OnActivated_Grinder_").And.Contains("Dispose")
				.Because("the disposal is read off the private type's metadata, which the factory's return type hides");
		}

		[Test]
		public async Task ABaseInAnotherAssemblyWithoutInternalsVisibleTo_ReportsAwt226()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			                                                                      using Awaiten;

			                                                                      namespace Production;

			                                                                      public interface IGrinder { }
			                                                                      public interface IPlugin { }

			                                                                      [Container]
			                                                                      [Singleton<Grinder, IGrinder>]
			                                                                      [Scan<IPlugin>(As = ScanAs.Marker)]
			                                                                      public static partial class Shop
			                                                                      {
			                                                                          private sealed class Grinder : IGrinder { }

			                                                                          private sealed class AuditPlugin : IPlugin { }
			                                                                      }
			                                                                      """, """
			                                                                           using Awaiten;

			                                                                           namespace MyCode;

			                                                                           [Container(Extends = typeof(Production.Shop))]
			                                                                           public static partial class TestShop
			                                                                           {
			                                                                           }
			                                                                           """);

			await That(result.Diagnostics).Contains("*error AWT226*The type 'Production.Shop.Grinder'*'MyCode.TestShop'*'Production.Shop' builds it through an internal generated factory*grant the assembly of 'MyCode.TestShop' [InternalsVisibleTo]*").AsWildcard()
				.Because("the factory is internal, so the extending assembly needs access to the base's internals");
			await That(result.Diagnostics).Contains("*warning AWT193*'Production.Shop.AuditPlugin'*internal generated factory*InternalsVisibleTo*").AsWildcard()
				.Because("a scan match the extending container cannot build stays a warning");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard();
		}

		[Test]
		public async Task APrivateImplementationTheFactoryCannotMirror_ReportsAwt226WithTheReason()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public interface IGrinder { }

			                                       [Container]
			                                       [Singleton<Grinder, IGrinder>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class Grinder : IGrinder
			                                           {
			                                               [Inject(Optional = true)]
			                                               public IClock? Clock { get; set; }
			                                           }
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).HasSingle().Matching(diagnostic => diagnostic.Contains("AWT226"));
			await That(result.Diagnostics).Contains("*error AWT226*The type 'MyCode.Shop.Grinder'*its property 'Clock' is injected only when its service is registered*make 'MyCode.Shop.Grinder' internal*").AsWildcard()
				.Because("a factory parameter cannot be left out, so it names the reason and the remedy");
		}

		[Test]
		public async Task APrivateCompositeOrOpenGenericOfTheBase_ReportsAwt226()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public interface IRepository<T> { }
			                                       public interface IPlugin { }
			                                       public sealed class SystemClock : IClock { }
			                                       public sealed class Plugin : IPlugin { }
			                                       public sealed class Reader { public Reader(IRepository<string> repository) { } }

			                                       [Container]
			                                       [Singleton<SystemClock, IClock>]
			                                       [Decorate<AuditedClock, IClock>]
			                                       [Singleton<Plugin, IPlugin>]
			                                       [Composite<AllPlugins, IPlugin>]
			                                       [Singleton(typeof(Repository<>), typeof(IRepository<>))]
			                                       [Singleton<Reader>]
			                                       public static partial class Shop
			                                       {
			                                           private sealed class AuditedClock : IClock { public AuditedClock(IClock inner) { } }

			                                           private sealed class AllPlugins : IPlugin { public AllPlugins(IEnumerable<IPlugin> plugins) { } }

			                                           private sealed class Repository<T> : IRepository<T> { }
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AuditedClock*").AsWildcard()
				.Because("the base generates a factory for its private decorator");
			await That(result.Diagnostics).Contains("*error AWT226*The composite 'MyCode.Shop.AllPlugins'*make 'MyCode.Shop.AllPlugins' internal*").AsWildcard();
			await That(result.Diagnostics).Contains("*error AWT226*'MyCode.Shop.Repository<string>'*make 'MyCode.Shop.Repository<T>' internal*").AsWildcard()
				.Because("composites and open generics private to the base are not built through a generated factory yet");
			await That(result.Diagnostics).DoesNotContain("*error CS*").AsWildcard();
		}
	}
}
