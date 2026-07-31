# Awaiten web sample

An ASP.NET Core minimal-API app whose composition root is an Awaiten container. It exists to prove the
[MS.DI bridge](../../Docs/pages/08-msdi-bridge.md) end to end against a real host, not just in unit tests:
every claim the bridge documentation makes is exercised here by a request that actually goes over HTTP.

## Run it

```sh
dotnet run --project Samples/Awaiten.WebSample
```

```sh
curl http://localhost:5000/menu
curl http://localhost:5000/orders/Espresso
curl http://localhost:5000/receipt/text/Latte
curl http://localhost:5000/receipt/json/Latte
```

## Verify it

```sh
dotnet run --project Samples/Awaiten.WebSample -- --smoke
```

Starts the host on an ephemeral port, drives every endpoint, and exits non-zero if any check fails. This is
what makes the sample a test rather than a demo — `Awaiten.AotSample` does the same for native AOT.

## What it demonstrates

| Bridge feature                                                                                | Where                                                 |
|-----------------------------------------------------------------------------------------------|-------------------------------------------------------|
| `AwaitenServiceProviderFactory<TRoot>` as the host's provider factory                         | `Program.cs`                                          |
| Framework and Awaiten services resolving side by side                                         | every endpoint takes both                             |
| One Awaiten scope aligned to each request's MS.DI scope                                       | `/orders/{drink}` reports `sameScopePerRequest`       |
| Transients fresh per resolution inside a shared scope                                         | `/orders/{drink}` reports `distinctTransients`        |
| `AddAwaitenInitialization` warming async singletons before serving                            | `/menu` reports `warm: true` on the first request     |
| `[Container(SyncResolveAfterInit = true)]` so ASP.NET can bind an async service synchronously | `CoffeeShop`                                          |
| `[ImportService<T>]` pulling host services into the graph                                     | `IConfiguration`, `ILoggerFactory`                    |
| `VerifyAwaitenContainers()` checking those imports at startup                                 | `Program.cs`, right after `Build()`                   |
| Keyed registrations projected under their key                                                 | `/receipt/{format}/{drink}` via `[FromKeyedServices]` |

## The shape worth copying

`PriceList` is the point of the whole exercise. It needs asynchronous setup, and ASP.NET binds handler
parameters synchronously — normally an impossible pairing. Awaiten resolves it by warming the singleton
during host startup (`AddAwaitenInitialization`) and then allowing the synchronous resolve
(`SyncResolveAfterInit`). By the time a request arrives the resolve is a cache read, so it never blocks.
Without the warm-up the first synchronous resolve would block the request thread instead.

Note also that no domain class references Awaiten. `PriceList` takes an `IConfiguration`, `OrderService`
takes an `ILoggerFactory`; both come from the host, but external-ness is declared once on the container, so
the classes stay plain.

## Which bridge path this uses

This sample uses the **projection** path, where MS.DI stays the provider and the Awaiten container is
projected into it. That is the right default for a web app, because the host owns a great deal that Awaiten
never sees: the request pipeline, the options and logging infrastructure, everything `AddControllers` and its
kind register. Projection lets those and the Awaiten services resolve side by side.

The alternative is `AwaitenServiceProvider` as a full replacement, with Awaiten owning all construction and
disposal. It suits a process whose whole graph is yours, such as a console host, a worker or a desktop app. A
web app is not one: being the only provider means declaring every framework service the pipeline resolves.
Handler-parameter binding works on either path, because the provider answers `IServiceProviderIsService` from
the container's dispatch tables, which is what the framework consults to tell a dependency-injection parameter
from a request one.

This sample is also not AOT-published; `Awaiten.AotSample` covers that claim.
