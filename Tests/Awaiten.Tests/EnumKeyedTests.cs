using System.Collections.Generic;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of non-string service keys: an enum constant selects a keyed registration through
///     <c>[FromKey(PaymentProvider.Stripe)]</c>, an enum key and the equal string key are distinct, and an
///     <c>IReadOnlyDictionary&lt;TEnum, TService&gt;</c> synthesizes from enum-keyed registrations.
/// </summary>
public partial class EnumKeyedTests
{
	public enum PaymentProvider
	{
		Stripe,
		PayPal,
	}

	[Fact]
	public async Task FromKeyEnum_SelectsTheImplementationRegisteredUnderThatEnumConstant()
	{
		using GatewayContainer.Root container = new();

		Checkout checkout = container.Resolve<Checkout>();

		await That(checkout.Primary).Is<StripeGateway>()
			.Because("the [FromKey(PaymentProvider.Stripe)] parameter resolves the implementation keyed Stripe");
		await That(checkout.Secondary).Is<PayPalGateway>()
			.Because("the [FromKey(PaymentProvider.PayPal)] parameter resolves the implementation keyed PayPal");
	}

	[Fact]
	public async Task EnumKeyAndEqualStringKey_AreDistinctKeys()
	{
		using MixedKeyContainer.Root container = new();

		MixedConsumer consumer = container.Resolve<MixedConsumer>();

		// The enum member PaymentProvider.Stripe and the string "Stripe" are different keys, so they resolve to
		// different implementations rather than silently unifying.
		await That(consumer.ByEnum).Is<StripeGateway>()
			.Because("the enum key selects the enum-keyed registration");
		await That(consumer.ByString).Is<PayPalGateway>()
			.Because("the string \"Stripe\" is a distinct key from the enum PaymentProvider.Stripe");
	}

	[Fact]
	public async Task ResolveWithEnumKey_ThroughTheGenericConvenience_SelectsTheEnumKeyedRegistration()
	{
		using GatewayContainer.Root container = new();

		// The generic convenience takes an object key, so a non-string [Key] (here an enum) is reachable without
		// dropping to the non-generic Resolve(Type, object) and casting.
		await That(container.Resolve<IGateway>(PaymentProvider.Stripe)).Is<StripeGateway>()
			.Because("Resolve<T>(PaymentProvider.Stripe) selects the enum-keyed registration");
		await That(container.TryResolve<IGateway>(PaymentProvider.PayPal, out IGateway? gateway)).IsTrue();
		await That(gateway).Is<PayPalGateway>()
			.Because("TryResolve<T>(PaymentProvider.PayPal, out _) selects the enum-keyed registration");
	}

	[Fact]
	public async Task EnumKeyedDictionary_SynthesizesKeyedByTheEnumConstant()
	{
		using GatewayContainer.Root container = new();

		IReadOnlyDictionary<PaymentProvider, IGateway> gateways =
			container.Resolve<IReadOnlyDictionary<PaymentProvider, IGateway>>();

		await That(gateways).HasCount(2);
		await That(gateways[PaymentProvider.Stripe]).Is<StripeGateway>()
			.Because("the dictionary is keyed by the enum constant the registration used");
		await That(gateways[PaymentProvider.PayPal]).Is<PayPalGateway>()
			.Because("the dictionary is keyed by the enum constant the registration used");
	}

	[Fact]
	public async Task EnumKeyedDictionary_InjectedIntoAConsumer_ResolvesEveryEnumKeyedRegistration()
	{
		using GatewayContainer.Root container = new();

		GatewayRouter router = container.Resolve<GatewayRouter>();

		await That(router.Gateways).HasCount(2);
		await That(router.Gateways[PaymentProvider.PayPal]).Is<PayPalGateway>()
			.Because("an injected enum-keyed dictionary is filled from the enum-keyed registrations");
	}

	public interface IGateway;

	public sealed class StripeGateway : IGateway;

	public sealed class PayPalGateway : IGateway;

	public sealed class Checkout
	{
		public Checkout([FromKey(PaymentProvider.Stripe)] IGateway primary, [FromKey(PaymentProvider.PayPal)] IGateway secondary)
		{
			Primary = primary;
			Secondary = secondary;
		}

		public IGateway Primary { get; }

		public IGateway Secondary { get; }
	}

	public sealed class GatewayRouter
	{
		public GatewayRouter(IReadOnlyDictionary<PaymentProvider, IGateway> gateways) => Gateways = gateways;

		public IReadOnlyDictionary<PaymentProvider, IGateway> Gateways { get; }
	}

	[Container]
	[Singleton<StripeGateway, IGateway>(Key = PaymentProvider.Stripe)]
	[Singleton<PayPalGateway, IGateway>(Key = PaymentProvider.PayPal)]
	[Singleton<Checkout>]
	[Singleton<GatewayRouter>]
	public static partial class GatewayContainer;

	public sealed class MixedConsumer
	{
		public MixedConsumer([FromKey(PaymentProvider.Stripe)] IGateway byEnum, [FromKey("Stripe")] IGateway byString)
		{
			ByEnum = byEnum;
			ByString = byString;
		}

		public IGateway ByEnum { get; }

		public IGateway ByString { get; }
	}

	[Container]
	[Singleton<StripeGateway, IGateway>(Key = PaymentProvider.Stripe)]
	[Singleton<PayPalGateway, IGateway>(Key = "Stripe")]
	[Singleton<MixedConsumer>]
	public static partial class MixedKeyContainer;
}
