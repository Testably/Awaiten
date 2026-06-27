namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of the factory-method and pre-built-instance registrations: a <c>Factory</c>
///     registration produces the service by calling a container method (respecting the declared
///     lifetime), and an <c>Instance</c> registration hands back a member the container does not own.
///     The containers and services are nested types, so the enclosing class is <c>partial</c>.
/// </summary>
public partial class FactoryAndInstanceTests
{
	[Fact]
	public async Task Factory_ProducesTheServiceAndIsCachedAccordingToLifetime()
	{
		using FactoryContainer container = new();

		await That(container.Resolve<IWidget>()).IsSameAs(container.Resolve<IWidget>())
			.Because("the singleton factory result is cached");
		await That(((Widget)container.Resolve<IWidget>()).Origin).IsEqualTo("factory")
			.Because("the factory method produced the instance, which a constructor would not have marked");
	}

	[Fact]
	public async Task Factory_ResolvesItsParametersFromTheGraph()
	{
		using FactoryContainer container = new();

		Report report = container.Resolve<Report>();

		await That(report.Settings).IsSameAs(container.Resolve<Settings>())
			.Because("the factory method's Settings parameter is resolved from the graph");
	}

	[Fact]
	public async Task Factory_Scoped_IsCachedPerScopeAndReachedThroughTheContainer()
	{
		using FactoryContainer container = new();
		using IAwaitenScope scope = container.CreateScope();
		using IAwaitenScope other = container.CreateScope();

		await That(scope.Resolve<Session>()).IsSameAs(scope.Resolve<Session>())
			.Because("a scoped factory result is cached once per scope");
		await That(scope.Resolve<Session>()).IsNotSameAs(other.Resolve<Session>())
			.Because("each scope produces its own scoped instance through the instance factory method");
		await That(scope.Resolve<Session>().Origin).IsEqualTo("factory")
			.Because("the scope reached the instance factory through the container, which read its own state");
	}

	[Fact]
	public async Task Instance_HandsBackThePreBuiltMemberAndDoesNotDisposeIt()
	{
		Probe probe = new();
		using (InstanceContainer container = new(probe))
		{
			await That(container.Resolve<Probe>()).IsSameAs(probe)
				.Because("the container exposes the pre-built member it was given");
		}

		await That(probe.Disposed).IsFalse()
			.Because("the container did not construct the probe, so it must not dispose it");
	}

	[Fact]
	public async Task Instance_ResolvedFromAScope_ReturnsTheSameMember()
	{
		Probe probe = new();
		using InstanceContainer container = new(probe);
		using IAwaitenScope scope = container.CreateScope();

		await That(scope.Resolve<Probe>()).IsSameAs(probe)
			.Because("a scope reaches the pre-built member through the container");
	}

	[Fact]
	public async Task Instance_ResolvedFromADisposedContainer_Throws()
	{
		InstanceContainer container = new(new Probe());
		container.Dispose();

		await That(() => container.Resolve<Probe>()).Throws<ObjectDisposedException>()
			.Because("a disposed container rejects all resolution, including pre-built instances");
	}

	[Fact]
	public async Task Factory_DisposableBehindANonDisposableInterface_IsDisposedWithTheContainer()
	{
		DisposableGadget gadget;
		using (GadgetContainer container = new())
		{
			gadget = (DisposableGadget)container.Resolve<IGadget>();
		}

		await That(gadget.Disposed).IsTrue()
			.Because("disposability follows the factory's concrete return type, even when the service interface is not disposable");
	}

	public interface IWidget;

	public sealed class Widget : IWidget
	{
		public Widget(string origin) => Origin = origin;

		public string Origin { get; }
	}

	public sealed class Settings;

	public sealed class Session
	{
		public Session(string origin) => Origin = origin;

		public string Origin { get; }
	}

	public sealed class Report
	{
		public Report(Settings settings) => Settings = settings;

		public Settings Settings { get; }
	}

	[Container]
	[Singleton<Settings>]
	[Singleton<IWidget>(Factory = nameof(MakeWidget))]
	[Transient<Report>(Factory = nameof(MakeReport))]
	[Scoped<Session>(Factory = nameof(MakeSession))]
	public partial class FactoryContainer
	{
		private readonly string _origin = "factory";

		private static Widget MakeWidget() => new("factory");

		// Deliberately an instance method that reads container state, so the scope must reach it through
		// the '__container.' receiver - exercising that emitted path at runtime.
		private Session MakeSession() => new(_origin);

		private static Report MakeReport(Settings settings) => new(settings);
	}

	public sealed class Probe : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	[Container]
	[Singleton<Probe>(Instance = nameof(_probe))]
	public partial class InstanceContainer
	{
		private readonly Probe _probe;

		public InstanceContainer(Probe probe) => _probe = probe;
	}

	public interface IGadget;

	public sealed class DisposableGadget : IGadget, IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	[Container]
	[Singleton<IGadget>(Factory = nameof(MakeGadget))]
	public partial class GadgetContainer
	{
		private static DisposableGadget MakeGadget() => new();
	}
}
