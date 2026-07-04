using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Generator behavior of modules: a container pulls a module's registrations in with
///     <c>[Import(typeof(Module))]</c>, the module's strong registrations are emitted like the container's
///     own, and the container's own registration overrides an imported overridable <c>Default</c> - so the
///     overridden default is not emitted at all.
/// </summary>
public class ModuleTests
{
	[Fact]
	public async Task Module_ImportsRegistrationsAndTheContainerOverridesOverridableDefaults()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }
		                                       public sealed class AppClock : IClock { }
		                                       public sealed class Logger { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Default = true)]
		                                       [Singleton<Logger>]
		                                       public static class InfrastructureModule { }

		                                       [Container]
		                                       [Import(typeof(InfrastructureModule))]
		                                       [Singleton<AppClock, IClock>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The module contributes Logger; the container's IClock overrides the module's overridable default,
		// so the overridden ModuleClock is not emitted at all.
		await That(source).Contains("global::MyCode.Logger")
			.Because("a module's strong registrations are pulled into the container");
		await That(source).Contains("global::MyCode.AppClock")
			.Because("the container's own registration wins the service");
		await That(source).DoesNotContain("ModuleClock")
			.Because("an overridden default is dropped in full");
	}

	[Fact]
	public async Task Module_ImportedDefault_IsEmittedWhenTheContainerDoesNotOverrideIt()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Default = true)]
		                                       public static class InfrastructureModule { }

		                                       [Container]
		                                       [Import(typeof(InfrastructureModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::MyCode.ModuleClock")
			.Because("an imported default fills the gap when the container provides no IClock of its own");
	}

	[Fact]
	public async Task Module_Default_BeatsScanMatch_WhenTheDefaultImplementationSortsFirstAmongMatches()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class AaModuleClock : IClock { }
		                                       public sealed class ScannedClock : IClock { }

		                                       [Module]
		                                       [Singleton<AaModuleClock, IClock>(Default = true)]
		                                       public static class ClockModule { }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       [Scan(typeof(IClock), As = ScanAs.ImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("__Bucket(typeof(global::MyCode.IClock), static __s => Root.ResolveAaModuleClock(__s.__root)")
			.Because("an explicit overridable default beats a scan match for single resolution");
	}

	[Fact]
	public async Task Module_Default_BeatsScanMatch_WhenTheDefaultImplementationSortsLastAmongMatches()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ScannedClock : IClock { }
		                                       public sealed class ZzModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ZzModuleClock, IClock>(Default = true)]
		                                       public static class ClockModule { }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       [Scan(typeof(IClock), As = ScanAs.ImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("__Bucket(typeof(global::MyCode.IClock), static __s => Root.ResolveZzModuleClock(__s.__root)")
			.Because("the explicit default wins independently of how the scan enumerates its matches");
	}

	[Fact]
	public async Task Module_Default_BeatsScanMatch_WhenTheScanIsDeclaredBeforeTheImport()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ScannedClock : IClock { }
		                                       public sealed class ZzModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ZzModuleClock, IClock>(Default = true)]
		                                       public static class ClockModule { }

		                                       [Container]
		                                       [Scan(typeof(IClock), As = ScanAs.ImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
		                                       [Import(typeof(ClockModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("__Bucket(typeof(global::MyCode.IClock), static __s => Root.ResolveZzModuleClock(__s.__root)")
			.Because("attribute declaration order between [Scan] and [Import] does not affect default-over-scan precedence");
	}

	[Fact]
	public async Task Module_Default_LifetimeWins_OverAScanMatchingTheSameImplementationWithAnotherLifetime()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }

		                                       [Module]
		                                       [Transient<ModuleClock, IClock>(Default = true)]
		                                       public static class ClockModule { }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       [Scan(typeof(IClock), As = ScanAs.ImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a scan yields to an explicit default exactly like it yields to a strong registration, so no AWT142/AWT107 is reported");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("AwaitenRegistration(typeof(global::MyCode.IClock), global::Awaiten.AwaitenLifetime.Transient)")
			.Because("the explicit default's lifetime is kept; the scan's conflicting lifetime yields");
	}

	[Fact]
	public async Task Container_Default_BeatsScanMatch()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ScannedClock : IClock { }
		                                       public sealed class ZzDefaultClock : IClock { }

		                                       [Container]
		                                       [Singleton<ZzDefaultClock, IClock>(Default = true)]
		                                       [Scan(typeof(IClock), As = ScanAs.ImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("__Bucket(typeof(global::MyCode.IClock), static __s => Root.ResolveZzDefaultClock(__s.__root)")
			.Because("Default = true on the container itself also beats a scan match");
	}

	[Fact]
	public async Task Module_ClosedDefault_BeatsOpenGenericExpansion()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IRepo<T> { }
		                                       public sealed class Repo<T> : IRepo<T> { }
		                                       public sealed class Foo { }
		                                       public sealed class CachedRepo : IRepo<Foo> { }
		                                       public sealed class Consumer
		                                       {
		                                           public Consumer(IRepo<Foo> repo) { }
		                                       }

		                                       [Module]
		                                       [Singleton<CachedRepo, IRepo<Foo>>(Default = true)]
		                                       public static class RepositoryModule { }

		                                       [Container]
		                                       [Import(typeof(RepositoryModule))]
		                                       [Singleton(typeof(Repo<>), typeof(IRepo<>))]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("__Bucket(typeof(global::MyCode.IRepo<global::MyCode.Foo>), static __s => Root.ResolveCachedRepo(__s.__root)")
			.Because("an explicit closed default - a deliberate declaration - beats the closed registration the blanket open template expands on demand, like it beats a blanket scan");
	}

	[Fact]
	public async Task Module_DefaultOverriddenByAnEarlierDefault_DoesNotSeedOpenGenericExpansion()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public interface IRepo<T> { }
		                                       public sealed class Repo<T> : IRepo<T> { }
		                                       public sealed class Foo { }
		                                       public sealed class AClock : IClock { }
		                                       public sealed class BClock : IClock
		                                       {
		                                           public BClock(IRepo<Foo> repo) { }
		                                       }

		                                       [Module]
		                                       [Singleton<AClock, IClock>(TryAdd = true)]
		                                       public static class ModuleA { }

		                                       [Module]
		                                       [Singleton<BClock, IClock>(TryAdd = true)]
		                                       public static class ModuleB { }

		                                       [Container]
		                                       [Import(typeof(ModuleA))]
		                                       [Import(typeof(ModuleB))]
		                                       [Singleton(typeof(Repo<>), typeof(IRepo<>))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).DoesNotContain("Repo<global::MyCode.Foo>")
			.Because("a default dropped by an earlier default is not built, so its constructor must not seed open generic expansion either");
	}

	[Fact]
	public async Task Module_OverriddenDefault_DoesNotSeedOpenGenericExpansion()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public interface IRepo<T> { }
		                                       public sealed class Repo<T> : IRepo<T> { }
		                                       public sealed class Foo { }
		                                       public sealed class ModuleClock : IClock
		                                       {
		                                           public ModuleClock(IRepo<Foo> repo) { }
		                                       }
		                                       public sealed class AppClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Default = true)]
		                                       public static class ClockModule { }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       [Singleton<AppClock, IClock>]
		                                       [Singleton(typeof(Repo<>), typeof(IRepo<>))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).DoesNotContain("Repo<global::MyCode.Foo>")
			.Because("a dropped (overridden) default is not built, so its constructor must not synthesize closed registrations either");
	}

	[Fact]
	public async Task Module_SurvivingDefault_StillSeedsOpenGenericExpansion()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public interface IRepo<T> { }
		                                       public sealed class Repo<T> : IRepo<T> { }
		                                       public sealed class Foo { }
		                                       public sealed class ModuleClock : IClock
		                                       {
		                                           public ModuleClock(IRepo<Foo> repo) { }
		                                       }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Default = true)]
		                                       public static class ClockModule { }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       [Singleton(typeof(Repo<>), typeof(IRepo<>))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("Repo<global::MyCode.Foo>")
			.Because("a default that wins its service is built, so its constructor dependencies drive the expansion");
	}

	[Fact]
	public async Task Module_Decorate_WrapsAServiceLikeAContainerDeclaredDecorator()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IService { }
		                                       public sealed class Real : IService { }
		                                       public sealed class LoggingDecorator : IService
		                                       {
		                                           public LoggingDecorator(IService inner) { }
		                                       }

		                                       [Module]
		                                       [Singleton<Real, IService>]
		                                       [Decorate<LoggingDecorator, IService>]
		                                       public static class ServiceModule { }

		                                       [Container]
		                                       [Import(typeof(ServiceModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::MyCode.LoggingDecorator")
			.Because("a module's [Decorate] wraps the service exactly like one declared on the container");
	}

	[Fact]
	public async Task Module_ImportedTwice_ContributesItsDecoratorOnlyOnce()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IService { }
		                                       public sealed class Real : IService { }
		                                       public sealed class LoggingDecorator : IService
		                                       {
		                                           public LoggingDecorator(IService inner) { }
		                                       }

		                                       [Module]
		                                       [Singleton<Real, IService>]
		                                       [Decorate<LoggingDecorator, IService>]
		                                       public static class ServiceModule { }

		                                       [Container]
		                                       [Import(typeof(ServiceModule))]
		                                       [Import(typeof(ServiceModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		int wraps = source.Split(["new global::MyCode.LoggingDecorator("], System.StringSplitOptions.None).Length - 1;
		await That(wraps).IsEqualTo(1)
			.Because("a module imported twice is deduped, so its [Decorate] wraps the service once, not D(D(service))");
	}

	[Fact]
	public async Task Module_Composite_FrontsAServiceLikeAContainerDeclaredComposite()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface INotifier { }
		                                       public sealed class Email : INotifier { }
		                                       public sealed class Sms : INotifier { }
		                                       public sealed class CompositeNotifier : INotifier
		                                       {
		                                           public CompositeNotifier(IEnumerable<INotifier> notifiers) { }
		                                       }

		                                       [Module]
		                                       [Transient<Email, INotifier>]
		                                       [Transient<Sms, INotifier>]
		                                       [Composite<CompositeNotifier, INotifier>]
		                                       public static class NotifierModule { }

		                                       [Container]
		                                       [Import(typeof(NotifierModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::MyCode.CompositeNotifier")
			.Because("a module's [Composite] fronts the service exactly like one declared on the container");
	}

	[Fact]
	public async Task Module_ImportServices_LetsUnresolvedDependenciesFallThroughToTheExternalProvider()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IExternal { }
		                                       public sealed class Consumer
		                                       {
		                                           public Consumer(IExternal external) { }
		                                       }

		                                       [Module]
		                                       [ImportServices]
		                                       [Singleton<Consumer>]
		                                       public static class ExternalModule { }

		                                       [Container]
		                                       [Import(typeof(ExternalModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a module's [ImportServices] lets its registrations' unresolved dependencies fall through instead of raising AWT101");
	}

	[Fact]
	public async Task Module_WithOnlyADecorator_IsNotReportedAsEmpty()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IService { }
		                                       public sealed class Real : IService { }
		                                       public sealed class LoggingDecorator : IService
		                                       {
		                                           public LoggingDecorator(IService inner) { }
		                                       }

		                                       [Module]
		                                       [Decorate<LoggingDecorator, IService>]
		                                       public static class DecoratorModule { }

		                                       [Container]
		                                       [Import(typeof(DecoratorModule))]
		                                       [Singleton<Real, IService>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a [Decorate] is a contribution, so a decorator-only module is not empty (no AWT151)");
	}
	[Fact]
	public async Task Module_KeyedDefault_IsOverriddenOnlyByTheSameKey()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClockA : IClock { }
		                                       public sealed class ModuleClockB : IClock { }
		                                       public sealed class AppClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClockA, IClock>(Default = true, Key = "a")]
		                                       [Singleton<ModuleClockB, IClock>(Default = true, Key = "b")]
		                                       public static class ClockModule { }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       [Singleton<AppClock, IClock>(Key = "a")]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).DoesNotContain("ModuleClockA")
			.Because("the container overrides the module default under the same key, dropping it in full");
		await That(source).Contains("global::MyCode.ModuleClockB")
			.Because("a default under a different key is not overridden");
	}

	[Fact]
	public async Task Container_OwnDefault_YieldsToItsOwnStrongRegistration_RegardlessOfDeclarationOrder()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class DefaultClock : IClock { }
		                                       public sealed class AppClock : IClock { }

		                                       [Container]
		                                       [Singleton<DefaultClock, IClock>(Default = true)]
		                                       [Singleton<AppClock, IClock>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::MyCode.AppClock")
			.Because("Default/TryAdd work on the container itself, yielding to strong registrations even when declared first");
		await That(source).DoesNotContain("DefaultClock")
			.Because("the container's own overridden default is dropped in full, like a module's");
	}

	[Fact]
	public async Task Module_OpenGenericTypeofRegistration_IsImportedAndExpanded()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IRepo<T> { }
		                                       public sealed class Repo<T> : IRepo<T> { }
		                                       public sealed class Order { }
		                                       public sealed class Consumer
		                                       {
		                                           public Consumer(IRepo<Order> repo) { }
		                                       }

		                                       [Module]
		                                       [Singleton(typeof(Repo<>), typeof(IRepo<>))]
		                                       public static class RepositoryModule { }

		                                       [Container]
		                                       [Import(typeof(RepositoryModule))]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("Repo<global::MyCode.Order>")
			.Because("a module's open generic typeof registration is imported and expanded on demand like the container's own");
	}

	[Fact]
	public async Task Module_ImportedTwice_IsHarmless()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>]
		                                       public static class ClockModule { }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       [Import(typeof(ClockModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("re-registering the same implementation coalesces into one instance without conflicts");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.IClock[] { Root.ResolveModuleClock(__s.__root) }")
			.Because("the duplicate import does not duplicate the collection membership either");
	}

	[Fact]
	public async Task Module_WithOwnImport_StillContributesItsRegistrationsDespiteTheError()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Logger { }
		                                       public sealed class SystemClock { }
		                                       public sealed class Consumer
		                                       {
		                                           public Consumer(SystemClock clock) { }
		                                       }

		                                       [Module]
		                                       [Singleton<Logger>]
		                                       public static class LoggingModule { }

		                                       [Module]
		                                       [Import(typeof(LoggingModule))]
		                                       [Singleton<SystemClock>]
		                                       public static class InfrastructureModule { }

		                                       [Container]
		                                       [Import(typeof(InfrastructureModule))]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// The nested import is rejected (AWT150), but the erroring module's own registrations are still
		// imported so they do not additionally cascade as AWT101 missing dependencies.
		await That(result.Diagnostics).Contains("*AWT150*InfrastructureModule*").AsWildcard();
		await That(result.Diagnostics.Any(d => d.Contains("AWT101"))).IsFalse()
			.Because("the module's own registrations are imported despite its rejected nested [Import]");
	}
}
