using System.Collections.Generic;
using System.Linq;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     A <c>Factory</c> or <c>Instance</c> registration that names only the service is identified by its member, so
///     two of them for one service are two implementations: the container's registration wins over a module's, the
///     first wins within one container, and both are members of the collection, exactly as for two constructed
///     classes. Registrations naming the same member stay one shared implementation.
/// </summary>
public partial class FactoryIdentityTests
{
	public class OverAModule
	{
		[Test]
		public async Task ContainerInstance_WinsOverAModuleFactory()
		{
			using InstanceOverModuleContainer.Root container = new();

			await That(container.Resolve<IClock>()).IsSameAs(InstanceOverModuleContainer.Fixed)
				.Because("the container's registration takes precedence over the module's");
			await That(Names(container.Resolve<IEnumerable<IClock>>())).IsEqualTo(["fixed", "module",])
				.Because("the module's factory is a different implementation, so it stays a member of the collection after the container's");
		}

		[Test]
		public async Task ContainerFactory_WinsOverAModuleFactoryOfTheSameName()
		{
			using FactoryOverModuleContainer.Root container = new();

			await That(container.Resolve<IClock>().Name).IsEqualTo("container")
				.Because("the container's Create wins over the module's Create");
			await That(Names(container.Resolve<IEnumerable<IClock>>())).IsEqualTo(["container", "module",])
				.Because("the two members of the same name are different factories, so both are collection members");
		}

		[Test]
		public async Task ContainerInstance_WinsOverACrossAssemblyModuleFactory()
		{
			using CrossAssemblyOverrideContainer.Root container = new();

			await That(container.Resolve<SupportGreeter>()).IsSameAs(CrossAssemblyOverrideContainer.Greeter)
				.Because("the container's Instance takes precedence over the referenced module's factory");
			await That(container.Resolve<SupportGreeter[]>().Select(greeter => greeter.Greeting)).IsEqualTo(["hello from the container", "hello from the support module",])
				.Because("the module's factory is still called across the assembly boundary for the collection");
		}
	}

	public class InOneContainer
	{
		[Test]
		public async Task TwoFactories_FirstWinsAndBothAreCollectionMembers()
		{
			using TwoFactoriesContainer.Root container = new();

			await That(container.Resolve<IClock>().Name).IsEqualTo("a")
				.Because("the first registration wins single resolution");
			await That(Names(container.Resolve<IEnumerable<IClock>>())).IsEqualTo(["a", "b",])
				.Because("each factory is its own implementation, so both are collection members in registration order");
		}

		[Test]
		public async Task TwoFactoriesOfAConcreteService_FirstWinsAndBothAreCollectionMembers()
		{
			using ConcreteFactoriesContainer.Root container = new();

			await That(container.Resolve<Clock>().Name).IsEqualTo("a")
				.Because("the first registration wins single resolution");
			await That(Names(container.Resolve<Clock[]>())).IsEqualTo(["a", "b",])
				.Because("neither registration names an implementation type, so each factory is its own implementation");
		}

		[Test]
		public async Task TwoFactories_KeepTheirOwnLifetimes()
		{
			using MixedLifetimeContainer.Root container = new();

			IClock[] first = container.Resolve<IClock[]>();
			IClock[] second = container.Resolve<IClock[]>();

			await That(first[0]).IsSameAs(second[0])
				.Because("the singleton factory's instance is cached");
			await That(first[1]).IsNotSameAs(second[1])
				.Because("the transient factory produces a new instance per resolution");
		}

		[Test]
		public async Task TwoFactories_AreBothDisposedUnlessOneSuppressesDisposal()
		{
			Clock owned;
			Clock suppressed;
			using (SuppressDisposalContainer.Root container = new())
			{
				Clock[] clocks = container.Resolve<IEnumerable<IClock>>().Cast<Clock>().ToArray();
				owned = clocks[0];
				suppressed = clocks[1];
			}

			await That(owned.Disposed).IsTrue()
				.Because("the container owns what the first factory produced");
			await That(suppressed.Disposed).IsFalse()
				.Because("the second factory opted out of disposal, which applies to its own implementation only");
		}

		[Test]
		public async Task Decorator_WrapsEachFactory()
		{
			using DecoratedFactoriesContainer.Root container = new();

			await That(container.Resolve<IClock>().Name).IsEqualTo("logged a")
				.Because("the single resolution is the decorated first factory");
			await That(Names(container.Resolve<IEnumerable<IClock>>())).IsEqualTo(["logged a", "logged b",])
				.Because("each factory is a separate base implementation, so each is decorated");
		}
	}

	public class KeyedAndContextual
	{
		[Test]
		public async Task KeyedFactories_ResolveTheirOwnMember()
		{
			using KeyedFactoriesContainer.Root container = new();

			await That(container.Resolve<IClock>("a").Name).IsEqualTo("a");
			await That(container.Resolve<IClock>("b").Name).IsEqualTo("b");
			await That(container.Resolve<IReadOnlyDictionary<string, IClock>>().Values.Select(clock => clock.Name)).IsEqualTo(["a", "b",])
				.Because("each key is produced by its own factory");
		}

		[Test]
		public async Task ContextualFactory_ReachesOnlyItsConsumer()
		{
			using ContextualFactoryContainer.Root container = new();

			await That(container.Resolve<Report>().Clock.Name).IsEqualTo("b")
				.Because("the named consumer receives the contextual factory's clock");
			await That(container.Resolve<IClock>().Name).IsEqualTo("a")
				.Because("every other resolution keeps the unconditional factory");
		}

		[Test]
		public async Task TheSameFactoryUnderSeveralKeys_SharesOneInstance()
		{
			using SharedMemberContainer.Root container = new();

			await That(container.Resolve<IClock>("k")).IsSameAs(container.Resolve<IClock>())
				.Because("registrations naming the same factory are one singleton, shared by every key it is registered under");
			await That(container.Resolve<IClock[]>()).HasCount(1)
				.Because("the shared implementation is a single collection member");
		}
	}

	private static string[] Names(IEnumerable<IClock> clocks) => clocks.Select(clock => clock.Name).ToArray();

	public interface IClock
	{
		string Name { get; }
	}

	public sealed class Clock : IClock, IDisposable
	{
		public Clock(string name) => Name = name;

		public string Name { get; }

		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public sealed class LoggedClock : IClock
	{
		public LoggedClock(IClock inner) => Name = "logged " + inner.Name;

		public string Name { get; }
	}

	public sealed class Report
	{
		public Report(IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	[Module]
	[Singleton<IClock>(Factory = nameof(Create))]
	public static class ClockModule
	{
		public static IClock Create() => new Clock("module");
	}

	[Container]
	[Import(typeof(ClockModule))]
	[Singleton<IClock>(Instance = nameof(Fixed))]
	public static partial class InstanceOverModuleContainer
	{
		public static IClock Fixed { get; } = new Clock("fixed");
	}

	[Container]
	[Import(typeof(ClockModule))]
	[Singleton<IClock>(Factory = nameof(Create))]
	public static partial class FactoryOverModuleContainer
	{
		public static IClock Create() => new Clock("container");
	}

	[Container]
	[Import(typeof(CrossAssemblySupportModule))]
	[Singleton<SupportGreeter>(Instance = nameof(Greeter))]
	public static partial class CrossAssemblyOverrideContainer
	{
		public static SupportGreeter Greeter { get; } = new("hello from the container");
	}

	[Container]
	[Singleton<IClock>(Factory = nameof(CreateA))]
	[Singleton<IClock>(Factory = nameof(CreateB))]
	public static partial class TwoFactoriesContainer
	{
		public static IClock CreateA() => new Clock("a");

		public static IClock CreateB() => new Clock("b");
	}

	[Container]
	[Singleton<Clock>(Factory = nameof(CreateA))]
	[Singleton<Clock>(Factory = nameof(CreateB))]
	public static partial class ConcreteFactoriesContainer
	{
		public static Clock CreateA() => new("a");

		public static Clock CreateB() => new("b");
	}

	[Container]
	[Singleton<IClock>(Factory = nameof(CreateA))]
	[Transient<IClock>(Factory = nameof(CreateB))]
	public static partial class MixedLifetimeContainer
	{
		public static IClock CreateA() => new Clock("a");

		public static IClock CreateB() => new Clock("b");
	}

	[Container]
	[Singleton<IClock>(Factory = nameof(CreateA))]
	[Singleton<IClock>(Factory = nameof(CreateB), SuppressDisposal = true)]
	public static partial class SuppressDisposalContainer
	{
		public static Clock CreateA() => new("a");

		public static Clock CreateB() => new("b");
	}

	[Container]
	[Singleton<IClock>(Factory = nameof(CreateA))]
	[Singleton<IClock>(Factory = nameof(CreateB))]
	[Decorate<LoggedClock, IClock>]
	public static partial class DecoratedFactoriesContainer
	{
		public static IClock CreateA() => new Clock("a");

		public static IClock CreateB() => new Clock("b");
	}

	[Container]
	[Singleton<IClock>(Factory = nameof(CreateA), Key = "a")]
	[Singleton<IClock>(Factory = nameof(CreateB), Key = "b")]
	public static partial class KeyedFactoriesContainer
	{
		public static IClock CreateA() => new Clock("a");

		public static IClock CreateB() => new Clock("b");
	}

	[Container]
	[Singleton<IClock>(Factory = nameof(CreateA))]
	[Singleton<IClock>(Factory = nameof(CreateB), WhenInjectedInto = typeof(Report))]
	[Transient<Report>]
	public static partial class ContextualFactoryContainer
	{
		public static IClock CreateA() => new Clock("a");

		public static IClock CreateB() => new Clock("b");
	}

	[Container]
	[Singleton<IClock>(Factory = nameof(Create))]
	[Singleton<IClock>(Factory = nameof(Create), Key = "k")]
	public static partial class SharedMemberContainer
	{
		public static IClock Create() => new Clock("shared");
	}
}
