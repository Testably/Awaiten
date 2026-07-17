using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Opt-in property injection. A <c>[Inject]</c> property (set or init) is filled through an object
///     initializer on the constructor call, so the instance is never observed half-set. A plain property is
///     not injected. A property edge is a full graph edge, so it participates in AWT102 cycle detection.
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

		// Object-initializer syntax assigns init-only just as it does set.
		await That(source).Contains("new global::MyCode.Consumer() { Bus = ResolveBus(__s), Log = ResolveLog(__s) }");
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

		await That(source).Contains("new global::MyCode.Consumer() { Injected = ResolveBus(__s) }");
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

		await That(result.Diagnostics).Contains("*AWT102*").AsWildcard();
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

		// The deferred property is assigned after the instance is cached, not in the object initializer, so a
		// re-entrant resolve returns the cached instance and the cycle terminates.
		await That(source).Contains("__s._orderService = new global::MyCode.OrderService();");
		await That(source).Contains("_orderService.Invoice = Root.ResolveInvoiceService(__s.__root);");
		await That(source).DoesNotContain("new global::MyCode.OrderService() { Invoice");
		// The fast path is gated on a volatile wiring flag set last, so a concurrent caller sees the cached
		// instance only once it is fully wired.
		await That(source).Contains("private volatile bool _orderServiceWired;");
		await That(source).Contains("if (__s._orderService is not null && __s._orderServiceWired)")
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

		await That(result.Diagnostics).Contains("*AWT102*").AsWildcard();
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
		await That(source).Contains(".Dep = await Root.ResolveAsyncDepAsync(__s.__root, cancellationToken).ConfigureAwait(false);");
	}

	[Fact]
	public async Task ExplicitlyRegisteredCollectionShape_WinsOverSynthesisOnAnInjectedProperty()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Bundle : List<IPlugin> { }
		                                       public sealed class Host
		                                       {
		                                           [Inject] public IEnumerable<IPlugin> Plugins { get; set; }
		                                       }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Bundle, IEnumerable<IPlugin>>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// An [Inject] member resolves exactly like a constructor parameter: the registered IEnumerable<IPlugin>
		// service (an opaque value) preempts the collection synthesized from the IPlugin registrations.
		await That(source).Contains("Plugins = Root.ResolveBundle(__s.__root)")
			.Because("an explicitly registered collection shape wins over synthesis on property injection too");
	}
}
