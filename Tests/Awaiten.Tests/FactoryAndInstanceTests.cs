namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of the factory-method and pre-built-instance registrations: a <c>Factory</c>
///     registration produces the service by calling a static container method (respecting the declared
///     lifetime), and an <c>Instance</c> registration hands back a static member the container does not own.
///     The containers and services are nested types, so the enclosing class is <c>partial</c>.
/// </summary>
public partial class FactoryAndInstanceTests
{
	[Fact]
	public async Task Factory_ProducesTheServiceAndIsCachedAccordingToLifetime()
	{
		using FactoryContainer.Root container = new();

		await That(container.Resolve<IWidget>()).IsSameAs(container.Resolve<IWidget>())
			.Because("the singleton factory result is cached");
		await That(((Widget)container.Resolve<IWidget>()).Origin).IsEqualTo("factory")
			.Because("the factory method produced the instance, which a constructor would not have marked");
	}

	[Fact]
	public async Task Factory_ResolvesItsParametersFromTheGraph()
	{
		using FactoryContainer.Root container = new();

		Report report = container.Resolve<Report>();

		await That(report.Settings).IsSameAs(container.Resolve<Settings>())
			.Because("the factory method's Settings parameter is resolved from the graph");
	}

	[Fact]
	public async Task Factory_Scoped_IsCachedPerScopeAndReachedByName()
	{
		using FactoryContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();
		using IAwaitenScope other = container.CreateScope();

		await That(scope.Resolve<Session>()).IsSameAs(scope.Resolve<Session>())
			.Because("a scoped factory result is cached once per scope");
		await That(scope.Resolve<Session>()).IsNotSameAs(other.Resolve<Session>())
			.Because("each scope produces its own scoped instance through the factory method");
		await That(scope.Resolve<Session>().Origin).IsEqualTo("factory")
			.Because("the scope reached the static factory, which read the container's static state");
	}

	[Fact]
	public async Task Instance_HandsBackThePreBuiltMemberAndDoesNotDisposeIt()
	{
		Probe probe;
		using (InstanceContainer.Root container = new())
		{
			probe = container.Resolve<Probe>();
			await That(probe).IsSameAs(InstanceContainer.Probe)
				.Because("the container exposes the pre-built static member");
		}

		await That(probe.Disposed).IsFalse()
			.Because("the container did not construct the probe, so it must not dispose it");
	}

	[Fact]
	public async Task Instance_ResolvedFromAScope_ReturnsTheSameMember()
	{
		using InstanceContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();

		await That(scope.Resolve<Probe>()).IsSameAs(InstanceContainer.Probe)
			.Because("a scope reaches the pre-built member through the root");
	}

	[Fact]
	public async Task Instance_ResolvedFromADisposedContainer_Throws()
	{
		InstanceContainer.Root container = new();
		container.Dispose();

		await That(() => container.Resolve<Probe>()).Throws<ObjectDisposedException>()
			.Because("a disposed container rejects all resolution, including pre-built instances");
	}

	[Fact]
	public async Task Factory_RegisteredUnderSeveralServices_SharesOneInstance()
	{
		using SharedFactoryContainer.Root container = new();

		await That((object)container.Resolve<IRead>()).IsSameAs(container.Resolve<IWrite>())
			.Because("the same implementation behind two services with the same factory is coalesced into one instance");
	}

	[Fact]
	public async Task Instance_RegisteredUnderSeveralServices_ExposesOneMember()
	{
		using SharedInstanceContainer.Root container = new();

		await That((object)container.Resolve<IRead>()).IsSameAs(SharedInstanceContainer.Store)
			.Because("the pre-built member is exposed as the first service");
		await That((object)container.Resolve<IWrite>()).IsSameAs(SharedInstanceContainer.Store)
			.Because("the same pre-built member is exposed as every service it is registered under");
	}

	[Fact]
	public async Task Factory_Scoped_RegisteredUnderSeveralServices_SharesOnePerScope()
	{
		using SharedScopedFactoryContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();
		using IAwaitenScope other = container.CreateScope();

		await That((object)scope.Resolve<IRead>()).IsSameAs(scope.Resolve<IWrite>())
			.Because("the same scoped implementation behind two services with one factory is coalesced per scope");
		await That((object)scope.Resolve<IRead>()).IsNotSameAs(other.Resolve<IRead>())
			.Because("each scope produces its own shared instance");
	}

	[Fact]
	public async Task Factory_DisposableBehindANonDisposableInterface_IsDisposedWithTheContainer()
	{
		DisposableGadget gadget;
		using (GadgetContainer.Root container = new())
		{
			gadget = (DisposableGadget)container.Resolve<IGadget>();
		}

		await That(gadget.Disposed).IsTrue()
			.Because("disposability follows the factory's concrete return type, even when the service interface is not disposable");
	}

	[Fact]
	public async Task Factory_SingletonReturningInterfaceButBuildingADisposable_IsDisposedWithTheContainer()
	{
		HiddenDisposable hidden;
		using (HiddenDisposableContainer.Root container = new())
		{
			hidden = (HiddenDisposable)container.Resolve<IHidden>();
		}

		await That(hidden.DisposeCount).IsEqualTo(1)
			.Because("a singleton factory declared to return the non-disposable interface still builds a concrete IDisposable, which the container must dispose exactly once (not double-tracked by the runtime check)");
	}

	[Fact]
	public async Task Factory_ScopedReturningInterfaceButBuildingADisposable_IsDisposedWithTheScope()
	{
		using HiddenDisposableContainer.Root container = new();
		HiddenDisposable hidden;
		using (IAwaitenScope scope = container.CreateScope())
		{
			hidden = (HiddenDisposable)scope.Resolve<IScopedHidden>();
			await That(hidden.DisposeCount).IsEqualTo(0)
				.Because("the scope still owns the instance");
		}

		await That(hidden.DisposeCount).IsEqualTo(1)
			.Because("a scoped factory declared to return the non-disposable interface still builds a concrete IDisposable, which the scope must dispose exactly once");
	}

	[Fact]
	public async Task Factory_TransientReturningInterfaceButBuildingADisposable_IsDisposedWithTheContainer()
	{
		HiddenDisposable hidden;
		using (HiddenDisposableContainer.Root container = new())
		{
			hidden = (HiddenDisposable)container.Resolve<ITransientHidden>();
		}

		await That(hidden.DisposeCount).IsEqualTo(1)
			.Because("a transient factory declared to return the non-disposable interface still builds a concrete IDisposable, which the owner must dispose exactly once");
	}

	[Fact]
	public async Task Factory_ReturnTypeAlreadyDisposable_IsStillDisposed_NoRegression()
	{
		PlainDisposable plain;
		using (HiddenDisposableContainer.Root container = new())
		{
			plain = container.Resolve<PlainDisposable>();
		}

		await That(plain.DisposeCount).IsEqualTo(1)
			.Because("a factory whose declared return type already implements IDisposable is tracked - and disposed exactly once - as before");
	}

	[Fact]
	public async Task Factory_ReturningInterfaceButBuildingANonDisposable_IsNotRetained()
	{
		// A non-disposable factory output must not be retained: resolving it (and disposing the container)
		// must not throw, and the output is simply not tracked. The interesting half is that the generated
		// runtime `is IDisposable` check leaves it untracked rather than mis-casting it.
		using (HiddenDisposableContainer.Root container = new())
		{
			object plain = container.Resolve<IPlain>();
			await That(plain).IsNotNull()
				.Because("a non-disposable factory output resolves normally");
		}
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
	public static partial class FactoryContainer
	{
		private static readonly string _origin = "factory";

		private static Widget MakeWidget() => new("factory");

		// A static factory that reads the container's static state; a scope reaches it by simple name.
		private static Session MakeSession() => new(_origin);

		private static Report MakeReport(Settings settings) => new(settings);
	}

	public sealed class Probe : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	[Container]
	[Singleton<Probe>(Instance = nameof(Probe))]
	public static partial class InstanceContainer
	{
		// A pre-built instance is a static member the container hands back but never owns or disposes.
		internal static readonly Probe Probe = new();
	}

	public interface IGadget;

	public sealed class DisposableGadget : IGadget, IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	[Container]
	[Singleton<IGadget>(Factory = nameof(MakeGadget))]
	public static partial class GadgetContainer
	{
		private static DisposableGadget MakeGadget() => new();
	}

	public interface IHidden;

	public interface IScopedHidden;

	public interface ITransientHidden;

	public interface IPlain;

	// A concrete IDisposable behind a non-disposable service interface: the factory's *declared* return type
	// is the interface, so static disposability analysis misses it - the container must track it at runtime.
	// DisposeCount proves the runtime check tracks the instance exactly once, not twice.
	public sealed class HiddenDisposable : IHidden, IScopedHidden, ITransientHidden, IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	// A factory whose declared return type already implements IDisposable: the no-regression baseline.
	public sealed class PlainDisposable : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}

	// A non-disposable factory output behind an interface: the runtime check must leave it untracked.
	public sealed class PlainImpl : IPlain;

	[Container]
	[Singleton<IHidden>(Factory = nameof(MakeHidden))]
	[Scoped<IScopedHidden>(Factory = nameof(MakeScopedHidden))]
	[Transient<ITransientHidden>(Factory = nameof(MakeTransientHidden))]
	[Singleton<PlainDisposable>(Factory = nameof(MakePlainDisposable))]
	[Transient<IPlain>(Factory = nameof(MakePlain))]
	public static partial class HiddenDisposableContainer
	{
#pragma warning disable CA1859
		// Each factory is declared to return the non-disposable interface yet builds the concrete IDisposable.
		private static IHidden MakeHidden() => new HiddenDisposable();

		private static IScopedHidden MakeScopedHidden() => new HiddenDisposable();

		private static ITransientHidden MakeTransientHidden() => new HiddenDisposable();

		private static PlainDisposable MakePlainDisposable() => new();

		private static IPlain MakePlain() => new PlainImpl();
#pragma warning restore CA1859
	}

	public interface IRead;

	public interface IWrite;

	public sealed class Store : IRead, IWrite;

	[Container]
	[Singleton<Store, IRead>(Factory = nameof(MakeStore))]
	[Singleton<Store, IWrite>(Factory = nameof(MakeStore))]
	public static partial class SharedFactoryContainer
	{
		private static Store MakeStore() => new();
	}

	[Container]
	[Scoped<Store, IRead>(Factory = nameof(MakeStore))]
	[Scoped<Store, IWrite>(Factory = nameof(MakeStore))]
	public static partial class SharedScopedFactoryContainer
	{
		private static Store MakeStore() => new();
	}

	[Container]
	[Singleton<Store, IRead>(Instance = nameof(Store))]
	[Singleton<Store, IWrite>(Instance = nameof(Store))]
	public static partial class SharedInstanceContainer
	{
		// A pre-built instance shared across both services it is registered under.
		internal static readonly Store Store = new();
	}
}
