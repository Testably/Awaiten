using System;

namespace Awaiten.WebSample.Domain;

public sealed record Order(Guid OrderId, string Drink, decimal Price);
