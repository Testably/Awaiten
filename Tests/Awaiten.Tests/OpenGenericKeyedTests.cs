namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of keyed open generic registration: several open generic implementations share one
///     open service under different keys, and a consumer selects one closed instance with <c>[FromKey]</c>.
///     The key declared on the open registration flows onto every closed implementation expanded from it. The
///     containers and services are nested types, so the enclosing class is <c>partial</c>.
/// </summary>
public partial class OpenGenericKeyedTests
{
	[Fact]
	public async Task FromKey_SelectsTheOpenRegistrationExpandedUnderThatKey()
	{
		using KeyedOpenGenericContainer.Root container = new();

		Consumer consumer = container.Resolve<Consumer>();

		await That(consumer.Fast).Is<FastRepository<Order>>()
			.Because("the [FromKey(\"fast\")] parameter resolves the open registration keyed 'fast', closed at Order");
		await That(consumer.Slow).Is<SlowRepository<Order>>()
			.Because("the [FromKey(\"slow\")] parameter resolves the open registration keyed 'slow', closed at Order");
	}

	public sealed class Order;

	public interface IRepository<T>;

	public sealed class FastRepository<T> : IRepository<T>;

	public sealed class SlowRepository<T> : IRepository<T>;

	public sealed class Consumer
	{
		public Consumer([FromKey("fast")] IRepository<Order> fast, [FromKey("slow")] IRepository<Order> slow)
		{
			Fast = fast;
			Slow = slow;
		}

		public IRepository<Order> Fast { get; }

		public IRepository<Order> Slow { get; }
	}

	[Container]
	[Transient(typeof(FastRepository<>), typeof(IRepository<>), Key = "fast")]
	[Transient(typeof(SlowRepository<>), typeof(IRepository<>), Key = "slow")]
	[Transient<Consumer>]
	public static partial class KeyedOpenGenericContainer;
}
