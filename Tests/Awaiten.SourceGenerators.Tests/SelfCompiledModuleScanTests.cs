using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     A <c>[Module]</c> that compiles its own <c>[Scan]</c>: the module's build emits a factory + registration per
///     match, so a consuming container resolves an <c>internal</c> implementation through its accessible interface
///     without ever naming the implementation. The library keeps implementations internal and exposes only interfaces.
/// </summary>
public class SelfCompiledModuleScanTests
{
	private const string LibrarySource = """
	                                     using Awaiten;

	                                     namespace Lib;

	                                     public interface IClock { }
	                                     public sealed class SystemClock : IClock { }

	                                     public interface IPlugin { }
	                                     public interface IRoaster { }

	                                     internal sealed class Roaster : IPlugin, IRoaster
	                                     {
	                                         public Roaster(IClock clock) { }
	                                     }

	                                     [Module]
	                                     [Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton)]
	                                     public static partial class PluginModule { }
	                                     """;

	[Fact]
	public async Task ModuleSelfCompilesItsScanIntoAFactory()
	{
		(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) =
			Generator.RunGenerator(LibrarySource, [], []);
		string module = run.Results
			.SelectMany(r => r.GeneratedSources)
			.Single(s => s.HintName.Contains("ModuleScan"))
			.SourceText.ToString();

		await That(module).Contains("static partial class PluginModule")
			.Because("the module is re-opened as partial to receive the generated factory");
		await That(module).Contains("global::Awaiten.SingletonAttribute<global::Lib.IRoaster>")
			.Because("the match is registered under its accessible MatchingInterface, as a singleton");
		await That(module).Contains("public static global::Lib.IRoaster Awaiten__Scan_0_Roaster(global::Lib.IClock clock) => new global::Lib.Roaster(clock);")
			.Because("the factory returns the interface but constructs the internal implementation in the library");
		await That(module).Contains("Fallback = global::Awaiten.Fallback.Silent")
			.Because("the generated registration is an overridable default a consumer can replace");
	}

	[Fact]
	public async Task ConsumerResolvesTheInternalImplementationThroughItsInterface()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(LibrarySource, """
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof(PluginModule))]
			[Singleton<SystemClock, IClock>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("the internal implementation resolves through its interface with no missing dependency");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::Lib.PluginModule.Awaiten__Scan_0_Roaster")
			.Because("the container calls the module's generated factory to build the implementation");
		await That(source).DoesNotContain("new global::Lib.Roaster")
			.Because("the consumer never constructs the internal implementation directly");
		await That(result.Diagnostics).DoesNotContain("*AWT154*").AsWildcard()
			.Because("a [Scan] on a module is now supported, not rejected");
	}

	[Fact]
	public async Task ConsumerOverridesTheGeneratedRegistration()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(LibrarySource, """
			using Awaiten;
			using Lib;

			namespace MyCode;

			public sealed class AppRoaster : IRoaster { }

			[Container]
			[Import(typeof(PluginModule))]
			[Singleton<SystemClock, IClock>]
			[Singleton<AppRoaster, IRoaster>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("the container's own IRoaster registration overrides the module's Fallback.Silent default without a conflict");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("global::MyCode.AppRoaster")
			.Because("the container's own registration wins the IRoaster slot");
	}

	[Fact]
	public async Task InternalDisposableImplementationIsDisposedThroughTheRuntimeCheck()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			using System;
			using Awaiten;

			namespace Lib;

			public interface IClock { }
			public sealed class SystemClock : IClock { }
			public interface IPlugin { }
			public interface IRoaster { }

			internal sealed class Roaster : IPlugin, IRoaster, IDisposable
			{
			    public Roaster(IClock clock) { }
			    public void Dispose() { }
			}

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton)]
			public static partial class PluginModule { }
			""", """
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof(PluginModule))]
			[Singleton<SystemClock, IClock>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("global::System.IDisposable")
			.Because("a factory returning an interface tracks disposal behind a runtime IDisposable check, so the internal disposable is disposed");
	}
}
