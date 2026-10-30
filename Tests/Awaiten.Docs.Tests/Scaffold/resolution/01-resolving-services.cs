namespace Snippets;

// The page resolves from a container that it does not show.
[Container]
[Transient<Cup>]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<WholeMilk, IMilk>(Key = MilkKind.Whole)]
[Singleton<SoyMilk, IMilk>(Key = "Soy")]
public static partial class CoffeeShop;
