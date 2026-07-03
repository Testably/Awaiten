using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated shape of opt-in property injection: a property marked <c>[Inject]</c> (settable or
///     <c>init</c>) is filled through an object initializer appended to the constructor call, so the
///     instance is never observed half-set. A plain property is not auto-injected. A property edge is a
///     full graph edge (it participates in AWT102 cycle detection just like a constructor parameter).
/// </summary>
public class PropertyInjectionTests
{
	[Fact]
	public async Task Inject_EmitsAnObjectInitializerFillingTheMarkedProperties()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Bus { }
		                                       public sealed class Log { }
		                                       public sealed class Consumer
		                                       {
		                                           [Inject] public Bus Bus { get; init; }
		                                           [Inject] public Log Log { get; set; }
		                                       }

		                                       [Container]
		                                       [Transient<Bus>]
		                                       [Transient<Log>]
		                                       [Transient<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The init property and the settable property are both filled through an object initializer appended to
		// the constructor call (object-initializer syntax assigns init-only just as it does set).
		await That(source).Contains("new global::MyCode.Consumer() { Bus = ResolveBus(), Log = ResolveLog() }");
	}

	[Fact]
	public async Task PlainProperty_IsNotAutoInjected()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Bus { }
		                                       public sealed class Consumer
		                                       {
		                                           [Inject] public Bus Injected { get; init; }
		                                           public Bus Plain { get; init; }
		                                       }

		                                       [Container]
		                                       [Transient<Bus>]
		                                       [Transient<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Only the [Inject] property is assigned; the plain property is left to the caller.
		await That(source).Contains("new global::MyCode.Consumer() { Injected = ResolveBus() }");
		await That(source).DoesNotContain("Plain =");
	}

	[Fact]
	public async Task CycleThroughAnInjectedProperty_ReportsAwt102()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       // A depends on B through its constructor; B depends back on A through an injected property.
		                                       // A property edge is Direct, so it closes the cycle exactly like a constructor parameter would.
		                                       public sealed class A { public A(B b) { } }
		                                       public sealed class B { [Inject] public A A { get; set; } }

		                                       [Container]
		                                       [Transient<A>]
		                                       [Transient<B>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Any(d => d.Contains("AWT102"))).IsTrue();
	}

	[Fact]
	public async Task DeferredProperty_BreaksAMutualSingletonCycle_NoAwt102_AndAssignsAfterCaching()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       // Two singletons that reference each other through a deferred property - a deferred edge
		                                       // contributes no graph edge, so it breaks the mutual cycle that a plain [Inject] would close.
		                                       public sealed class OrderService { [Inject(Deferred = true)] public InvoiceService Invoice { get; set; } }
		                                       public sealed class InvoiceService { [Inject(Deferred = true)] public OrderService Order { get; set; } }

		                                       [Container]
		                                       [Singleton<OrderService>]
		                                       [Singleton<InvoiceService>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The deferred property is assigned after the instance is stored in its cache field - not inside the object
		// initializer - so a re-entrant resolve returns the cached instance and the cycle terminates.
		await That(source).Contains("_orderService = new global::MyCode.OrderService();");
		await That(source).Contains("_orderService.Invoice = __root.ResolveInvoiceService();");
		// The constructor call carries no object initializer for the deferred member.
		await That(source).DoesNotContain("new global::MyCode.OrderService() { Invoice");
		// The lock-free fast path is preserved but gated on a volatile wiring flag, set last inside the cache-miss
		// block: a concurrent caller returns the cached instance only once its deferred property is wired, while the
		// mid-wiring re-entrant resolve sees the flag still false and terminates the cycle through the lock.
		await That(source).Contains("private volatile bool _orderServiceWired;");
		await That(source).Contains("if (_orderService is not null && _orderServiceWired)")
			.Because("the fast path returns the singleton only once it is fully wired, keeping a half-wired instance unobservable across threads without permanently locking every resolve");
		await That(source).Contains("_orderServiceWired = true;");
	}

	[Fact]
	public async Task DeferredProperty_WiringFlagField_DoesNotCollideWithAServiceNamedLikeIt()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Bus { }
		                                       // Reader gets a '_readerWired' wiring flag for its deferred member; a service type literally
		                                       // named 'ReaderWired' would claim the same '_readerWired' cache field if derived names were not
		                                       // part of the name uniquification (CS0102 in the generated container).
		                                       public sealed class Reader { [Inject(Deferred = true)] public Bus Bus { get; set; } }
		                                       public sealed class ReaderWired { }

		                                       [Container]
		                                       [Singleton<Bus>]
		                                       [Singleton<Reader>]
		                                       [Singleton<ReaderWired>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("the name table reserves the derived 'Wired' suffix, so a service named like another service's wiring flag is renamed instead of colliding");
	}

	[Fact]
	public async Task DeferredProperty_TheSameCycleWithoutDeferred_StillReportsAwt102()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       // The same mutual reference with a plain [Inject] property is a Direct edge, so it closes the cycle.
		                                       public sealed class OrderService { [Inject] public InvoiceService Invoice { get; set; } }
		                                       public sealed class InvoiceService { [Inject] public OrderService Order { get; set; } }

		                                       [Container]
		                                       [Singleton<OrderService>]
		                                       [Singleton<InvoiceService>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Any(d => d.Contains("AWT102"))).IsTrue();
	}

	[Fact]
	public async Task DeferredProperty_ToAnAsyncTarget_TaintsTheOwnerAndAwaitsTheTarget()
	{
		GeneratorResult result = Generator.Run("""
		                                       using System.Threading;
		                                       using System.Threading.Tasks;
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       // The owner is not itself async, but its deferred member targets an async-initialized service.
		                                       // Taint must reach the owner so its deferred assignment awaits, rather than emitting a synchronous
		                                       // resolve of an async-only service (which would not compile).
		                                       public sealed class AsyncDep : IAsyncInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
		                                       }
		                                       public sealed class Owner { [Inject(Deferred = true)] public AsyncDep Dep { get; set; } }

		                                       [Container]
		                                       [Singleton<AsyncDep>]
		                                       [Singleton<Owner>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// No diagnostics includes the compiler diagnostics of the generated source, so a synchronous resolve of the
		// async-only target (CS1061) would fail here.
		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The owner is async-tainted, so its deferred member is wired by awaiting the target's async resolver.
		await That(source).Contains(".Dep = await ResolveAsyncDepAsync(cancellationToken).ConfigureAwait(false);");
	}
}
