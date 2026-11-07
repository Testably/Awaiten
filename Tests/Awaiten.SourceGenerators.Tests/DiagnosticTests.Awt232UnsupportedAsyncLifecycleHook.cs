namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt232UnsupportedAsyncLifecycleHook
	{
		[Test]
		public async Task ReportsAnActivationHookReturningTaskOfT()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Terminal { }

			                                       [Container]
			                                       [Singleton<Terminal>(OnActivated = nameof(Connect))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static Task<bool> Connect(Terminal terminal) => Task.FromResult(true);
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT232*Connect*Task<bool>*").AsWildcard()
				.Because("the container awaits an activation hook but has nowhere to put its result, so a Task<T> hook is rejected rather than silently discarding the value");
			await That(result.Diagnostics).DoesNotContain("*AWT164*").AsWildcard()
				.Because("the precise AWT232 replaces the generic 'no usable hook' error");
		}

		[Test]
		public async Task ReportsAnActivationHookReturningValueTaskOfT()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Terminal { }

			                                       [Container]
			                                       [Singleton<Terminal>(OnActivated = nameof(Connect))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static ValueTask<int> Connect(Terminal terminal) => default;
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT232*Connect*").AsWildcard()
				.Because("a ValueTask<T> activation hook has a result the container cannot use, like Task<T>");
		}

		[Test]
		public async Task ReportsAReleaseHookReturningTaskOfT()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Terminal { }

			                                       [Container]
			                                       [Singleton<Terminal>(OnRelease = nameof(Disconnect))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static Task<bool> Disconnect(Terminal terminal) => Task.FromResult(true);
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT232*Disconnect*Task<bool>*").AsWildcard()
				.Because("teardown awaits a release hook but has nowhere to put its result, like activation");
		}

		[Test]
		public async Task ReportsAnAsynchronousReleaseHookTakingACancellationToken()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Terminal { }

			                                       [Container]
			                                       [Singleton<Terminal>(OnRelease = nameof(Disconnect))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static Task Disconnect(Terminal terminal, CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT232*Disconnect*CancellationToken*DisposeAsync*").AsWildcard()
				.Because("DisposeAsync has no token to pass, and resolving one from the graph would pass a token that never cancels");
			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the hook is rejected before its parameters are resolved from the graph");
		}

		[Test]
		public async Task ReportsAnAsynchronousReleaseHookTakingACancellationTokenOnAModuleScan()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace Lib;

			                                       public interface IPlugin { }
			                                       public interface IRoaster { }
			                                       internal sealed class Roaster : IPlugin, IRoaster { }

			                                       [Module]
			                                       [Scan<IPlugin>(As = ScanAs.MatchingInterface, OnRelease = nameof(Stop))]
			                                       public static partial class PluginModule
			                                       {
			                                       	internal static ValueTask Stop(Roaster roaster, CancellationToken cancellationToken) => default;
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT232*Stop*CancellationToken*").AsWildcard()
				.Because("the module validates its own hooks, and rejects a token on an asynchronous release hook there too");
		}

		[Test]
		public async Task DoesNotReportForTaskAndValueTaskReleaseHooks()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Terminal { }
			                                       public sealed class Printer { }
			                                       public sealed class Settings { }

			                                       [Container]
			                                       [Singleton<Settings>]
			                                       [Singleton<Terminal>(OnRelease = nameof(Disconnect))]
			                                       [Scoped<Printer>(OnRelease = nameof(Cool))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static Task Disconnect(Terminal terminal, Settings settings) => Task.CompletedTask;
			                                       	private static ValueTask Cool(Printer printer) => default;
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("DisposeAsync awaits Task and ValueTask release hooks");
		}

		[Test]
		public async Task ReportsAnActivationHookReturningTaskOfTOnAModuleRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace Lib;

			                                       public interface ITerminal { }
			                                       internal sealed class Terminal : ITerminal { }

			                                       [Module]
			                                       [Singleton<Terminal, ITerminal>(OnActivated = nameof(Connect))]
			                                       public static partial class TerminalModule
			                                       {
			                                       	internal static Task<int> Connect(Terminal terminal) => Task.FromResult(0);
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT232*Connect*").AsWildcard()
				.Because("a self-compiled module registration validates its hooks like a container does");
		}

		[Test]
		public async Task DoesNotReportForTaskAndValueTaskActivationHooks()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Terminal { }
			                                       public sealed class Printer { }

			                                       [Container]
			                                       [Singleton<Terminal>(OnActivated = nameof(Connect))]
			                                       [Singleton<Printer>(OnActivated = nameof(Warm))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static Task Connect(Terminal terminal, CancellationToken cancellationToken) => Task.CompletedTask;
			                                       	private static ValueTask Warm(Printer printer) => default;
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("Task and ValueTask activation hooks are supported, with or without a CancellationToken");
		}
	}
}
