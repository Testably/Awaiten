using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Modules compiled into a referenced assembly: their registrations (read from metadata attributes,
///     including generic attributes and named arguments like <c>Default</c>) are imported like source
///     modules, and diagnostics arising from such a module fall back to the container's <c>[Import]</c>
///     location instead of losing their location entirely.
/// </summary>
public class CrossAssemblyModuleTests
{
	private const string ModuleAssemblySource = """
	                                            using Awaiten;

	                                            namespace ModuleAssembly;

	                                            public interface IClock { }
	                                            public sealed class ModuleClock : IClock { }
	                                            public sealed class Logger { }

	                                            [Module]
	                                            [Singleton<ModuleClock, IClock>(Default = true)]
	                                            [Singleton<Logger>]
	                                            public static class InfrastructureModule { }
	                                            """;

	[Fact]
	public async Task CrossAssemblyModule_RegistrationsAreImported()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly(ModuleAssemblySource, """
			using Awaiten;
			using ModuleAssembly;

			namespace MyCode;

			[Container]
			[Import(typeof(InfrastructureModule))]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::ModuleAssembly.Logger")
			.Because("a metadata module's strong registrations are imported like a source module's");
		await That(source).Contains("global::ModuleAssembly.ModuleClock")
			.Because("a metadata module's Default fills the gap when the container does not override it");
	}

	[Fact]
	public async Task CrossAssemblyModule_DefaultIsOverriddenByTheContainer()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly(ModuleAssemblySource, """
			using Awaiten;
			using ModuleAssembly;

			namespace MyCode;

			public sealed class AppClock : IClock { }

			[Container]
			[Import(typeof(InfrastructureModule))]
			[Singleton<AppClock, IClock>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::MyCode.AppClock")
			.Because("the container's own registration wins the service");
		await That(source).DoesNotContain("ModuleClock")
			.Because("the metadata module's overridden Default is dropped in full, exactly like a source module's");
	}

	[Fact]
	public async Task CrossAssemblyModule_DiagnosticsFallBackToTheImportLocation()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly(ModuleAssemblySource, """
			using Awaiten;
			using ModuleAssembly;

			namespace MyCode;

			public sealed class AppClock : IClock { }

			[Module]
			[Singleton<AppClock, IClock>(Default = true)]
			public static class AppModule { }

			[Container]
			[Import(typeof(AppModule))]
			[Import(typeof(InfrastructureModule))]
			public static partial class MyContainer
			{
			}
			""");

		// The metadata module's Default loses to AppModule's, so AWT148 is reported for a registration that
		// has no syntax of its own; it must fall back to the container's [Import] line instead of having no
		// location at all. Diagnostic.ToString() prefixes "(line,col): " only when a location exists.
		await That(result.Diagnostics.Any(d => d.Contains("AWT148"))).IsTrue()
			.Because("two Defaults collide across assemblies with nothing stronger to resolve them");
		await That(result.Diagnostics).Contains("(*,*): *AWT148*").AsWildcard()
			.Because("the metadata registration's diagnostic falls back to the container's [Import] location");
	}

	[Fact]
	public async Task CrossAssemblyModule_FactoryMemberIsResolvedAndEmittedQualified()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly("""
			using Awaiten;

			namespace ModuleAssembly;

			public interface IClock { }
			public sealed class ModuleClock : IClock { }

			[Module]
			[Singleton<ModuleClock, IClock>(Factory = nameof(CreateClock))]
			public static class ClockModule
			{
			    public static ModuleClock CreateClock() => new ModuleClock();
			}
			""", """
			using Awaiten;
			using ModuleAssembly;

			namespace MyCode;

			[Container]
			[Import(typeof(ClockModule))]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::ModuleAssembly.ClockModule.CreateClock(")
			.Because("a metadata module's factory resolves against the module and is emitted qualified");
	}

	[Fact]
	public async Task CrossAssemblyModule_InternalFactory_ReportsAwt108NamingTheModule()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly("""
			using Awaiten;

			namespace ModuleAssembly;

			public interface IClock { }
			public sealed class ModuleClock : IClock { }

			[Module]
			[Singleton<ModuleClock, IClock>(Factory = nameof(CreateClock))]
			public static class ClockModule
			{
			    internal static ModuleClock CreateClock() => new ModuleClock();
			}
			""", """
			using Awaiten;
			using ModuleAssembly;

			namespace MyCode;

			[Container]
			[Import(typeof(ClockModule))]
			public static partial class MyContainer
			{
			}
			""");

		// An internal member of another assembly (without InternalsVisibleTo) is not even imported into the
		// referencing compilation's symbol tables (MetadataImportOptions.Public), so - exactly like the real
		// compiler - the generator cannot see it at all: the honest outcome is AWT108 "no accessible method"
		// naming the module, not AWT153 (which covers members the generator can see but the container cannot
		// call, e.g. a private member of a source module).
		await That(result.Diagnostics).Contains("*AWT108*the module 'ModuleAssembly.ClockModule' has no accessible method 'CreateClock'*").AsWildcard()
			.Because("an invisible cross-assembly internal member is indistinguishable from a missing one");
	}
}
