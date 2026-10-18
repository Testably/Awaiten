# Contributor guide

## Pull requests

**Pull requests are welcome!**  
Please include a clear description of the changes you have made with your request; the title should follow
the [conventional commits](https://www.conventionalcommits.org/en/v1.0.0/) guideline.
All code should be covered by unit tests and comply with the coding guideline in this project.

## Building and testing

`dotnet build Awaiten.slnx` builds everything; `./build.ps1 UnitTests` (or `./build.sh UnitTests`) runs the tests
like the CI.

The tests run on [TUnit](https://tunit.dev) as Microsoft.Testing.Platform executables, which `global.json` selects for
`dotnet test`:

```shell
dotnet test --project Tests/Awaiten.Tests -f net10.0
dotnet test --project Tests/Awaiten.Tests -f net10.0 --treenode-filter "/*/*/ScanTests/*"
```

## Naming public types

A public type carries the `Awaiten` prefix by its role:

- **Prefixed:** the container and what it exposes to a host, which sits next to `IServiceProvider` or
  `ServiceDescriptor` there: resolvers, scopes, the root, container and registration metadata
  (`IAwaitenContainerMetadata`, `AwaitenRegistration`, `AwaitenExternalDependency`), host-side helpers and the types
  of `Awaiten.Extensions.DependencyInjection`.
- **Unprefixed:** the declaration DSL, that is the attributes and the enums used as their arguments (`Lifetime`,
  `ScanAs`, `Fallback`, `LifetimeSafety`), and the types users implement or name in their own classes (`Owned<T>`,
  `IAsyncInitializable`, `IExternalResolver`).
