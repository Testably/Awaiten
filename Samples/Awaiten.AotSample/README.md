# Awaiten.AotSample

A minimal console app that proves Awaiten's `Microsoft.Extensions.DependencyInjection` bridge is
reflection-free and native-AOT compatible. It registers a generated `[Container]` into an
`IServiceCollection` with `AddGeneratedContainer`, verifies its external dependencies with
`VerifyAwaitenContainers`, and resolves a service (whose graph mixes an Awaiten-owned `IClock` with a
host-owned `Banner` injected across the seam via `[FromServices]`) from an MS.DI scope.

The project is part of `Awaiten.slnx`, so it is compiled cross-platform alongside the unit tests. Because
publishing it natively needs a platform linker, the native-AOT publish (and the run that follows) happens in
a dedicated `AotSample` CI job on Windows rather than on every runner. `TreatWarningsAsErrors` is enabled, so
any trim or AOT warning fails that job.

## Publish with native AOT

Requires the native AOT prerequisites (on Windows, the "Desktop development with C++" workload):
<https://aka.ms/nativeaot-prerequisites>.

```sh
dotnet publish Samples/Awaiten.AotSample -c Release -r win-x64 -p:GeneratePackageOnBuild=false
./Samples/Awaiten.AotSample/bin/Release/net10.0/win-x64/publish/Awaiten.AotSample.exe
# prints: Awaiten on AOT @ 2026-06-24   (exit code 0)
```

Use the runtime identifier for your platform (`linux-x64`, `osx-arm64`, …). The whole graph projects with
explicit factory/instance delegates, so no reflection-based activation is involved. The **async** projection
(a service bridged as `Task<TService>`, exercised here by `Warmup`) is reflection-free too: the generator
emits the closed `typeof(Task<TService>)` and the `Task<object>`→`Task<T>` converter into the container's
registration metadata, so the bridge never calls `MakeGenericType` / `MakeGenericMethod` at runtime and needs
no trim/AOT suppressions.

## Run without the AOT toolchain

To verify behavior where the native linker is unavailable, run it on the JIT or publish it self-contained:

```sh
dotnet run --project Samples/Awaiten.AotSample -c Release -p:GeneratePackageOnBuild=false
# or
dotnet publish Samples/Awaiten.AotSample -c Release -r win-x64 --self-contained true -p:PublishAot=false -p:GeneratePackageOnBuild=false
```
