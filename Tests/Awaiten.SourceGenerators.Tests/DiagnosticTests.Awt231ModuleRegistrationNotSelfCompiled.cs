using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt231ModuleRegistrationNotSelfCompiled
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

		private static string Library(string friend) => $$"""
			using Awaiten;

			[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("{{friend}}")]

			namespace Lib;

			public interface IGreeter { }
			public interface IFarewell { }
			public interface IWelcome { }
			public interface IClock { }
			internal interface IFormatter { }
			internal sealed class Formatter : IFormatter { }
			public sealed class SystemClock : IClock { }

			internal sealed class Greeter : IGreeter
			{
			    public Greeter(IFormatter formatter) { }
			}

			internal sealed class Farewell : IFarewell, IWelcome { }

			internal sealed class Welcome : IWelcome
			{
			    public Welcome([FromKey("utc")] IClock clock) { }
			}

			[Module]
			[Singleton<Formatter, IFormatter>]
			[Singleton<SystemClock, IClock>(Key = "utc")]
			[Singleton<Greeter, IGreeter>]
			[Singleton<Farewell, IFarewell>]
			[Singleton<Farewell, IWelcome>(Key = "farewell")]
			[Singleton<Welcome, IWelcome>]
			public static partial class GreetingModule { }
			""";

		[Test]
		public async Task ReportsEachSkippedRegistrationWithItsReason()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library("Lib.Tests"), Consumer);

			await That(result.Diagnostics).Contains("*AWT231*GreetingModule*'Lib.Greeter'*because its constructor parameter of type 'Lib.IFormatter' is not accessible outside the module's assembly*InternalsVisibleTo*public*").AsWildcard()
				.Because("the module skipped the factory on purpose, and the consumer names why and how to fix it");
			await That(result.Diagnostics).Contains("*AWT231*GreetingModule*'Lib.Farewell'*because it is registered under several services (Lib.IFarewell, Lib.IWelcome)*").AsWildcard();
			await That(result.Diagnostics).Contains("*AWT231*GreetingModule*'Lib.Welcome'*because its constructor parameter 'clock' is marked [FromKey]*").AsWildcard();
			await That(result.Diagnostics.Where(diagnostic => diagnostic.Contains("AWT231"))).HasCount(3)
				.Because("each implementation is reported once, even when it has several registrations");
			await That(result.Diagnostics).DoesNotContain("*AWT154*").AsWildcard()
				.Because("the module was built with the generator, so it is not a module that was never expanded");
		}

		[Test]
		public async Task ConsumerGrantedInternalsVisibleTo_ConstructsDirectly()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library("TestAssembly"), Consumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("InternalsVisibleTo lets the consumer construct every skipped implementation itself");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("new global::Lib.Greeter(")
				.And.Contains("new global::Lib.Farewell(")
				.And.Contains("new global::Lib.Welcome(");
		}

		[Test]
		public async Task ModuleBuiltWithoutTheGenerator_StillReportsAwt154()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly(Library("Lib.Tests"), Consumer);

			await That(result.Diagnostics).Contains("*AWT154*GreetingModule*").AsWildcard()
				.Because("without the generator the module carries no record of a skip, so it was never expanded");
			await That(result.Diagnostics).DoesNotContain("*AWT231*").AsWildcard();
		}
	}
}
