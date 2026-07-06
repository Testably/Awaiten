namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of <c>typeof(...)</c> service keys: a type constant selects a keyed registration through
///     <c>[FromKey(typeof(Marker))]</c>, exactly as a string or enum key does, and a type key is distinct from an
///     unrelated one.
/// </summary>
public partial class TypeKeyedTests
{
	[Fact]
	public async Task FromKeyTypeof_SelectsTheImplementationRegisteredUnderThatTypeConstant()
	{
		using GatewayContainer.Root container = new();

		Checkout checkout = container.Resolve<Checkout>();

		await That(checkout.Primary).Is<StripeGateway>()
			.Because("the [FromKey(typeof(Primary))] parameter resolves the implementation keyed typeof(Primary)");
		await That(checkout.Secondary).Is<PayPalGateway>()
			.Because("the [FromKey(typeof(Backup))] parameter resolves the implementation keyed typeof(Backup)");
	}

	public sealed class Primary;

	public sealed class Backup;

	public interface IGateway;

	public sealed class StripeGateway : IGateway;

	public sealed class PayPalGateway : IGateway;

	public sealed class Checkout
	{
		public Checkout([FromKey(typeof(Primary))] IGateway primary, [FromKey(typeof(Backup))] IGateway secondary)
		{
			Primary = primary;
			Secondary = secondary;
		}

		public IGateway Primary { get; }

		public IGateway Secondary { get; }
	}

	[Container]
	[Singleton<StripeGateway, IGateway>(Key = typeof(Primary))]
	[Singleton<PayPalGateway, IGateway>(Key = typeof(Backup))]
	[Singleton<Checkout>]
	public static partial class GatewayContainer;
}
