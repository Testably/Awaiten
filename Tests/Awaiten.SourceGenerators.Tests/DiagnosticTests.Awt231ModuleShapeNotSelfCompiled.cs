using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt231ModuleShapeNotSelfCompiled
	{
		private static string Consumer(string module) => $$"""
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof({{module}}))]
			public static partial class MyContainer
			{
			}
			""";

		private static string Library(string? friend, string module) => $$"""
			using Awaiten;

			{{(friend is null ? "" : $"[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"{friend}\")]")}}

			namespace Lib;

			public interface IGreeter { }
			internal sealed class Greeter : IGreeter { }

			[Module]
			[Singleton<Greeter, IGreeter>]
			{{module}} { }
			""";

		private const string NotPartial = "public static class GreetingModule";

		private const string Generic = "public static partial class GreetingModule<T>";

		public class LibraryBuild
		{
			[Test]
			public async Task NotPartialWithoutAnInternalsVisibleToGrant_ReportsAwt194()
			{
				GeneratorResult result = Generator.Run(Library(null, NotPartial));

				await That(result.Diagnostics).Contains("*AWT194*GreetingModule*").AsWildcard()
					.Because("no other assembly could ever construct the implementation without the factory the module cannot receive");
			}

			[Test]
			public async Task NotPartialWithAnInternalsVisibleToGrant_RecordsTheSkipInAnInternalType()
			{
				GeneratorResult result = Generator.Run(Library("App", NotPartial));

				await That(result.Diagnostics).IsEmpty()
					.Because("an assembly granted InternalsVisibleTo constructs the implementation itself, as it did before modules generated factories");
				await That(result.Sources.Values.Single()).Contains("internal static class Awaiten__ModuleNotPartial_GreetingModule_")
					.Because("a consumer sees an internal type of another assembly in its metadata, so the record adds no public surface");
			}

			[Test]
			public async Task NotPartialWithAnInternalsVisibleToGrant_KeepsAwt194ForATypePrivateToTheModule()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IGreeter { }

					[Module]
					[Singleton<GreetingModule.Greeter, IGreeter>]
					public static class GreetingModule
					{
					    private sealed class Greeter : IGreeter { }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT194*GreetingModule*").AsWildcard()
					.Because("InternalsVisibleTo does not open a private nested type, so only the module's factory could construct it");
			}

			[Test]
			public async Task GenericWithoutAnInternalsVisibleToGrant_ReportsAwt201()
			{
				GeneratorResult result = Generator.Run(Library(null, Generic));

				await That(result.Diagnostics).Contains("*AWT201*GreetingModule*").AsWildcard();
			}

			[Test]
			public async Task GenericWithAnInternalsVisibleToGrant_ReportsNothing()
			{
				GeneratorResult result = Generator.Run(Library("App", Generic));

				await That(result.Diagnostics).IsEmpty()
					.Because("an assembly granted InternalsVisibleTo constructs the implementation itself, and any other consumer sees from metadata that the module is generic");
				await That(result.Sources).IsEmpty();
			}
		}

		public class CrossAssemblyConsumer
		{
			[Test]
			public async Task NotPartialWithoutInternalsVisibleTo_ReportsAwt231()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library("Lib.Tests", NotPartial), Consumer("GreetingModule"));

				await That(result.Diagnostics).Contains("*AWT231*GreetingModule*'Lib.Greeter'*generated no factory for it because the module, or a class that contains it, is not partial*InternalsVisibleTo*").AsWildcard()
					.Because("the record tells a module the generator skipped from one built without it");
				await That(result.Diagnostics).DoesNotContain("*AWT154*").AsWildcard();
			}

			[Test]
			public async Task NotPartialBuiltWithoutTheGenerator_StillReportsAwt154()
			{
				GeneratorResult result = Generator.RunWithReferencedAssembly(Library("Lib.Tests", NotPartial), Consumer("GreetingModule"));

				await That(result.Diagnostics).Contains("*AWT154*GreetingModule*").AsWildcard();
				await That(result.Diagnostics).DoesNotContain("*AWT231*").AsWildcard();
			}

			[Test]
			public async Task GenericWithoutInternalsVisibleTo_ReportsAwt231()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library("Lib.Tests", Generic), Consumer("GreetingModule<int>"));

				await That(result.Diagnostics).Contains("*AWT231*GreetingModule<int>*'Lib.Greeter'*generated no factory for it because the module is generic*").AsWildcard()
					.Because("a generic module can never carry a factory, which the consumer sees from metadata");
				await That(result.Diagnostics).DoesNotContain("*AWT154*").AsWildcard();
			}

			[Test]
			public async Task WithInternalsVisibleTo_ConstructsDirectly()
			{
				GeneratorResult notPartial = Generator.RunWithGeneratedReferencedAssembly(Library("TestAssembly", NotPartial), Consumer("GreetingModule"));
				GeneratorResult generic = Generator.RunWithGeneratedReferencedAssembly(Library("TestAssembly", Generic), Consumer("GreetingModule<int>"));

				await That(notPartial.Diagnostics).IsEmpty();
				await That(generic.Diagnostics).IsEmpty();
				await That(notPartial.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("new global::Lib.Greeter(");
				await That(generic.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("new global::Lib.Greeter(");
			}
		}
	}
}
