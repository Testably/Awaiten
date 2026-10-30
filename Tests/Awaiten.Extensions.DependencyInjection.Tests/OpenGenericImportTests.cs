using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed partial class OpenGenericImportTests
{
	public sealed class OrderService(ILogger<OrderService> logger)
	{
		public ILogger<OrderService> Logger { get; } = logger;
	}

	public sealed class PaymentService(ILogger<PaymentService> logger)
	{
		public ILogger<PaymentService> Logger { get; } = logger;
	}

	[Container]
	[ImportService(typeof(ILogger<>))]
	[Singleton<OrderService>]
	[Singleton<PaymentService>]
	public static partial class LoggingContainer;

	[Test]
	public async Task OpenImport_ResolvesEachClosingFromTheHostProvider()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddAwaitenContainer<LoggingContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		OrderService order = provider.GetRequiredService<OrderService>();
		PaymentService payment = provider.GetRequiredService<PaymentService>();

		await That(order.Logger).IsSameAs(provider.GetRequiredService<ILogger<OrderService>>())
			.Because("ILogger<OrderService> is a closing of the imported ILogger<>, so the host's logger is used");
		await That(payment.Logger).IsSameAs(provider.GetRequiredService<ILogger<PaymentService>>())
			.Because("the one open import covers every consumer's closing");
	}

	[Test]
	public async Task OpenImport_VerificationAcceptsAHostThatProvidesTheClosings()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddAwaitenContainer<LoggingContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).DoesNotThrow()
			.Because("the host's open ILogger<> registration serves every advertised closing");
	}

	[Test]
	public async Task OpenImport_VerificationNamesAMissingClosing()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<LoggingContainer.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(() => provider.VerifyAwaitenContainers()).Throws<InvalidOperationException>()
			.WithMessage("*ILogger*OrderService*").AsWildcard()
			.Because("each consumed closing is advertised, so verification names the closing the host lacks");
	}
}
