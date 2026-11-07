namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt234InaccessibleAsyncInitializer
	{
		[Test]
		public async Task ReportsAnInternalConventionOfASelfCompiledRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace Lib;

			                                       internal interface IStartable { Task StartAsync(); }
			                                       public interface ITerminal { }
			                                       internal sealed class Terminal : ITerminal, IStartable { public Task StartAsync() => Task.CompletedTask; }

			                                       [Module]
			                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			                                       [Singleton<Terminal, ITerminal>]
			                                       public static partial class TerminalModule;
			                                       """);

			await That(result.Diagnostics).Contains("*AWT234*Terminal*IStartable.StartAsync*not accessible outside the module's assembly*").AsWildcard()
				.Because("a consumer in another assembly runs the convention through its type, which it cannot name");
		}

		[Test]
		public async Task ReportsAnInternalConventionMethodOfAScanMatch()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace Lib;

			                                       public abstract class Machine { internal virtual Task StartAsync() => Task.CompletedTask; }
			                                       public interface IPlugin { }
			                                       public interface IPrinter { }
			                                       internal sealed class Printer : Machine, IPlugin, IPrinter { }

			                                       [Module]
			                                       [AsyncInitializer<Machine>(nameof(Machine.StartAsync))]
			                                       [Scan<IPlugin>(As = ScanAs.MatchingInterface)]
			                                       public static partial class PrinterModule;
			                                       """);

			await That(result.Diagnostics).Contains("*AWT234*Printer*Machine.StartAsync*the method is not accessible outside the module's assembly*").AsWildcard()
				.Because("a consumer in another assembly calls the convention's method itself, so it must be public");
		}

		[Test]
		public async Task DoesNotReportAConventionOfAModuleWithNothingSelfCompiled()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace Lib;

			                                       internal interface IStartable { Task StartAsync(); }
			                                       internal sealed class Terminal : IStartable { public Task StartAsync() => Task.CompletedTask; }

			                                       [Module]
			                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			                                       [Singleton<Terminal>]
			                                       internal static partial class TerminalModule;

			                                       [Container]
			                                       [Import(typeof(TerminalModule))]
			                                       public static partial class MyContainer;
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("an internal module never reaches another assembly, so its internal convention is callable wherever it is imported");
		}

		[Test]
		public async Task ReportsAnInternalConventionOfAnotherAssemblyAtTheContainer()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
				using Awaiten;
				using System.Threading.Tasks;

				namespace Lib;

				internal interface IStartable { Task StartAsync(); }
				public sealed class Terminal : IStartable { public Task StartAsync() => Task.CompletedTask; }

				[Module]
				[AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
				[Singleton<Terminal>]
				public static class TerminalModule;
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(TerminalModule))]
				public static partial class MyContainer;
				""");

			await That(result.Diagnostics).Contains("*AWT234*Terminal*IStartable.StartAsync*not accessible from the container*").AsWildcard()
				.Because("the container constructs the public terminal itself but cannot cast it to the internal convention type");
		}

		[Test]
		public async Task ASkippedRegistrationNamesTheConventionInAwt231()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(
				AsyncInitializerConventionTests.CrossAssembly.FriendLibrary.Replace("\"TestAssembly\"", "\"SomeFriend\""), """
					using Awaiten;
					using Lib;

					namespace MyCode;

					[Container]
					[Import(typeof(TerminalModule))]
					public static partial class MyContainer;
					""");

			await That(result.Diagnostics).Contains("*AWT231*Terminal*async initializer*IStartable.StartAsync*").AsWildcard()
				.Because("the module left the terminal to the assemblies granted InternalsVisibleTo, and a consumer without access learns why it has no factory");
		}
	}
}
