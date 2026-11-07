using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Optional and deferred <c>[Inject]</c> properties of an implementation that <see cref="CrossAssemblyInjectionModule" />
///     builds through its generated factory, for a container in another assembly without <c>InternalsVisibleTo</c>.
/// </summary>
public class ModuleInjectedPropertyTests
{
	[Test]
	public async Task OptionalProperty_IsInjectedWhenTheServiceIsRegistered()
	{
		using InjectionWithClockContainer.Root container = new();

		IInjectedTimer timer = container.Resolve<IInjectedTimer>();

		await That(timer.Clock).IsSameAs(container.Resolve<IInjectedClock>());
	}

	[Test]
	public async Task OptionalProperty_KeepsItsValueWhenTheServiceIsMissing()
	{
		using InjectionContainer.Root container = new();

		await That(container.Resolve<IInjectedTimer>().ClockIsFallback).IsTrue()
			.Because("the container passes null for the missing clock, and the factory then leaves the property as it is");
	}

	[Test]
	public async Task DeferredProperty_BreaksTheCycleAfterConstruction()
	{
		using InjectionContainer.Root container = new();

		IInjectedTimer timer = container.Resolve<IInjectedTimer>();

		await That(timer.Alarm.Timer).IsSameAs(timer)
			.Because("the setter wrapper assigns the alarm after the singleton timer is cached, so the alarm receives that timer");
	}
}

public sealed class InjectedClock : IInjectedClock;

[Container]
[Import(typeof(CrossAssemblyInjectionModule))]
public static partial class InjectionContainer;

[Container]
[Import(typeof(CrossAssemblyInjectionModule))]
[Singleton<InjectedClock, IInjectedClock>]
public static partial class InjectionWithClockContainer;