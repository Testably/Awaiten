global using static Snippets.Prelude;
using Microsoft.Extensions.DependencyInjection;

namespace Snippets;

// The host's variables, which the page takes from the surrounding startup code.
public static class Prelude
{
	public static IServiceCollection services = default!;
	public static IServiceProvider provider = default!;
	public static Microsoft.AspNetCore.Builder.WebApplicationBuilder builder = default!;
	public static IExternalResolver myResolver = default!;
}

// The page bridges a container that it does not show.
[Container]
[Singleton<EspressoBrewer, IBrewer>]
[Singleton<EspressoMachine>]
public static partial class CoffeeShop;
