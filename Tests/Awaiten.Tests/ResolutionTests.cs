namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of a generated container over a small graph. The container
///     (<see cref="GraphContainer" />) and its services are declared as nested types to exercise the
///     nested-container support — the enclosing class is therefore <c>partial</c>.
/// </summary>
public partial class ResolutionTests
{
	[Fact]
	public async Task ConstructorDependencies_AreWiredFromTheGraph()
	{
		GraphContainer container = new();

		Leaf leaf = container.Get<Leaf>();
		IMiddle middle = container.Get<IMiddle>();
		Top top = container.Get<Top>();

		await That(middle.Leaf).IsSameAs(leaf).Because("the singleton Leaf is shared by every consumer");
		await That(top.Leaf).IsSameAs(leaf).Because("the singleton Leaf is shared by every consumer");
		await That(top.Middle).IsSameAs(middle)
			.Because("the singleton Middle is the same instance injected into the transient Top");
	}

	[Fact]
	public async Task Resolve_ForAnUnregisteredType_Throws()
	{
		GraphContainer container = new();

		await That(() => container.Resolve(typeof(string))).Throws<InvalidOperationException>();
	}

	[Fact]
	public async Task Resolve_OnIAwaitenContainer_ReturnsTheRegisteredInstance()
	{
		GraphContainer container = new();

		object middle = container.Resolve(typeof(IMiddle));

		await That(middle).IsSameAs(container.Get<IMiddle>());
	}

	[Fact]
	public async Task Singleton_ReturnsTheSameInstanceEachTime()
	{
		GraphContainer container = new();

		Leaf first = container.Get<Leaf>();
		Leaf second = container.Get<Leaf>();

		await That(first).IsSameAs(second);
	}

	[Fact]
	public async Task Singleton_ViaServiceType_ReturnsTheSameInstanceEachTime()
	{
		GraphContainer container = new();

		IMiddle first = container.Get<IMiddle>();
		IMiddle second = container.Get<IMiddle>();

		await That(first).IsSameAs(second);
		await That(first).Is<Middle>();
	}

	[Fact]
	public async Task Transient_ReturnsANewInstanceEachTime()
	{
		GraphContainer container = new();

		Top first = container.Get<Top>();
		Top second = container.Get<Top>();

		await That(ReferenceEquals(first, second)).IsFalse();
	}

	[Fact]
	public async Task TryResolve_ForAnUnregisteredType_ReturnsFalse()
	{
		GraphContainer container = new();

		bool resolved = container.TryResolve(typeof(string), out object? instance);

		await That(resolved).IsFalse();
		await That(instance).IsNull();
	}

	public sealed class Leaf;

	public interface IMiddle
	{
		Leaf Leaf { get; }
	}

	public sealed class Middle(Leaf leaf) : IMiddle
	{
		public Leaf Leaf { get; } = leaf;
	}

	public sealed class Top(IMiddle middle, Leaf leaf)
	{
		public IMiddle Middle { get; } = middle;
		public Leaf Leaf { get; } = leaf;
	}

	[Container]
	[Singleton<Leaf>]
	[Singleton<Middle, IMiddle>]
	[Transient<Top>]
	public partial class GraphContainer;
}
