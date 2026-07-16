using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	// AWT118 is reported by AwaitenAnalyzer (not the generator) so it can be suppressed in source, so these tests drive the analyzer. LifetimeSafetyTests covers the strict-error vs loose-warning escalation.
	public class Awt118RootAccumulatingFactory
	{
		[Fact]
		public async Task ReportsWhenASingletonHoldsAFuncOverADisposableTransient()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool : IDisposable { public void Dispose() { } }
			                                       public sealed class Depot { public Depot(Func<Tool> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsTrue()
				.Because("a singleton holding a Func over a disposable transient accumulates instances on the root");
		}

		[Fact]
		public async Task ReportsForADisposableParameterizedServiceReachedThroughAFunc()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool : IDisposable { public Tool([Arg] int id) { } public void Dispose() { } }
			                                       public sealed class Depot { public Depot(Func<int, Tool> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsTrue()
				.Because("a disposable parameterized service built through a singleton-held Func accumulates on the root");
		}

		[Fact]
		public async Task DoesNotReportWhenTheFactoryHandsBackAnOwnedHandle()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool : IDisposable { public void Dispose() { } }
			                                       public sealed class Depot { public Depot(Func<Owned<Tool>> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsFalse()
				.Because("Func<Owned<T>> hands each instance back as a disposal handle, so nothing accumulates");
		}

		[Fact]
		public async Task DoesNotReportForADisposableTransientThatIsNotReachedThroughARootBoundFunc()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool : IDisposable { public void Dispose() { } }
			                                       public sealed class ScopedConsumer { public ScopedConsumer(Func<Tool> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Scoped<ScopedConsumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsFalse()
				.Because("a Func held by a scoped (not root-owned) consumer is bounded by that scope's lifetime, not the root's");
		}

		[Fact]
		public async Task ReportsWhenTheFuncTargetIsAReleaseHookedPooledTransient()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool : IDisposable { public void Dispose() { } }
			                                       public sealed class Depot { public Depot(Func<Tool> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>(OnRelease = nameof(Return), SuppressDisposal = true)]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Return(Tool tool) { }
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsTrue()
				.Because("each Func call queues a release closure that retains the instance on the root, so a SuppressDisposal pooled transient accumulates there like a tracked disposable");
		}

		[Fact]
		public async Task DoesNotReportWhenTheFuncTargetSuppressesDisposalWithoutAReleaseHook()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool : IDisposable { public void Dispose() { } }
			                                       public sealed class Depot { public Depot(Func<Tool> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>(SuppressDisposal = true)]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsFalse()
				.Because("a SuppressDisposal transient without a release hook tracks nothing on the root, so the Func accumulates nothing");
		}

		[Fact]
		public async Task DoesNotReportForANonDisposableTransientFactory()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool { }
			                                       public sealed class Depot { public Depot(Func<Tool> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsFalse()
				.Because("a non-disposable transient with no disposable dependencies leaves nothing to accumulate");
		}

		[Fact]
		public async Task ReportsWhenAFuncBuildsANonDisposableThatTransitivelyConstructsADisposable()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Spark : IDisposable { public void Dispose() { } }
			                                       public sealed class Tool { public Tool(Spark spark) { } }
			                                       public sealed class Depot { public Depot(Func<Tool> tools) { } }

			                                       [Container]
			                                       [Transient<Spark>]
			                                       [Transient<Tool>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsTrue()
				.Because("building the non-disposable Tool on demand rebuilds its disposable transient Spark, which accumulates on the root just the same");
		}

		[Fact]
		public async Task ReportsWhenAFuncBuildsAConsumerThatCollectsDisposableTransients()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class Disposable : IPlugin, IDisposable { public void Dispose() { } }
			                                       public sealed class Consumer { public Consumer(IEnumerable<IPlugin> plugins) { } }
			                                       public sealed class Depot { public Depot(Func<Consumer> consumers) { } }

			                                       [Container]
			                                       [Transient<Disposable, IPlugin>]
			                                       [Transient<Consumer>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics).Contains("*AWT118*").AsWildcard()
				.Because("building the non-disposable Consumer on demand materializes its collection of disposable transients, which accumulate on the root just as a direct disposable transient dependency would - the transitive-disposable walk follows collection edges");
		}

		[Fact]
		public async Task ReportsWhenASingletonHoldsAFuncOfTaskOverADisposableAsyncTransient()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Conn : IAsyncInitializable, IDisposable
			                                       {
			                                           public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
			                                           public void Dispose() { }
			                                       }
			                                       public sealed class Pool { public Pool(Func<Task<Conn>> open) { } }

			                                       [Container]
			                                       [Transient<Conn>]
			                                       [Singleton<Pool>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsTrue()
				.Because("a singleton holding a Func<…, Task<T>> over a disposable async transient accumulates initialized instances on the root, just as the synchronous Func does - and Func<…, Task<T>> is the only deferred factory that can reach an async service");
			await That(diagnostics.Any(d => d.Contains("AWT118") && d.Contains("Task<Owned<"))).IsTrue()
				.Because("the async remedy points at the async owned form Func<…, Task<Owned<T>>>, the leak-free way to obtain a disposable async service per use (a synchronous Owned<T> is illegal here - AWT119)");
		}

		[Fact]
		public async Task DoesNotReportForANonDisposableAsyncTransientFactory()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Conn : IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
			                                       }
			                                       public sealed class Pool { public Pool(Func<Task<Conn>> open) { } }

			                                       [Container]
			                                       [Transient<Conn>]
			                                       [Singleton<Pool>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsFalse()
				.Because("a non-disposable async transient leaves nothing to accumulate, so the async factory is not flagged");
		}

		[Fact]
		public async Task ReportsWhenAFuncBuildsAConsumerThatCollectsDisposableKeyedMembers()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class Disposable : IPlugin, IDisposable { public void Dispose() { } }
			                                       public sealed class Consumer { public Consumer(IReadOnlyDictionary<string, IPlugin> plugins) { } }
			                                       public sealed class Depot { public Depot(Func<Consumer> consumers) { } }

			                                       [Container]
			                                       [Transient<Disposable, IPlugin>(Key = "d")]
			                                       [Transient<Consumer>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics).Contains("*AWT118*").AsWildcard()
				.Because("building the non-disposable Consumer on demand materializes its keyed dictionary of disposable transients, which accumulate on the root - the transitive-disposable walk follows keyed-dictionary edges too");
		}

		[Fact]
		public async Task ReportsWhenAFuncBuildsAConsumerThatCollectsDisposableAwaitedKeyedMembers()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Collections.Generic;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class Disposable : IPlugin, IDisposable { public void Dispose() { } }
			                                       public sealed class Consumer { public Consumer(Task<IReadOnlyDictionary<string, IPlugin>> plugins) { } }
			                                       public sealed class Depot { public Depot(Func<Consumer> consumers) { } }

			                                       [Container]
			                                       [Transient<Disposable, IPlugin>(Key = "d")]
			                                       [Transient<Consumer>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics).Contains("*AWT118*").AsWildcard()
				.Because("building the non-disposable Consumer on demand materializes its awaited keyed dictionary of disposable transients (the task starts materializing them at construction), which accumulate on the root - the transitive-disposable walk follows awaited-keyed-dictionary edges too");
		}

		[Fact]
		public async Task ReportsWhenASingletonActivationHookHoldsAFuncOverADisposableTransient()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool : IDisposable { public void Dispose() { } }
			                                       public sealed class Depot { }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Singleton<Depot>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Depot depot, Func<Tool> tools) { }
			                                       }
			                                       """);

			// A release hook's Func is rejected outright (AWT191), so the activation hook is the surviving
			// hook-parameter shape this walk covers.
			await That(diagnostics.Any(d => d.Contains("AWT118"))).IsTrue()
				.Because("a root-owned singleton's activation hook can invoke its Func over a disposable transient, each call tracking a fresh disposable on the root like a constructor-held Func");
		}
	}
}
