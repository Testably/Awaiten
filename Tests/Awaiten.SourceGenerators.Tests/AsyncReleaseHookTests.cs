using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     An <c>OnRelease</c> hook returning <c>Task</c> or <c>ValueTask</c> is queued as an asynchronous release, which
///     <c>DisposeAsync</c> awaits and a synchronous <c>Dispose</c> refuses. It does not make its service async-initialized.
/// </summary>
public class AsyncReleaseHookTests
{
	[Test]
	public async Task TheReleaseQueueHoldsAsynchronousReleases()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Terminal { }
		                                       public sealed class Printer { }
		                                       public sealed class Tray { }

		                                       [Container]
		                                       [Singleton<Terminal>(OnRelease = nameof(Disconnect))]
		                                       [Scoped<Printer>(OnRelease = nameof(Cool))]
		                                       [Scoped<Tray>(OnRelease = nameof(Wipe))]
		                                       public static partial class MyContainer
		                                       {
		                                       	private static Task Disconnect(Terminal terminal) => Task.CompletedTask;
		                                       	private static ValueTask Cool(Printer printer) => default;
		                                       	private static void Wipe(Tray tray) { }
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("protected global::System.Collections.Generic.List<object>? __releases;")
			.Because("one queue keeps synchronous and asynchronous releases in creation order");
		await That(source).Contains("new global::System.Func<global::System.Threading.Tasks.Task>(async () => await Disconnect(");
		await That(source).Contains("new global::System.Func<global::System.Threading.Tasks.Task>(async () => await Cool(")
			.Because("a ValueTask hook is awaited inside a Task closure, so the queue holds one asynchronous delegate type");
		await That(source).Contains("new global::System.Action(() => Wipe(");
	}

	[Test]
	public async Task AContainerWithOnlySynchronousReleases_KeepsItsActionQueue()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Tray { }

		                                       [Container]
		                                       [Scoped<Tray>(OnRelease = nameof(Wipe))]
		                                       public static partial class MyContainer
		                                       {
		                                       	private static void Wipe(Tray tray) { }
		                                       }
		                                       """);

		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("protected global::System.Collections.Generic.List<global::System.Action>? __releases;")
			.Because("the generated code of a container without an asynchronous release hook stays unchanged");
	}

	[Test]
	public async Task AnAsynchronousReleaseHook_DoesNotTaintItsService()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Terminal { }
		                                       public sealed class Consumer { public Consumer(Func<Terminal> terminal) { } }

		                                       [Container]
		                                       [Singleton<Terminal>(OnRelease = nameof(Disconnect))]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       	private static Task Disconnect(Terminal terminal) => Task.CompletedTask;
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("an asynchronous release runs at teardown, so the service still resolves synchronously");
	}

	[Test]
	public async Task AModuleEmitsATaskReturningReleaseWrapper()
	{
		(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator("""
			using Awaiten;
			using System.Threading.Tasks;

			namespace Lib;

			public interface ITerminal { }
			internal sealed class Terminal : ITerminal { }

			[Module]
			[Singleton<Terminal, ITerminal>(OnRelease = nameof(Disconnect))]
			public static partial class TerminalModule
			{
			    internal static Task Disconnect(Terminal terminal) => Task.CompletedTask;
			}
			""", [], []);
		string module = run.Results.SelectMany(r => r.GeneratedSources).Single(s => s.HintName.Contains("TerminalModule")).SourceText.ToString();

		await That(module).Contains("public static global::System.Threading.Tasks.Task Awaiten__Hook_OnRelease_Terminal_*(global::Lib.ITerminal @awaiten__instance) => Disconnect((global::Lib.Terminal)@awaiten__instance);").AsWildcard()
			.Because("the wrapper returns the hook's Task for the consumer to await");
	}

	[Test]
	public async Task ACrossAssemblyModuleReleaseHook_IsAwaitedByTheConsumer()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			using Awaiten;
			using System.Threading.Tasks;

			namespace Lib;

			public interface ITerminal { }
			internal sealed class Terminal : ITerminal { }

			[Module]
			[Singleton<Terminal, ITerminal>(OnRelease = nameof(Disconnect))]
			public static partial class TerminalModule
			{
			    internal static Task Disconnect(Terminal terminal) => Task.CompletedTask;
			}
			""", """
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			[Import(typeof(TerminalModule))]
			public static partial class MyContainer;
			""");

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("new global::System.Func<global::System.Threading.Tasks.Task>(async () => await global::Lib.TerminalModule.Awaiten__Hook_OnRelease_Terminal_")
			.Because("the consumer reads the asynchronous release off the wrapper's Task return type");
	}
}
