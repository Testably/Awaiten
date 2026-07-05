namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of contextual binding: a <c>WhenInjectedInto</c> registration is used only when injected
///     into the named consumer, while every other consumer (and the public resolution) gets the unconditional
///     registration. An explicit <c>[FromKey]</c> on a parameter still wins over a contextual binding; an
///     <c>[Inject]</c> property picks it up like a constructor parameter; and a decorator on the service wraps the
///     contextual implementation too. The containers and services are nested types, so the enclosing class is
///     <c>partial</c>.
/// </summary>
public partial class ConditionalTests
{
	[Fact]
	public async Task WhenInjectedInto_SelectsTheContextualImplementationForThatConsumerOnly()
	{
		using ConditionalContainer.Root container = new();

		await That(container.Resolve<NeedsTest>().Clock).Is<TestClock>()
			.Because("the WhenInjectedInto binding redirects IClock to TestClock inside NeedsTest");
		await That(container.Resolve<NeedsDefault>().Clock).Is<DefaultClock>()
			.Because("every other consumer gets the unconditional DefaultClock");
		await That(container.Resolve<IClock>()).Is<DefaultClock>()
			.Because("the contextual implementation is not exposed on the public resolution");
	}

	[Fact]
	public async Task FromKey_TakesPrecedenceOverAContextualBinding()
	{
		using PrecedenceContainer.Root container = new();

		NeedsBoth consumer = container.Resolve<NeedsBoth>();

		await That(consumer.Keyed).Is<SpecialClock>()
			.Because("an explicit [FromKey] selects the keyed registration over the contextual binding");
		await That(consumer.Contextual).Is<TestClock>()
			.Because("the unkeyed parameter still picks up the WhenInjectedInto binding for this consumer");
	}

	[Fact]
	public async Task WhenInjectedInto_FillsAnInjectPropertyWithTheContextualImplementation()
	{
		using PropertyContainer.Root container = new();

		await That(container.Resolve<NeedsTestProperty>().Clock).Is<TestClock>()
			.Because("an [Inject] property redirects to the contextual binding like a constructor parameter");
	}

	[Fact]
	public async Task WhenInjectedInto_WrapsTheContextualImplementationInTheServicesDecorator()
	{
		using DecoratedContainer.Root container = new();

		IClock injected = container.Resolve<NeedsTest>().Clock;
		IClock other = container.Resolve<NeedsDefault>().Clock;

		await That(injected).Is<ClockDecorator>()
			.Because("the contextual binding is wrapped by the same decorator chain as the default");
		await That(((ClockDecorator)injected).Inner).Is<TestClock>()
			.Because("the decorator wraps the contextual implementation, not the default");
		await That(((ClockDecorator)other).Inner).Is<DefaultClock>()
			.Because("every other consumer gets the decorated default");
	}

	public interface IClock;

	public sealed class DefaultClock : IClock;

	public sealed class TestClock : IClock;

	public sealed class SpecialClock : IClock;

	public sealed class NeedsDefault
	{
		public NeedsDefault(IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	public sealed class NeedsTest
	{
		public NeedsTest(IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	public sealed class NeedsBoth
	{
		public NeedsBoth([FromKey("special")] IClock keyed, IClock contextual)
		{
			Keyed = keyed;
			Contextual = contextual;
		}

		public IClock Keyed { get; }

		public IClock Contextual { get; }
	}

	public sealed class NeedsTestProperty
	{
		[Inject]
		public IClock? Clock { get; set; }
	}

	public sealed class ClockDecorator : IClock
	{
		public ClockDecorator(IClock inner) => Inner = inner;

		public IClock Inner { get; }
	}

	[Container]
	[Singleton<DefaultClock, IClock>]
	[Singleton<TestClock, IClock>(WhenInjectedInto = typeof(NeedsTest))]
	[Singleton<NeedsDefault>]
	[Singleton<NeedsTest>]
	public static partial class ConditionalContainer;

	[Container]
	[Singleton<DefaultClock, IClock>]
	[Singleton<SpecialClock, IClock>(Key = "special")]
	[Singleton<TestClock, IClock>(WhenInjectedInto = typeof(NeedsBoth))]
	[Singleton<NeedsBoth>]
	public static partial class PrecedenceContainer;

	[Container]
	[Singleton<DefaultClock, IClock>]
	[Singleton<TestClock, IClock>(WhenInjectedInto = typeof(NeedsTestProperty))]
	[Singleton<NeedsTestProperty>]
	public static partial class PropertyContainer;

	[Container]
	[Singleton<DefaultClock, IClock>]
	[Singleton<TestClock, IClock>(WhenInjectedInto = typeof(NeedsTest))]
	[Decorate<ClockDecorator, IClock>]
	[Singleton<NeedsDefault>]
	[Singleton<NeedsTest>]
	public static partial class DecoratedContainer;
}
