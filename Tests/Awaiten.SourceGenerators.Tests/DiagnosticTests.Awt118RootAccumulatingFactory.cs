using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	// AWT118 is reported by AwaitenAnalyzer (not the generator) so it can be suppressed in source; these
	// tests therefore drive the analyzer. The containers use the default (strict) lifetime safety, under
	// which AWT118 is an error - LifetimeSafetyTests covers the strict-error vs loose-warning escalation.
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
	}
}
