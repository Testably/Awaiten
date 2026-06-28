namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of resolve-time arguments: a service with one or more <c>[Arg]</c> parameters is
///     built through an injected <see cref="Func{T, TResult}" /> (or a directly resolved
///     <c>Func&lt;TArg…, T&gt;</c>) that supplies those arguments, while its remaining dependencies still
///     come from the graph. Each call constructs a fresh instance, and a disposable one is released by the
///     owner that the factory is bound to. The containers and services are nested types, so the enclosing
///     class is <c>partial</c>.
/// </summary>
public partial class RuntimeArgumentTests
{
	[Fact]
	public async Task Func_WithRuntimeArgument_BuildsTheServiceFromBothTheArgumentAndTheGraph()
	{
		using RuntimeArgumentContainer.Root container = new();

		Workshop workshop = container.Resolve<Workshop>();
		Robot first = workshop.Build("R2");
		Robot second = workshop.Build("C3");

		await That(first.Name).IsEqualTo("R2")
			.Because("the runtime argument flows through the Func into the marked parameter");
		await That(second.Name).IsEqualTo("C3");
		await That(first).IsNotSameAs(second)
			.Because("a parameterized service is built fresh on every call");
		await That(first.Engine).IsSameAs(second.Engine)
			.Because("the non-argument dependency is still resolved from the graph (a shared singleton)");
	}

	[Fact]
	public async Task Func_WithTwoRuntimeArguments_ForwardsThemPositionally()
	{
		using RuntimeArgumentContainer.Root container = new();

		Func<string, int, Gadget> factory = container.Resolve<Func<string, int, Gadget>>();
		Gadget gadget = factory("Bender", 42);

		await That(gadget.Name).IsEqualTo("Bender");
		await That(gadget.Serial).IsEqualTo(42);
	}

	[Fact]
	public async Task Func_BoundToAScope_ReleasesTheBuiltDisposablesWithThatScope()
	{
		using RuntimeArgumentContainer.Root container = new();

		Tool tool;
		using (IAwaitenScope scope = container.CreateScope())
		{
			Func<int, Tool> factory = scope.Resolve<Func<int, Tool>>();
			tool = factory(7);

			await That(tool.Disposed).IsFalse()
				.Because("the scope that built the tool is still alive");
		}

		await That(tool.Disposed).IsTrue()
			.Because("a disposable parameterized service is tracked by the owner its factory is bound to");
	}

	[Fact]
	public async Task Func_OverAParameterizedServiceThatItselfTakesRuntimeArguments_Composes()
	{
		using RuntimeArgumentContainer.Root container = new();

		Func<string, Crate> crates = container.Resolve<Func<string, Crate>>();
		Crate crate = crates("export");
		Widget first = crate.Pack(5);
		Widget second = crate.Pack(9);

		await That(crate.Label).IsEqualTo("export")
			.Because("the outer runtime argument flows into the outer [Arg] parameter");
		await That(first.Size).IsEqualTo(5)
			.Because("the inner runtime argument flows through the nested Func into the inner [Arg] parameter");
		await That(second.Size).IsEqualTo(9);
		await That(first).IsNotSameAs(second)
			.Because("each call to the nested factory builds a fresh widget");
		await That(first.Engine).IsSameAs(second.Engine)
			.Because("the nested graph dependency is still the shared singleton");
	}

	[Fact]
	public async Task Factory_WithRuntimeArgument_ForwardsItToTheFactoryMethod()
	{
		using FactoryArgContainer.Root container = new();

		Func<string, Badge> badges = container.Resolve<Func<string, Badge>>();

		await That(badges("A1").Code).IsEqualTo("A1")
			.Because("a [Arg] on a factory-method parameter is supplied at resolve time, like a constructor's");
		await That(badges("B2").Code).IsEqualTo("B2");
	}

	[Fact]
	public async Task Arg_WhoseTypeIsAlsoRegistered_IsSuppliedAtRuntimeRatherThanFromTheGraph()
	{
		using RuntimeArgumentContainer.Root container = new();

		Engine supplied = new();
		Func<Engine, Stamp> stamps = container.Resolve<Func<Engine, Stamp>>();
		Stamp stamp = stamps(supplied);

		await That(stamp.Engine).IsSameAs(supplied)
			.Because("a [Arg] parameter is supplied at runtime, bypassing the registered Engine even though its type is registered");
	}

	[Fact]
	public async Task Func_BoundToASingleton_ReleasesTheBuiltDisposablesWithTheContainer()
	{
		RuntimeArgumentContainer.Root container = new();

		Depot depot = container.Resolve<Depot>();
		Tool tool = depot.Make(1);

		await That(tool.Disposed).IsFalse()
			.Because("the container that owns the singleton's factory is still alive");

		container.Dispose();

		await That(tool.Disposed).IsTrue()
			.Because("a tool built through a singleton's root-bound Func accumulates on the root and is disposed with the container");
	}

	public sealed class Engine;

	public sealed class Robot
	{
		public Robot(Engine engine, [Arg] string name)
		{
			Engine = engine;
			Name = name;
		}

		public Engine Engine { get; }

		public string Name { get; }
	}

	public sealed class Gadget
	{
		public Gadget([Arg] string name, [Arg] int serial)
		{
			Name = name;
			Serial = serial;
		}

		public string Name { get; }

		public int Serial { get; }
	}

	public sealed class Tool : IDisposable
	{
		public Tool([Arg] int id) => Id = id;

		public int Id { get; }

		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public sealed class Workshop
	{
		private readonly Func<string, Robot> _robots;

		public Workshop(Func<string, Robot> robots) => _robots = robots;

		public Robot Build(string name) => _robots(name);
	}

	// A parameterized service whose own runtime argument is supplied through a nested parameterized Func.
	public sealed class Widget
	{
		public Widget(Engine engine, [Arg] int size)
		{
			Engine = engine;
			Size = size;
		}

		public Engine Engine { get; }

		public int Size { get; }
	}

	public sealed class Crate
	{
		private readonly Func<int, Widget> _widgets;

		public Crate([Arg] string label, Func<int, Widget> widgets)
		{
			Label = label;
			_widgets = widgets;
		}

		public string Label { get; }

		public Widget Pack(int size) => _widgets(size);
	}

	// A [Arg] whose type is itself a registered service: the value comes from the call, not the graph.
	public sealed class Stamp
	{
		public Stamp([Arg] Engine engine) => Engine = engine;

		public Engine Engine { get; }
	}

	// A singleton holding a Func over a disposable parameterized service: the built tools bind to the root.
	public sealed class Depot
	{
		private readonly Func<int, Tool> _tools;

		public Depot(Func<int, Tool> tools) => _tools = tools;

		public Tool Make(int id) => _tools(id);
	}

	// The singleton Depot holds a Func<int, Tool> over the disposable parameterized Tool: tools built through
	// it accumulate on the root until the container is disposed, which is exactly what Func_BoundToASingleton...
	// asserts. That deliberate accumulation is an AWT117 error under strict lifetime safety, so this container
	// opts into Loose; the leak-free alternative (Func<int, Owned<Tool>>) is exercised in OwnedTests.
	[Container(LifetimeSafety = LifetimeSafety.Loose)]
	[Singleton<Engine>]
	[Transient<Robot>]
	[Transient<Gadget>]
	[Transient<Tool>]
	[Transient<Widget>]
	[Transient<Crate>]
	[Transient<Stamp>]
	[Singleton<Depot>]
	[Singleton<Workshop>]
	public static partial class RuntimeArgumentContainer;

	public sealed class Badge
	{
		public Badge(string code) => Code = code;

		public string Code { get; }
	}

	// A factory method with a [Arg] parameter: the runtime argument is forwarded to the factory, not the
	// constructor, so the produced type need not declare [Arg] itself.
	[Container]
	[Transient<Badge>(Factory = nameof(MakeBadge))]
	public static partial class FactoryArgContainer
	{
		private static Badge MakeBadge([Arg] string code) => new(code);
	}
}
