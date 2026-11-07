using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt231ModuleScanNotSelfCompiled
	{
		private const string Consumer = """
		                                using Awaiten;
		                                using Lib;

		                                namespace MyCode;

		                                [Container]
		                                [Import(typeof(PluginModule))]
		                                public static partial class MyContainer
		                                {
		                                }
		                                """;

		private const string FriendConsumer = """
		                                      using System.Collections.Generic;
		                                      using Awaiten;
		                                      using Lib;

		                                      namespace MyCode;

		                                      public sealed class UtcClock : IClock { }

		                                      [Container]
		                                      [Import(typeof(PluginModule))]
		                                      [Singleton<Secret>]
		                                      [Singleton<UtcClock, IClock>(Key = "utc")]
		                                      public static partial class MyContainer
		                                      {
		                                      }
		                                      """;

		private static string Library(string? friend) => $$"""
			using Awaiten;

			{{(friend is null ? "" : $"[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"{friend}\")]")}}

			namespace Lib;

			public interface IPlugin { }
			public interface IRoaster { }
			public interface IGrinder { }
			public interface ITimer { }
			public interface IClock { }
			internal sealed class Secret { }

			internal sealed class Roaster : IPlugin, IRoaster
			{
			    public Roaster(Secret secret) { }
			}

			internal sealed class Grinder : IPlugin, IGrinder { }

			internal sealed class Timer : IPlugin, ITimer
			{
			    public Timer([FromKey("utc")] IClock clock) { }
			}

			[Module]
			[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton)]
			[Scan<IPlugin>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
			public static partial class PluginModule { }
			""";

		public class LibraryBuild
		{
			[Test]
			public async Task WithoutAnInternalsVisibleToGrant_ReportsTheErrors()
			{
				GeneratorResult result = Generator.Run(Library(null));

				await That(result.Diagnostics).Contains("*AWT195*Roaster*Secret*").AsWildcard()
					.Because("no other assembly could ever construct the match, so the missing factory is an error in the module's own build");
				await That(result.Diagnostics).Contains("*AWT197*Grinder*").AsWildcard();
				await That(result.Diagnostics).Contains("*AWT200*Timer*clock*[FromKey]*").AsWildcard();
			}

			[Test]
			public async Task WithAnInternalsVisibleToGrant_RecordsEachSkippedMatch()
			{
				GeneratorResult result = Generator.Run(Library("App"));

				await That(result.Diagnostics).IsEmpty()
					.Because("an assembly granted InternalsVisibleTo constructs the matches itself, so the module only records them");
				string source = result.Sources.Values.Single();
				await That(source).Contains("[global::Awaiten.GeneratedScanRegistrationAttribute<global::Lib.IRoaster>(\"Lib.Roaster\", Lifetime = global::Awaiten.Lifetime.Singleton)]")
					.And.Contains("[global::Awaiten.GeneratedScanRegistrationAttribute<global::Lib.IPlugin>(\"Lib.Roaster\", Lifetime = global::Awaiten.Lifetime.Singleton)]")
					.And.Contains("[global::Awaiten.GeneratedScanRegistrationAttribute<global::Lib.IGrinder>(\"Lib.Grinder\", Lifetime = global::Awaiten.Lifetime.Singleton)]")
					.And.Contains("[global::Awaiten.GeneratedScanRegistrationAttribute<global::Lib.IPlugin>(\"Lib.Grinder\", Lifetime = global::Awaiten.Lifetime.Singleton)]")
					.And.Contains("[global::Awaiten.GeneratedScanRegistrationAttribute<global::Lib.ITimer>(\"Lib.Timer\", Lifetime = global::Awaiten.Lifetime.Singleton)]")
					.Because("each skipped match is recorded under every service the scans expose it as, named by its metadata name");
				await That(source).Contains("public const string Awaiten__ScanSkipped_Roaster_")
					.And.Contains("its constructor parameter of type 'Lib.Secret' is not accessible outside the module's assembly")
					.And.Contains("it is exposed under several services (Lib.IGrinder, Lib.IPlugin)")
					.And.Contains("its constructor parameter 'clock' is marked [FromKey]");
				await That(source).DoesNotContain("Awaiten__Scan_")
					.Because("the module generates no factory for a skipped match");
			}

			[Test]
			public async Task KeepsTheErrorOfAMatchPrivateToTheModule()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IPlugin { }
					public interface IClock { }

					[Module]
					[Scan<IPlugin>(As = ScanAs.Marker)]
					public static partial class PluginModule
					{
					    private sealed class Timer : IPlugin
					    {
					        public Timer([FromKey("utc")] IClock clock) { }
					    }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT200*Timer*clock*[FromKey]*").AsWildcard()
					.Because("InternalsVisibleTo does not open a private nested type, so no other assembly could construct it");
			}
		}

		public class CrossAssemblyConsumer
		{
			[Test]
			public async Task WithoutInternalsVisibleTo_ReportsEachSkippedMatchWithItsReason()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library("Lib.Tests"), Consumer);

				await That(result.Diagnostics).Contains("*AWT231*PluginModule*'Lib.Roaster'*generated no factory for it because its constructor parameter of type 'Lib.Secret' is not accessible outside the module's assembly*InternalsVisibleTo*[Scan]*").AsWildcard()
					.Because("the module skipped the match on purpose, and the consumer names why and how to fix it");
				await That(result.Diagnostics).Contains("*AWT231*PluginModule*'Lib.Grinder'*because it is exposed under several services (Lib.IGrinder, Lib.IPlugin)*").AsWildcard();
				await That(result.Diagnostics).Contains("*AWT231*PluginModule*'Lib.Timer'*because its constructor parameter 'clock' is marked [FromKey]*").AsWildcard();
				await That(result.Diagnostics.Where(diagnostic => diagnostic.Contains("AWT231"))).HasCount(3)
					.Because("each match is reported once, even when it is recorded under several services");
				await That(result.Diagnostics).DoesNotContain("*AWT154*").AsWildcard()
					.Because("the module was built with the generator, so its scan was expanded");
			}

			[Test]
			public async Task WithInternalsVisibleTo_RegistersEachSkippedMatchDirectly()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library("TestAssembly"), FriendConsumer);

				await That(result.Diagnostics).IsEmpty()
					.Because("InternalsVisibleTo lets the consumer construct every skipped match itself");
				string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
				await That(source).Contains("new global::Lib.Roaster(")
					.And.Contains("new global::Lib.Grinder(")
					.And.Contains("new global::Lib.Timer(")
					.Because("the consumer constructs a skipped match exactly as the module's scan would have registered it");
			}

			[Test]
			public async Task WithInternalsVisibleTo_RunsTheHookWrapperOfASkippedMatch()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TestAssembly")]

					namespace Lib;

					public interface IPlugin { }
					internal sealed class Secret { }

					internal sealed class Roaster : IPlugin
					{
					    public Roaster(Secret secret) { }
					}

					[Module]
					[Scan<IPlugin>(As = ScanAs.Marker, OnActivated = nameof(Activate))]
					[Singleton<Secret>]
					public static partial class PluginModule
					{
					    internal static void Activate(Roaster roaster) { }
					}
					""", Consumer);

				await That(result.Diagnostics).IsEmpty()
					.Because("the module still emits the public hook wrapper, which takes the service the consumer's instance converts to");
				await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("new global::Lib.Roaster(")
					.And.Contains("global::Lib.PluginModule.Awaiten__ScanHook_OnActivated_Roaster_");
			}
		}
	}
}
