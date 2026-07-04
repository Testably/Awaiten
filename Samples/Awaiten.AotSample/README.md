# Awaiten.AotSample

A minimal console app that proves Awaiten's `Microsoft.Extensions.DependencyInjection` bridge is
reflection-free and native-AOT compatible. It registers a generated `[Container]` into an
`IServiceCollection` with `AddGeneratedContainer`, verifies its external dependencies with
`VerifyAwaitenContainers`, and resolves a service (whose graph mixes an Awaiten-owned `IClock` with a
host-owned `Banner` injected across the seam via `[FromServices]`) from an MS.DI scope.

The project is intentionally **not** part of `Awaiten.slnx` / the test run: it targets `net10.0` only and
publishing it natively needs a platform linker, so it is built and published on demand rather than in CI.

## Publish with native AOT

Requires the native AOT prerequisites (on Windows, the "Desktop development with C++" workload):
<https://aka.ms/nativeaot-prerequisites>.

```sh
dotnet publish Samples/Awaiten.AotSample -c Release -r win-x64 -p:GeneratePackageOnBuild=false
./Samples/Awaiten.AotSample/bin/Release/net10.0/win-x64/publish/Awaiten.AotSample.exe
# prints: Awaiten on AOT @ 2026-06-24   (exit code 0)
```

Use the runtime identifier for your platform (`linux-x64`, `osx-arm64`, …). The AOT IL compiler emits no
trim or AOT warnings for the generated container or the bridge — all MS.DI registrations use explicit
factory/instance delegates, so no reflection-based activation is involved.

## Run without the AOT toolchain

To verify behavior where the native linker is unavailable, run it on the JIT or publish it self-contained:

```sh
dotnet run --project Samples/Awaiten.AotSample -c Release -p:GeneratePackageOnBuild=false
# or
dotnet publish Samples/Awaiten.AotSample -c Release -r win-x64 --self-contained true -p:PublishAot=false -p:GeneratePackageOnBuild=false
```
