using Awaiten.WebSample.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Awaiten.WebSample;

/// <summary>
///     The composition root. Everything the application owns is declared here and verified by the compiler;
///     what the host owns is declared external and resolved across the bridge.
/// </summary>
/// <remarks>
///     <c>SyncResolveAfterInit</c> is what makes an async-initialized service usable from ASP.NET at all: the
///     framework binds handler parameters synchronously, so without it <see cref="PriceList" /> would only be
///     reachable as <c>Task&lt;PriceList&gt;</c>. Paired with <c>AddAwaitenInitialization</c> the warm-up has
///     already run by the time the first request arrives, so the synchronous resolve is a cache read and
///     never blocks.
/// </remarks>
[Container(SyncResolveAfterInit = true)]
[ImportService<IConfiguration>]
[ImportService<ILoggerFactory>]
[Singleton<PriceList>]
[Scoped<OrderContext>]
[Transient<OrderService>]
[Singleton<TextReceiptFormatter, IReceiptFormatter>(Key = "text")]
[Singleton<JsonReceiptFormatter, IReceiptFormatter>(Key = "json")]
public static partial class CoffeeShop;
