using System;
using System.Net;
using Awaiten.Extensions.DependencyInjection;
using Awaiten.WebSample;
using Awaiten.WebSample.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using CoffeeShop = Awaiten.WebSample.CoffeeShop;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The whole integration, in two lines. The factory projects the generated container into the host's service
// collection, so framework registrations and Awaiten-owned services resolve side by side; the initialization
// hosted service warms the async-initialized singletons before the host starts serving.
builder.Host.UseServiceProviderFactory(new AwaitenServiceProviderFactory<CoffeeShop.Root>());
builder.Services.AddAwaitenInitialization<CoffeeShop.Root>();

bool smoke = args.Contains("--smoke");
if (smoke)
{
	builder.WebHost.UseUrls(new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(), 0).ToString());
}

WebApplication app = builder.Build();

// The runtime counterpart of the compile-time missing-dependency check, across the host boundary: the
// container's [ImportService<T>] dependencies must actually be registered. Throws and names them if not.
app.Services.VerifyAwaitenContainers();

// An async-initialized singleton, bound synchronously because the container opted into SyncResolveAfterInit
// and the hosted service already warmed it.
app.MapGet("/menu", (PriceList prices) => Results.Ok(new
{
	warm = prices.IsWarm,
	drinks = prices.Drinks,
}));

// OrderService is transient and depends on the scoped OrderContext. ASP.NET opens an MS.DI scope per
// request and the bridge aligns an Awaiten scope to it, so both instances below share one OrderContext —
// and the next request gets a different one.
app.MapGet("/orders/{drink}", (string drink, OrderService first, OrderService second) => Results.Ok(new
{
	order = first.Place(drink),
	sameScopePerRequest = first.OrderId == second.OrderId,
	distinctTransients = !ReferenceEquals(first, second),
}));

// A keyed Awaiten registration is projected under its key, so [FromKeyedServices] reaches it.
app.MapGet("/receipt/{format}/{drink}", (
	string format,
	string drink,
	OrderService orders,
	[FromKeyedServices("text")] IReceiptFormatter text,
	[FromKeyedServices("json")] IReceiptFormatter json) =>
{
	Order order = orders.Place(drink);
	return Results.Text(format == "json" ? json.Format(order) : text.Format(order));
});

if (!smoke)
{
	app.Run();
	return 0;
}

return await SmokeTest.RunAsync(app).ConfigureAwait(false);
