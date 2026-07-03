using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt138Awt139Awt140DeferredProperty
	{
		[Fact]
		public async Task ReportsAwt138WhenDeferredPropertyIsInitOnly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           // A deferred property is assigned after construction, which an init-only accessor forbids.
			                                           [Inject(Deferred = true)] public Bus Bus { get; init; }
			                                       }

			                                       [Container]
			                                       [Singleton<Bus>]
			                                       [Singleton<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT138"))).IsTrue()
				.Because("a deferred property is assigned after construction, so it needs a set accessor - an init-only accessor can only be assigned in an object initializer");
		}

		[Fact]
		public async Task ReportsAwt139WhenADeferredCycleIncludesATransient()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // A transient is not cached, so a mutual deferred cycle through it cannot terminate at runtime.
			                                       public sealed class Left { [Inject(Deferred = true)] public Right Right { get; set; } }
			                                       public sealed class Right { [Inject(Deferred = true)] public Left Left { get; set; } }

			                                       [Container]
			                                       [Transient<Left>]
			                                       [Transient<Right>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT139"))).IsTrue()
				.Because("a deferred property breaks a cycle only when the owning instance is cached; a transient is rebuilt on each resolve, so the cycle would recurse forever");
		}

		[Fact]
		public async Task ReportsAwt139WhenADeferredCycleClosesThroughACollectionMember()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Collections.Generic;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // A deferred collection member materializes its elements eagerly at assignment time, so it closes a
			                                       // cycle just like a direct member - a transient participant still cannot terminate.
			                                       public sealed class Hub { [Inject(Deferred = true)] public IEnumerable<Node> Nodes { get; set; } }
			                                       public sealed class Node { [Inject(Deferred = true)] public Hub Hub { get; set; } }

			                                       [Container]
			                                       [Transient<Hub>]
			                                       [Transient<Node>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT139"))).IsTrue()
				.Because("a deferred collection member is materialized eagerly, so a transient cycle closed through it recurses forever exactly like a direct member");
		}

		[Fact]
		public async Task ASynchronousSingletonDeferredCycle_DoesNotReportAwt139OrAwt140()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // Both sides are cached and resolved synchronously, so the mutual deferred cycle terminates - the supported case.
			                                       public sealed class Left { [Inject(Deferred = true)] public Right Right { get; set; } }
			                                       public sealed class Right { [Inject(Deferred = true)] public Left Left { get; set; } }

			                                       [Container]
			                                       [Singleton<Left>]
			                                       [Singleton<Right>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT139") || d.Contains("AWT140"))).IsFalse()
				.Because("a synchronous singleton (or scoped) deferred cycle is cached before it is wired, so it terminates and is supported");
		}

		[Fact]
		public async Task ReportsAwt140WhenADeferredCycleIncludesAnAsyncService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Threading;
			                                       using System.Threading.Tasks;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // Both sides are async-initialized: an async resolver publishes its memoized task only after the
			                                       // re-entrant resolve has returned, so the deferred cycle overflows the stack or deadlocks at runtime.
			                                       public sealed class Left : IAsyncInitializable
			                                       {
			                                           [Inject(Deferred = true)] public Right Right { get; set; }
			                                           public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
			                                       }
			                                       public sealed class Right : IAsyncInitializable
			                                       {
			                                           [Inject(Deferred = true)] public Left Left { get; set; }
			                                           public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<Left>]
			                                       [Singleton<Right>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT140"))).IsTrue()
				.Because("a deferred property cannot break a cycle through an async service - its memoized task is published only after the re-entrant resolve returns, so the cycle cannot terminate");
			await That(result.Diagnostics.Any(d => d.Contains("AWT139"))).IsFalse()
				.Because("both participants are singletons, so the fault is the async one (AWT140), not the transient one (AWT139)");
		}

		[Fact]
		public async Task AnAsyncNonCyclicDeferredProperty_DoesNotReportAwt140()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Threading;
			                                       using System.Threading.Tasks;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // A non-cyclic deferred member of an async service is fine: it is awaited after construction and
			                                       // never re-enters the still-building owner.
			                                       public sealed class Dependency : IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
			                                       }
			                                       public sealed class Consumer : IAsyncInitializable
			                                       {
			                                           [Inject(Deferred = true)] public Dependency Dependency { get; set; }
			                                           public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<Dependency>]
			                                       [Singleton<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT140"))).IsFalse()
				.Because("a deferred member is only rejected when it closes a cycle through an async service, not for any async deferred member");
		}
	}
}
