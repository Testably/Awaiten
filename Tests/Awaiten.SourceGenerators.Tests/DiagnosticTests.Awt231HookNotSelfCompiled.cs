using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt231HookNotSelfCompiled
	{
		private const string Consumer = """
		                                using Awaiten;
		                                using Lib;

		                                namespace MyCode;

		                                [Container]
		                                [Import(typeof(GreetingModule))]
		                                public static partial class MyContainer
		                                {
		                                }
		                                """;

		private const string FriendConsumer = """
		                                      using Awaiten;
		                                      using Lib;

		                                      namespace MyCode;

		                                      public sealed class AuditLog : ILog { }

		                                      [Container]
		                                      [Import(typeof(GreetingModule))]
		                                      [Singleton<AuditLog, ILog>(Key = "audit")]
		                                      public static partial class MyContainer
		                                      {
		                                      }
		                                      """;

		private static string Library(string? friend) => $$"""
			using Awaiten;

			{{(friend is null ? "" : $"[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"{friend}\")]")}}

			namespace Lib;

			public interface IGreeter { }
			public interface IPlugin { }
			public interface ILog { }
			internal sealed class Log { }
			internal sealed class Greeter : IGreeter { }
			internal sealed class Roaster : IPlugin { }

			[Module]
			[Singleton<Log>]
			[Singleton<Greeter, IGreeter>(OnActivated = nameof(Activate))]
			[Scan<IPlugin>(As = ScanAs.Marker, OnRelease = nameof(Release))]
			public static partial class GreetingModule
			{
			    internal static void Activate(Greeter greeter, Log log) { }

			    internal static void Release(Roaster roaster, [FromKey("audit")] ILog log) { }
			}
			""";

		public class LibraryBuild
		{
			[Test]
			public async Task WithoutAnInternalsVisibleToGrant_ReportsTheErrors()
			{
				GeneratorResult result = Generator.Run(Library(null));

				await That(result.Diagnostics).Contains("*AWT203*Lib.Greeter*OnActivated*Lib.Log*").AsWildcard()
					.Because("no other assembly could ever call the hook, so the missing wrapper is an error in the module's own build");
				await That(result.Diagnostics).Contains("*AWT204*Lib.Roaster*OnRelease*log*[FromKey]*").AsWildcard();
			}

			[Test]
			public async Task WithAnInternalsVisibleToGrant_RecordsEachSkippedWrapper()
			{
				GeneratorResult result = Generator.Run(Library("App"));

				await That(result.Diagnostics).IsEmpty()
					.Because("an assembly granted InternalsVisibleTo calls the hooks directly, so the module only records them");
				string source = result.Sources.Values.Single();
				await That(source).Contains("public const string HookSkipped_OnActivated_Greeter_")
					.And.Contains("its OnActivated hook's parameter of type 'Lib.Log' is not accessible outside the module's assembly")
					.And.Contains("Registration_Greeter_")
					.And.DoesNotContain("Hook_OnActivated_Greeter_")
					.Because("the registration keeps its factory and loses only the wrapper of the hook it cannot mirror");
				await That(source).Contains("[global::Awaiten.GeneratedScanRegistrationAttribute<global::Lib.IPlugin>(\"Lib.Roaster\", Lifetime = global::Awaiten.Lifetime.Transient, OnRelease = \"Release\")]")
					.And.Contains("its OnRelease hook's parameter 'log' is marked [FromKey]")
					.And.DoesNotContain("ScanHook_")
					.Because("a scan match is skipped as a whole, since a consumer calls the hook on the implementation it constructs itself");
			}

			[Test]
			public async Task KeepsTheErrorOfAGenericHook()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IHandler<T> { }
					internal sealed class Log { }
					internal sealed class OrderHandler : IHandler<int> { }

					[Module]
					[Singleton<Log>]
					[Scan(typeof(IHandler<>), As = ScanAs.Marker, OnActivated = nameof(Activate))]
					public static partial class HandlerModule
					{
					    internal static void Activate<T>(IHandler<T> handler, Log log) { }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT203*OrderHandler*OnActivated*Lib.Log*").AsWildcard()
					.Because("a consumer calling the hook directly could not close its type argument");
			}

			[Test]
			public async Task KeepsTheErrorOfAnArgParameter()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IGreeter { }
					internal sealed class Log { }
					internal sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Log>]
					[Singleton<Greeter, IGreeter>(OnActivated = nameof(Activate))]
					public static partial class GreetingModule
					{
					    internal static void Activate(Greeter greeter, Log log, [Arg] string name) { }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT203*Lib.Greeter*OnActivated*Lib.Log*").AsWildcard()
					.Because("a direct call could not supply the [Arg] either, so the module's build still fails");
			}
		}

		public class CrossAssemblyConsumer
		{
			[Test]
			public async Task WithoutInternalsVisibleTo_ReportsEachSkippedWrapperWithItsReason()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library("Lib.Tests"), Consumer);

				await That(result.Diagnostics).Contains("*AWT231*GreetingModule*'Lib.Greeter'*generated no OnActivated hook wrapper for it because its OnActivated hook's parameter of type 'Lib.Log' is not accessible outside the module's assembly*InternalsVisibleTo*its OnActivated hook public*").AsWildcard()
					.Because("a container that calls the factory cannot run the hook without its wrapper");
				await That(result.Diagnostics).Contains("*AWT231*GreetingModule*'Lib.Roaster'*generated no factory for it because its OnRelease hook's parameter 'log' is marked [FromKey]*").AsWildcard();
				await That(result.Diagnostics.Where(diagnostic => diagnostic.Contains("AWT231"))).HasCount(2);
			}

			[Test]
			public async Task WithInternalsVisibleTo_CallsEachHookDirectly()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library("TestAssembly"), FriendConsumer);

				await That(result.Diagnostics).IsEmpty()
					.Because("InternalsVisibleTo lets the consumer construct the implementations and call their hooks itself");
				string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
				await That(source).Contains("global::Lib.GreetingModule.Activate(")
					.And.Contains("global::Lib.GreetingModule.Release(")
					.And.Contains("new global::Lib.Roaster(");
			}
		}
	}
}
