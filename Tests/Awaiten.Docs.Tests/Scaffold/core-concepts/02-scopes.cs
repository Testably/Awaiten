namespace Snippets;

// The page opens scopes on a container that it does not show.
[Container]
[Singleton<EspressoMachine>]
[Scoped<Order>]
[Scoped<Tab>]
public static partial class CoffeeShop;
