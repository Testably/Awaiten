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
}
