namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of container-side property injection via <c>[InjectProperty&lt;TImplementation&gt;]</c>:
///     the implementation stays a plain POCO (no <c>[Inject]</c>), and the container declares which properties to
///     fill. Each entry carries its own <c>Optional</c>/<c>Deferred</c>/<c>Key</c> flags and is filled exactly like
///     an <c>[Inject]</c> property. Because it keys off the implementation type, it also reaches a type brought in
///     by <c>[Scan]</c>.
/// </summary>
public partial class PropertyInjectionContainerSideTests
{
	[Fact]
	public async Task RequiredProperty_DeclaredOnTheContainer_IsFilled()
	{
		using RequiredContainer.Root container = new();

		Report report = container.Resolve<Report>();

		await That(report.Clock).Is<Clock>()
			.Because("a plain POCO property named by [InjectProperty] is filled from the graph like an [Inject] one");
	}

	[Fact]
	public async Task OptionalProperty_WithNoRegistration_IsLeftAtItsDefault()
	{
		using OptionalMissingContainer.Root container = new();

		Report report = container.Resolve<Report>();

		await That(report.Clock).IsNull()
			.Because("an Optional [InjectProperty] whose service type is not registered is left at its default rather than being a missing dependency");
	}

	[Fact]
	public async Task DeferredProperty_BreaksAMutualSingletonCycle()
	{
		using DeferredCycleContainer.Root container = new();

		Left left = container.Resolve<Left>();
		Right right = container.Resolve<Right>();

		await That(left.Right).IsSameAs(right);
		await That(right.Left).IsSameAs(left)
			.Because("both back-references are deferred, so each singleton is cached before it is wired and the mutual cycle terminates");
	}

	[Fact]
	public async Task KeyedProperty_SelectsTheKeyedRegistration()
	{
		using KeyedContainer.Root container = new();

		Consumer consumer = container.Resolve<Consumer>();

		await That(consumer.Primary).Is<FastChannel>();
		await That(consumer.Backup).Is<SlowChannel>()
			.Because("the Key on each [InjectProperty] selects the matching keyed registration");
	}

	[Fact]
	public async Task PropertyOnAScannedType_IsFilled()
	{
		using ScanContainer.Root container = new();

		Widget widget = container.Resolve<Widget>();

		await That(widget.Clock).Is<Clock>()
			.Because("[InjectProperty] keys off the implementation type, so it fills a property on a type registered via [Scan]");
	}

	[Fact]
	public async Task InheritedProperty_DeclaredOnABaseType_IsFilled()
	{
		using InheritedContainer.Root container = new();

		DerivedReport report = container.Resolve<DerivedReport>();

		await That(report.Clock).Is<Clock>()
			.Because("the base-type walk resolves an inherited property, so [InjectProperty] fills a property declared on a base class of the implementation");
	}

	[Fact]
	public async Task ShadowedProperty_TheMostDerivedOneWins()
	{
		using ShadowContainer.Root container = new();

		ShadowDerived instance = container.Resolve<ShadowDerived>();

		await That(instance.Value).Is<Clock>()
			.Because("a new-shadowed property resolves derived-first, so the most-derived declaration is the one filled, consistent with the [Inject] path");
		await That(((ShadowBase)instance).Value).IsNull()
			.Because("the shadowed base property is a distinct member and is left unset");
	}

	[Fact]
	public async Task InitOnlyProperty_NonDeferred_IsFilled()
	{
		using InitOnlyContainer.Root container = new();

		InitConsumer consumer = container.Resolve<InitConsumer>();

		await That(consumer.Clock).Is<Clock>()
			.Because("a non-deferred init-only property is assigned inside the object initializer, so [InjectProperty] fills it (distinct from the init-only + Deferred case, which is AWT144)");
	}

	public sealed class Clock;

	public sealed class Report
	{
		// A plain POCO: no [Inject], the container declares the injection.
		public Clock? Clock { get; set; }
	}

	[Container]
	[Singleton<Clock>]
	[Singleton<Report>]
	[InjectProperty<Report>(nameof(Report.Clock))]
	public static partial class RequiredContainer;

	// Clock is deliberately not registered: the optional property is dropped so the container still compiles.
	[Container]
	[Singleton<Report>]
	[InjectProperty<Report>(nameof(Report.Clock), Optional = true)]
	public static partial class OptionalMissingContainer;

	public sealed class Left
	{
		public Right? Right { get; set; }
	}

	public sealed class Right
	{
		public Left? Left { get; set; }
	}

	[Container]
	[Singleton<Left>]
	[Singleton<Right>]
	[InjectProperty<Left>(nameof(Left.Right), Deferred = true)]
	[InjectProperty<Right>(nameof(Right.Left), Deferred = true)]
	public static partial class DeferredCycleContainer;

	public interface IChannel;

	public sealed class FastChannel : IChannel;

	public sealed class SlowChannel : IChannel;

	public sealed class Consumer
	{
		public IChannel? Primary { get; set; }

		public IChannel? Backup { get; set; }
	}

	[Container]
	[Singleton<FastChannel, IChannel>(Key = "fast")]
	[Singleton<SlowChannel, IChannel>(Key = "slow")]
	[Singleton<Consumer>]
	[InjectProperty<Consumer>(nameof(Consumer.Primary), Key = "fast")]
	[InjectProperty<Consumer>(nameof(Consumer.Backup), Key = "slow")]
	public static partial class KeyedContainer;

	public interface IWidget;

	// A plain POCO discovered by the scan; its Clock is filled by the container-side entry below.
	public sealed class Widget : IWidget
	{
		public Clock? Clock { get; set; }
	}

	[Container]
	[Singleton<Clock>]
	[Scan(typeof(IWidget), Lifetime = AwaitenLifetime.Singleton)]
	[InjectProperty<Widget>(nameof(Widget.Clock))]
	public static partial class ScanContainer;

	public abstract class ReportBase
	{
		// Declared on the base type; the [InjectProperty] entry names it on the derived implementation.
		public Clock? Clock { get; set; }
	}

	public sealed class DerivedReport : ReportBase;

	[Container]
	[Singleton<Clock>]
	[Singleton<DerivedReport>]
	[InjectProperty<DerivedReport>(nameof(DerivedReport.Clock))]
	public static partial class InheritedContainer;

	public class ShadowBase
	{
		public Clock? Value { get; set; }
	}

	public sealed class ShadowDerived : ShadowBase
	{
		// Shadows the base property; the derived-first walk must resolve this one, not the base.
		public new Clock? Value { get; set; }
	}

	[Container]
	[Singleton<Clock>]
	[Singleton<ShadowDerived>]
	[InjectProperty<ShadowDerived>(nameof(ShadowDerived.Value))]
	public static partial class ShadowContainer;

	public sealed class InitConsumer
	{
		// init-only and non-deferred: assigned inside the object initializer.
		public Clock? Clock { get; init; }
	}

	[Container]
	[Singleton<Clock>]
	[Singleton<InitConsumer>]
	[InjectProperty<InitConsumer>(nameof(InitConsumer.Clock))]
	public static partial class InitOnlyContainer;
}
