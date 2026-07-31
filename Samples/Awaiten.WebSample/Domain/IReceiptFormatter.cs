namespace Awaiten.WebSample.Domain;

public interface IReceiptFormatter
{
	string Format(Order order);
}
