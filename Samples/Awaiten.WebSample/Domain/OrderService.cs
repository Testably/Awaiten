using System;
using Microsoft.Extensions.Logging;

namespace Awaiten.WebSample.Domain;

/// <summary>
///     A transient mixing all three sources: a scoped Awaiten dependency (<see cref="OrderContext" />), an
///     async-initialized Awaiten singleton (<see cref="PriceList" />), and a host-owned service
///     (<see cref="ILoggerFactory" />) reached across the bridge.
/// </summary>
public sealed partial class OrderService(OrderContext context, PriceList prices, ILoggerFactory loggerFactory)
{
	private readonly ILogger _logger = loggerFactory.CreateLogger<OrderService>();

	public Guid OrderId => context.OrderId;

	public Order Place(string drink)
	{
		Guid orderId = context.OrderId;
		decimal price = prices.PriceOf(drink);
		LogOrderPlaced(orderId, drink, price);
		return new Order(orderId, drink, price);
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId}: {Drink} at {Price}")]
	private partial void LogOrderPlaced(Guid orderId, string drink, decimal price);
}
