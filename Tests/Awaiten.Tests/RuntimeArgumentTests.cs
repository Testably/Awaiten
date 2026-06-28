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
		using RuntimeArgumentContainer container = new();

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
		using RuntimeArgumentContainer container = new();

		Func<string, int, Gadget> factory = container.Resolve<Func<string, int, Gadget>>();
		Gadget gadget = factory("Bender", 42);

		await That(gadget.Name).IsEqualTo("Bender");
		await That(gadget.Serial).IsEqualTo(42);
	}

	[Fact]
	public async Task Func_BoundToAScope_ReleasesTheBuiltDisposablesWithThatScope()
	{
		using RuntimeArgumentContainer container = new();

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

	public sealed class Engine;

	public sealed class Robot
	{
		public Robot(Engine engine, [Arg<string>] string name)
		{
			Engine = engine;
			Name = name;
		}

		public Engine Engine { get; }

		public string Name { get; }
	}

	public sealed class Gadget
	{
		public Gadget([Arg<string>] string name, [Arg<int>] int serial)
		{
			Name = name;
			Serial = serial;
		}

		public string Name { get; }

		public int Serial { get; }
	}

	public sealed class Tool : IDisposable
	{
		public Tool([Arg<int>] int id) => Id = id;

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

	[Container]
	[Singleton<Engine>]
	[Transient<Robot>]
	[Transient<Gadget>]
	[Transient<Tool>]
	[Singleton<Workshop>]
	public partial class RuntimeArgumentContainer;
}
