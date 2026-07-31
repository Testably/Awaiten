using System.Globalization;

namespace Awaiten.WebSample.Domain;

public sealed class TextReceiptFormatter : IReceiptFormatter
{
	public string Format(Order order)
		=> $"{order.Drink} — {order.Price.ToString("0.00", CultureInfo.InvariantCulture)} (#{order.OrderId})";
}
