# Awaiten

[![Nuget](https://img.shields.io/nuget/v/Awaiten)](https://www.nuget.org/packages/Awaiten)
[![Build](https://github.com/Testably/Awaiten/actions/workflows/build.yml/badge.svg)](https://github.com/Testably/Awaiten/actions/workflows/build.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=Testably_Awaiten&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Testably_Awaiten)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=Testably_Awaiten&metric=coverage)](https://sonarcloud.io/summary/overall?id=Testably_Awaiten)
[![Mutation testing badge](https://img.shields.io/endpoint?style=flat&url=https%3A%2F%2Fbadge-api.stryker-mutator.io%2Fgithub.com%2FTestably%2FAwaiten%2Fmain)](https://dashboard.stryker-mutator.io/reports/github.com/Testably/Awaiten/main)

**The async-first dependency injection container for .NET.**

Awaiten is a Roslyn source generator that wires your object graph at build time. There is no runtime reflection, the configuration is verified by the compiler (missing, cyclic, ambiguous and lifetime-mismatched registrations are build errors), and the generated code is plain, readable C#.

Its headline differentiator is **async initialization**: services that need asynchronous setup after construction (opening a connection, handshaking with hardware) are tracked through the graph, and touching an uninitialized instance is a compile error.

```csharp
[Container]
[Singleton<RealTimeSystem, ITimeSystem>]
[Scoped<StorageService, IStorageService>]
[Transient<OrderService, IOrderService>]
public partial class AppContainer { }

await using var app = new AppContainer();
await app.InitializeAsync();          // async-initialized services are warmed up
var service = app.Get<IOrderService>();
```

## Packages

| Package | Description |
|---|---|
| `Awaiten` | Pure core: attributes, the source generator, and the runtime seams. No third-party dependencies. |
| `Awaiten.Extensions.DependencyInjection` | Microsoft.Extensions.DependencyInjection interop (ASP.NET Core, generic host, and other containers). |

## Status

Early scaffolding. See the design documents for the full specification, the diagnostic catalogue, and integration recipes (ASP.NET Core, WPF, MS.DI, Autofac).

## Building

```sh
./build.sh          # or build.cmd / build.ps1 on Windows
```

The build uses [NUKE](https://nuke.build/). Targets include compile, unit tests, API checks, code analysis and packaging.

## License

MIT © Valentin Breuß
