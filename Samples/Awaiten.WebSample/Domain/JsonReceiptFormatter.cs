using System.Globalization;

namespace Awaiten.WebSample.Domain;

public sealed class JsonReceiptFormatter : IReceiptFormatter
{
	public string Format(Order order)
		=> $"{{\"drink\":\"{order.Drink}\",\"price\":{order.Price.ToString("0.00", CultureInfo.InvariantCulture)}}}";
}
