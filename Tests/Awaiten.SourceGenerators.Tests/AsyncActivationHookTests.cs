using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     An <c>OnActivated</c> hook returning <c>Task</c> or <c>ValueTask</c> makes its service async-initialized, exactly
///     like implementing <c>IAsyncInitializable</c>, so the generator applies every async-taint rule to it.
/// </summary>
public class AsyncActivationHookTests
{
	private const string Terminal = """
	                                using Awaiten;
	                                using System;
	                                using System.Collections.Generic;
	                                using System.Threading;
	                                using System.Threading.Tasks;

	                                namespace MyCode;

	                                public sealed class Terminal { }
	                                public sealed class Register { public Register(Terminal terminal) { } }

	                                """;

	[Test]
	public async Task AFuncOverTheHookedService_IsAwt119()
	{
		GeneratorResult result = Generator.Run(Terminal + """
		                                                  public sealed class Consumer { public Consumer(Func<Terminal> terminal) { } }

		                                                  [Container]
		                                                  [Singleton<Terminal>(OnActivated = nameof(Connect))]
		                                                  [Singleton<Consumer>]
		                                                  public static partial class MyContainer
		                                                  {
		                                                  	private static Task Connect(Terminal terminal) => Task.CompletedTask;
		                                                  }
		                                                  """);

		await That(result.Diagnostics).Contains("*AWT119*").AsWildcard()
			.Because("the async hook makes the terminal itself an async source, so a synchronous Func over it is AWT119");
	}

	[Test]
	public async Task ALazyOverATransitiveConsumer_IsAwt120()
	{
		GeneratorResult result = Generator.Run(Terminal + """
		                                                  public sealed class Consumer { public Consumer(Lazy<Register> register) { } }

		                                                  [Container]
		                                                  [Singleton<Terminal>(OnActivated = nameof(Connect))]
		                                                  [Singleton<Register>]
		                                                  [Singleton<Consumer>]
		                                                  public static partial class MyContainer
		                                                  {
		                                                  	private static ValueTask Connect(Terminal terminal) => default;
		                                                  }
		                                                  """);

		await That(result.Diagnostics).Contains("*AWT120*Register*Terminal*").AsWildcard()
			.Because("the taint of the async hook propagates to a service that depends on the hooked one");
	}

	[Test]
	public async Task AnEagerSingletonWithAnAsyncHook_IsAwt161()
	{
		GeneratorResult result = Generator.Run(Terminal + """
		                                                  [Container]
		                                                  [Singleton<Terminal>(Eager = true, OnActivated = nameof(Connect))]
		                                                  public static partial class MyContainer
		                                                  {
		                                                  	private static Task Connect(Terminal terminal) => Task.CompletedTask;
		                                                  }
		                                                  """);

		await That(result.Diagnostics).Contains("*AWT161*").AsWildcard()
			.Because("an eager singleton is built synchronously in the constructor, which cannot await the hook");
	}

	[Test]
	public async Task AnEagerSingletonWithAnAsyncHook_UnderSyncAfterInit_Compiles()
	{
		GeneratorResult result = Generator.Run(Terminal + """
		                                                  [Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
		                                                  [Singleton<Terminal>(Eager = true, OnActivated = nameof(Connect))]
		                                                  public static partial class MyContainer
		                                                  {
		                                                  	private static Task Connect(Terminal terminal) => Task.CompletedTask;
		                                                  }
		                                                  """);

		await That(result.Diagnostics).IsEmpty()
			.Because("SyncAfterInit lets the eager build block on the async path, as for an IAsyncInitializable singleton");
	}

	[Test]
	public async Task ASynchronousCollectionOverAHookedMember_IsAwt122()
	{
		GeneratorResult result = Generator.Run(Terminal + """
		                                                  public interface IDevice { }
		                                                  public sealed class Scale : IDevice { }
		                                                  public sealed class Counter { public Counter(IEnumerable<IDevice> devices) { } }

		                                                  [Container]
		                                                  [Singleton<Scale, IDevice>(OnActivated = nameof(Tare))]
		                                                  [Singleton<Counter>]
		                                                  public static partial class MyContainer
		                                                  {
		                                                  	private static Task Tare(Scale scale) => Task.CompletedTask;
		                                                  }
		                                                  """);

		await That(result.Diagnostics).Contains("*AWT122*").AsWildcard()
			.Because("a synchronous collection cannot hold a member whose activation must be awaited");
	}

	[Test]
	public async Task TheHookReceivesTheResolveToken_AtAnyPosition()
	{
		GeneratorResult result = Generator.Run(Terminal + """
		                                                  public sealed class Settings { }

		                                                  [Container]
		                                                  [Singleton<Settings>]
		                                                  [Singleton<Terminal>(OnActivated = nameof(Connect))]
		                                                  public static partial class MyContainer
		                                                  {
		                                                  	private static Task Connect(Terminal terminal, CancellationToken cancellationToken, Settings settings) => Task.CompletedTask;
		                                                  }
		                                                  """);

		await That(result.Diagnostics).IsEmpty()
			.Because("an async hook forwards the resolve-time token into a CancellationToken parameter like an async factory, instead of resolving it from the graph");
		await That(result.Sources.Values.Single(source => source.Contains("class Root"))).Contains("await Connect(created, cancellationToken, ").AsWildcard()
			.Because("the generated resolver awaits the hook and passes the token it was called with");
	}

	[Test]
	public async Task ASynchronousHookWithACancellationToken_StillResolvesItFromTheGraph()
	{
		GeneratorResult result = Generator.Run(Terminal + """
		                                                  [Container]
		                                                  [Singleton<Terminal>(OnActivated = nameof(Connect))]
		                                                  public static partial class MyContainer
		                                                  {
		                                                  	private static void Connect(Terminal terminal, CancellationToken cancellationToken) { }
		                                                  }
		                                                  """);

		await That(result.Diagnostics).Contains("*AWT101*CancellationToken*").AsWildcard()
			.Because("a synchronous hook runs where no resolve-time token exists, so its CancellationToken stays an ordinary dependency");
	}

	[Test]
	public async Task AModuleEmitsATaskReturningWrapperThatForwardsTheToken()
	{
		(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator("""
			using Awaiten;
			using System.Threading;
			using System.Threading.Tasks;

			namespace Lib;

			public interface ITerminal { }
			internal sealed class Terminal : ITerminal { }

			[Module]
			[Singleton<Terminal, ITerminal>(OnActivated = nameof(Connect))]
			public static partial class TerminalModule
			{
			    internal static Task Connect(Terminal terminal, CancellationToken cancellationToken) => Task.CompletedTask;
			}
			""", [], []);
		string module = run.Results.SelectMany(r => r.GeneratedSources).Single(s => s.HintName.Contains("TerminalModule")).SourceText.ToString();

		await That(module).Contains("public static global::System.Threading.Tasks.Task Awaiten__Hook_OnActivated_Terminal_*(global::Lib.ITerminal @awaiten__instance, global::System.Threading.CancellationToken @cancellationToken) => Connect((global::Lib.Terminal)@awaiten__instance, @cancellationToken);").AsWildcard()
			.Because("the wrapper returns the hook's Task and forwards the token, so a consumer awaits it with its own resolve-time token");
	}

	[Test]
	public async Task ACrossAssemblyModuleHook_IsAsyncForTheConsumer()
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			using Awaiten;
			using System.Threading;
			using System.Threading.Tasks;

			namespace Lib;

			public interface ITerminal { }
			internal sealed class Terminal : ITerminal { }

			[Module]
			[Singleton<Terminal, ITerminal>(OnActivated = nameof(Connect))]
			public static partial class TerminalModule
			{
			    internal static Task Connect(Terminal terminal, CancellationToken cancellationToken) => Task.CompletedTask;
			}
			""", """
			using Awaiten;
			using Lib;
			using System;

			namespace MyCode;

			public sealed class Consumer { public Consumer(Func<ITerminal> terminal) { } }

			[Container]
			[Import(typeof(TerminalModule))]
			[Singleton<Consumer>]
			public static partial class MyContainer;
			""");

		await That(result.Diagnostics).Contains("*AWT119*").AsWildcard()
			.Because("the consumer reads the asynchronous activation off the wrapper's Task return type, without any extra metadata");
	}
}
