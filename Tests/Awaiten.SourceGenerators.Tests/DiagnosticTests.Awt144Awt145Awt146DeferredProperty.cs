using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt144Awt145Awt146DeferredProperty
	{
		[Fact]
		public async Task ReportsAwt144WhenDeferredPropertyIsInitOnly()
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

			await That(result.Diagnostics).Contains("*AWT144*").AsWildcard()
				.Because("a deferred property is assigned after construction, so it needs a set accessor - an init-only accessor can only be assigned in an object initializer");
		}

		[Fact]
		public async Task ReportsAwt144WhenDeferredPropertyIsRequired()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           // A deferred property is omitted from the emitted object initializer, which a required member
			                                           // does not allow - without AWT144 the generated `new Consumer()` fails with an opaque CS9035.
			                                           [Inject(Deferred = true)] public required Bus Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Singleton<Bus>]
			                                       [Singleton<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT144*").AsWildcard()
				.Because("a required member can only be satisfied inside an object initializer, which is exactly the construction-time path a deferred property must avoid - it deserves the targeted diagnostic, not CS9035 in generated code");
		}

		[Fact]
		public async Task ReportsAwt145WhenADeferredCycleIncludesATransient()
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

			await That(result.Diagnostics).Contains("*AWT145*").AsWildcard()
				.Because("a deferred property breaks a cycle only when the owning instance is cached; a transient is rebuilt on each resolve, so the cycle would recurse forever");
		}

		[Fact]
		public async Task ReportsAwt145ForASelfReferentialTransientDeferredProperty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // A transient that defers a reference to its own type is a one-node deferred cycle; a transient is
			                                       // never cached, so each resolve rebuilds a fresh instance and the self-reference recurses forever.
			                                       public sealed class Node { [Inject(Deferred = true)] public Node Self { get; set; } }

			                                       [Container]
			                                       [Transient<Node>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT145*").AsWildcard()
				.Because("a self-referential deferred property on a transient is a one-node cycle with nothing cached, so it cannot terminate");
		}

		[Fact]
		public async Task ASelfReferentialSingletonDeferredProperty_DoesNotReportAwt145OrAwt146()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // A singleton is cached before its deferred member is wired, so the re-entrant self-resolve returns
			                                       // the cached instance and the one-node cycle terminates - the supported case.
			                                       public sealed class Node { [Inject(Deferred = true)] public Node Self { get; set; } }

			                                       [Container]
			                                       [Singleton<Node>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a self-referential deferred property on a cached singleton terminates and is supported");
		}

		[Fact]
		public async Task ReportsAwt145WhenADeferredCycleClosesThroughACollectionMember()
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

			await That(result.Diagnostics).Contains("*AWT145*").AsWildcard()
				.Because("a deferred collection member is materialized eagerly, so a transient cycle closed through it recurses forever exactly like a direct member");
		}

		[Fact]
		public async Task ASynchronousSingletonDeferredCycle_DoesNotReportAwt145OrAwt146()
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

			await That(result.Diagnostics).DoesNotContain("*AWT145*").AsWildcard()
				.Because("a synchronous singleton (or scoped) deferred cycle is cached before it is wired, so it terminates and is supported");
			await That(result.Diagnostics).DoesNotContain("*AWT146*").AsWildcard()
				.Because("a synchronous singleton (or scoped) deferred cycle is cached before it is wired, so it terminates and is supported");
		}

		[Fact]
		public async Task AMixedLifetimeDeferredCycle_WithOneCachedParticipant_DoesNotReportAwt145()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // Left is a singleton, Right a transient, both deferred. The singleton is cached before it is wired,
			                                       // so a re-entrant resolve returns the cached Left and the cycle terminates from any entry point - the
			                                       // transient is merely rebuilt a bounded number of times. AWT145 fires only when *every* participant is
			                                       // a transient (nothing cached anywhere), so it must not fire here.
			                                       public sealed class Left { [Inject(Deferred = true)] public Right Right { get; set; } }
			                                       public sealed class Right { [Inject(Deferred = true)] public Left Left { get; set; } }

			                                       [Container]
			                                       [Singleton<Left>]
			                                       [Transient<Right>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT145*").AsWildcard()
				.Because("a cached singleton participant breaks the recursion, so a mixed-lifetime deferred cycle terminates and is not the all-transient AWT145 fault");
			await That(result.Diagnostics).IsEmpty()
				.Because("the mixed cycle is supported: no AWT102 (deferred edges are absent from the construction graph), AWT146 (nothing async) or AWT147 (no construction edge) either");
		}

		[Fact]
		public async Task ReportsAwt146WhenADeferredCycleIncludesAnAsyncService()
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

			await That(result.Diagnostics).Contains("*AWT146*").AsWildcard()
				.Because("a deferred property cannot break a cycle through an async service - its memoized task is published only after the re-entrant resolve returns, so the cycle cannot terminate");
			await That(result.Diagnostics).DoesNotContain("*AWT145*").AsWildcard()
				.Because("both participants are singletons, so the fault is the async one (AWT146), not the transient one (AWT145)");
		}

		[Fact]
		public async Task AnAsyncNonCyclicDeferredProperty_DoesNotReportAwt146()
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

			await That(result.Diagnostics).DoesNotContain("*AWT146*").AsWildcard()
				.Because("a deferred member is only rejected when it closes a cycle through an async service, not for any async deferred member");
		}

		[Fact]
		public async Task ReportsAwt147WhenAMixedCycleStillTraversesAConstructorEdge()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // Only one side is deferred: Left defers Right, but Right's constructor still takes Left. The deferred
			                                       // edge escapes AWT102, yet the surviving constructor edge re-enters an as-yet-uncached participant when
			                                       // resolution begins at Right, so the cycle is only partly broken and cannot terminate from every entry.
			                                       public sealed class Left { [Inject(Deferred = true)] public Right Right { get; set; } }
			                                       public sealed class Right { public Right(Left left) { } }

			                                       [Container]
			                                       [Singleton<Left>]
			                                       [Singleton<Right>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT147*").AsWildcard()
				.Because("a deferred property breaks a cycle only when every edge is deferred; a surviving constructor edge re-enters an uncached participant, so the cycle is only partly broken");
			await That(result.Diagnostics).DoesNotContain("*AWT102*").AsWildcard()
				.Because("the deferred edge is absent from the construction graph, so AWT102 does not fire - AWT147 is the diagnostic that catches this");
		}

		[Fact]
		public async Task ReportsAwt147ForAMixedCycleThroughAPlainInjectProperty_RegardlessOfLifetime()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // Left defers Right, but Right holds Left through a plain [Inject] property (a Direct construction-time
			                                       // edge). A transient is never cached, so without AWT147 this compiles clean and stack-overflows at runtime.
			                                       // The mixed-edge fault applies regardless of lifetime, so AWT147 (not AWT145) is the reason.
			                                       public sealed class Left { [Inject(Deferred = true)] public Right Right { get; set; } }
			                                       public sealed class Right { [Inject] public Left Left { get; set; } }

			                                       [Container]
			                                       [Transient<Left>]
			                                       [Transient<Right>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT147*").AsWildcard()
				.Because("a mixed cycle through a plain [Inject] property still traverses a construction-time edge, so a deferred property cannot break it");
			await That(result.Diagnostics).DoesNotContain("*AWT102*").AsWildcard()
				.Because("the deferred edge keeps the cycle out of the construction graph, so AWT102 does not fire");
		}

		[Fact]
		public async Task ReportsAwt147WhenAMixedCycleTraversesAnEagerOwned()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // Left defers Right, but Right holds Left through a bare Owned<Left> constructor parameter - an eager
			                                       // relationship that resolves its target at construction time, so it is a construction edge just like a
			                                       // direct parameter. The cycle is only partly broken and re-enters an uncached participant from Right.
			                                       public sealed class Left { [Inject(Deferred = true)] public Right Right { get; set; } }
			                                       public sealed class Right { public Right(Owned<Left> left) { } }

			                                       [Container]
			                                       [Transient<Left>]
			                                       [Transient<Right>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT147*").AsWildcard()
				.Because("a bare Owned<T> resolves its target at construction time, so it is a construction edge that leaves the deferred cycle only partly broken");
			await That(result.Diagnostics).DoesNotContain("*AWT102*").AsWildcard()
				.Because("the construction graph has only the Right -> Left edge (the deferred Left -> Right edge is absent), so it holds no cycle for AWT102");
		}

		[Fact]
		public async Task ReportsAwt147WhenAFaultyMixedCycleOverlapsASupportedDeferredCycle()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // Two cycles share the strongly connected component {Alpha, Beta, Gamma}: the all-deferred
			                                       // Alpha <-> Gamma cycle is supported, but Alpha -> Beta -> Gamma -> Alpha keeps Beta's
			                                       // constructor edge to Gamma. A per-cycle DFS that dismisses the supported cycle first never
			                                       // enumerates the faulty one (Gamma is already off the stack when Beta is visited), so the
			                                       // verdict must come from the component's whole edge set: Beta is a cached (singleton) source
			                                       // of a construction edge, so resolving Beta re-enters it mid-construction and duplicates it.
			                                       public sealed class Alpha
			                                       {
			                                           [Inject(Deferred = true)] public Gamma Gamma { get; set; }
			                                           [Inject(Deferred = true)] public Beta Beta { get; set; }
			                                       }
			                                       public sealed class Beta { public Beta(Gamma gamma) { } }
			                                       public sealed class Gamma { [Inject(Deferred = true)] public Alpha Alpha { get; set; } }

			                                       [Container]
			                                       [Singleton<Alpha>]
			                                       [Singleton<Beta>]
			                                       [Singleton<Gamma>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT147*").AsWildcard()
				.Because("the construction edge Beta -> Gamma survives in a cycle with deferred edges, so resolving Beta re-enters it before it is cached and constructs a duplicate singleton - even though the same component also contains a supported all-deferred cycle");
			await That(result.Diagnostics).DoesNotContain("*AWT102*").AsWildcard()
				.Because("the construction graph holds only Beta -> Gamma, which is acyclic, so AWT102 stays silent - AWT147 is the diagnostic that must catch this");
		}

		[Fact]
		public async Task AConstructionEdgeFromATransient_WithACachedParticipant_IsSupported()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // The classic hub-and-spoke break: the singleton hub defers its spoke, and the transient
			                                       // spoke's constructor takes the hub. The hub is cached before it is wired, so the spoke's
			                                       // constructor edge resolves the already-cached hub and the cycle terminates from both entry
			                                       // points (entering at the spoke merely builds one extra transient spoke for the hub, which is
			                                       // normal transient semantics). This must not be rejected as AWT147.
			                                       public sealed class Hub { [Inject(Deferred = true)] public Spoke Spoke { get; set; } }
			                                       public sealed class Spoke { public Spoke(Hub hub) { } }

			                                       [Container]
			                                       [Singleton<Hub>]
			                                       [Transient<Spoke>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a construction edge that starts at a transient in a cycle with a synchronously-cached participant terminates from every entry point, so the supported hub-and-spoke break must compile clean");
		}
	}
}
