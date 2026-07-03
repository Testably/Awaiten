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
		                                       public sealed class InfrastructureModule { }

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
		                                       public sealed class InfrastructureModule { }

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
	public async Task Module_GenericImportForm_ImportsTheSameAsTheTypeofForm()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Logger { }

		                                       [Module]
		                                       [Singleton<Logger>]
		                                       public sealed class InfrastructureModule { }

		                                       [Container]
		                                       [Import<InfrastructureModule>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::MyCode.Logger")
			.Because("[Import<TModule>] pulls in the module's registrations like [Import(typeof(TModule))]");
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
		                                       public sealed class ClockModule { }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       [Scan(typeof(IClock), As = ScanAs.ImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("__Bucket(typeof(global::MyCode.IClock), static __s => __s.ResolveAaModuleClock()")
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
		                                       public sealed class ClockModule { }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       [Scan(typeof(IClock), As = ScanAs.ImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("__Bucket(typeof(global::MyCode.IClock), static __s => __s.ResolveZzModuleClock()")
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
		                                       public sealed class ClockModule { }

		                                       [Container]
		                                       [Scan(typeof(IClock), As = ScanAs.ImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
		                                       [Import(typeof(ClockModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("__Bucket(typeof(global::MyCode.IClock), static __s => __s.ResolveZzModuleClock()")
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
		                                       public sealed class ClockModule { }

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

		await That(source).Contains("__Bucket(typeof(global::MyCode.IClock), static __s => __s.ResolveZzDefaultClock()")
			.Because("Default = true on the container itself also beats a scan match");
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
		                                       public sealed class ClockModule { }

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
		                                       public sealed class ClockModule { }

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
}
