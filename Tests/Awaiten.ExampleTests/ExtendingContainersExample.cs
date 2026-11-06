using CoffeeShop;

namespace Awaiten.ExampleTests;

/// <summary>
///     The example of the "Extending containers" page: a test container extends the production coffee shop and
///     replaces only the payment gateway, so the barista from the production composition never charges a real card.
/// </summary>
public partial class ExtendingContainersExample
{
	[Test]
	public async Task Barista_ChargesThePrice()
	{
		using TestCoffeeShop.Root shop = new();

		Receipt receipt = shop.Resolve<Barista>().Serve(3.50m);

		await That(receipt.Amount).IsEqualTo(3.50m);
	}

	public sealed class CardGateway : IPaymentGateway
	{
		public Receipt Charge(decimal amount) => throw new InvalidOperationException("A test must not charge a real card.");
	}

	public sealed class FakeGateway : IPaymentGateway
	{
		public Receipt Charge(decimal amount) => new(amount);
	}

	[Container]
	[Singleton<CardGateway, IPaymentGateway>]
	[Singleton<Barista>]
	public static partial class ProductionCoffeeShop;

	[Container(Extends = typeof(ProductionCoffeeShop))]
	[Singleton<FakeGateway, IPaymentGateway>]
	public static partial class TestCoffeeShop;
}
