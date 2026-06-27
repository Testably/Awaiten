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

		// The singleton factory result is cached.
		await That(container.Resolve<IWidget>()).IsSameAs(container.Resolve<IWidget>());
		// The factory was actually used (it stamps a marker the constructor would not).
		await That(((Widget)container.Resolve<IWidget>()).Origin).IsEqualTo("factory");
	}

	[Fact]
	public async Task Factory_ResolvesItsParametersFromTheGraph()
	{
		using FactoryContainer container = new();

		Report report = container.Resolve<Report>();

		await That(report.Settings).IsSameAs(container.Resolve<Settings>());
	}

	[Fact]
	public async Task Instance_HandsBackThePreBuiltMemberAndDoesNotDisposeIt()
	{
		Probe probe = new();
		using (InstanceContainer container = new(probe))
		{
			await That(container.Resolve<Probe>()).IsSameAs(probe);
		}

		// The container did not construct the probe, so it must not dispose it.
		await That(probe.Disposed).IsFalse();
	}

	public interface IWidget;

	public sealed class Widget : IWidget
	{
		public Widget(string origin) => Origin = origin;

		public string Origin { get; }
	}

	public sealed class Settings;

	public sealed class Report
	{
		public Report(Settings settings) => Settings = settings;

		public Settings Settings { get; }
	}

	[Container]
	[Singleton<Settings>]
	[Singleton<IWidget>(Factory = nameof(MakeWidget))]
	[Transient<Report>(Factory = nameof(MakeReport))]
	public partial class FactoryContainer
	{
		private IWidget MakeWidget() => new Widget("factory");

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
}
