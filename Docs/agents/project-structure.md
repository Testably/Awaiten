# Architecture and source locations

Product code is in `Source/`, tests in `Tests/`, benchmarks in `Benchmarks/`, runnable samples in `Samples/`, the Fallout build in `Pipeline/` and the user documentation in `Docs/pages/`. `Awaiten.slnx` contains all of them.

## Projects (`Source/`)

| Project | Responsibility |
|---|---|
| `Awaiten` | The public surface users reference: registration attributes (`[Container]`, `[Singleton<T>]`, `[Scoped<T>]`, `[Transient<T>]`, `[Decorate]`, `[Composite]`, `[Scan]`, `[Module]`, `[Import]`, `[ImportService<T>]`, `[Inject]`, `[Arg]`, `[FromKey]` and more), the resolver interfaces (`IAwaitenResolver`, `IAwaitenScope`, `IAwaitenRoot`, `IAwaitenAsyncResolver`), `IAsyncInitializable`, `Owned<T>` and the small runtime seams the generated code calls. Ships the generator as an analyzer. |
| `Awaiten.SourceGenerators` | The `AwaitenGenerator` incremental generator plus the `AwaitenAnalyzer` (AWT118, AWT156) and `AwaitenBoundaryAnalyzer` (AWT134, AWT135, AWT205). netstandard2.0, packed into `Awaiten`. |
| `Awaiten.Extensions.DependencyInjection` | The MS.DI bridge: `AddAwaitenContainer` projects a generated container into an `IServiceCollection`, `AwaitenServiceProvider`/`AwaitenServiceProviderFactory` make the container the provider, plus host initialization (`AwaitenContainerBuilder.WithInitialization`) and `VerifyAwaitenContainers`. |

## How the generator works

1. `AwaitenGenerator.Initialize` has two roots, both via `ForAttributeWithMetadataName`: `[Container]` classes and `[Module]` classes that declare a `[Scan]`.
2. For a container, the registration attributes of the container and its imported modules are read (`AwaitenGenerator.Registrations.cs`, `.Scan.cs`, `.ModuleScan.cs`, `.OpenGenerics.cs`, `.Decorators.cs`, `.OpenDecorators.cs`, `.Keys.cs`), coalesced (`.Coalescing.cs`), and the graph is resolved and checked (`.Analysis.cs`, `.Classification.cs`, `.Collection.cs`, `.Production.cs`, `.Variance.cs`, `.Symbols.cs`).
3. The result is an equatable `ContainerModel` (`Entities/`, using `Internals/EquatableArray.cs`) that carries the diagnostics as `DiagnosticInfo`. Roslyn symbols and live locations must not reach this model, or incremental caching breaks.
4. `Sources/` emits the code: a `Scope` with the resolution logic and a sealed `Root` that owns the singletons (`Sources.cs`, `.Construction.cs`, `.Dispatch.cs`, `.Disposal.cs`, `.Keyed.cs`, `.Resolvers.cs`, `.Guidance.cs`, `.Names.cs`). `Sources.Module.cs` emits the self-compiled factories and `[GeneratedScanRegistration]` attributes of a module scan.

Containers and modules must be `static partial` classes; a non-static one is a diagnostic (AWT116, AWT152). Errors still emit a container shell (`model.HasErrors`) so the user's code keeps compiling around it. Runtime error texts the generated code throws are built in `Sources.Guidance.cs`; reuse them so `Resolve` and `GetResolvability` give the same answer.

## Diagnostics

- Descriptors: `Source/Awaiten.SourceGenerators/Diagnostics.cs`, category `Awaiten`, ids `AWT101` upward. A message states the cause and the remedy. One id may have a descriptor per severity when a container option decides it (`AWT118` under strict and loose lifetime safety).
- Release tracking: `AnalyzerReleases.Unshipped.md` lists every rule so far (`AnalyzerReleases.Shipped.md` is still empty). Add a row with id, severity and a one-line note for each new descriptor.
- Tests: `Tests/Awaiten.SourceGenerators.Tests/DiagnosticTests.AwtNNN<Name>.cs`, typically a nested class per id inside the partial `DiagnosticTests`, with cases that report and cases that must not.
- Docs: a `### AWTnnn` section with a triggering example in `Docs/pages/09-diagnostics.md`.

## Tests and samples

- `Tests/Awaiten.Tests`: compiles real containers with the generator and exercises them at runtime on net10.0, net8.0 and net48. Uses `Awaiten.Tests.Support` as a referenced assembly for cross-assembly scans and modules.
- `Tests/Awaiten.SourceGenerators.Tests`: generator output and diagnostics over in-memory compilations (`TestHelpers/`), including cross-assembly cases via `RunWithReferencedAssembly` and `RunWithGeneratedReferencedAssembly`.
- `Tests/Awaiten.Extensions.DependencyInjection.Tests`: bridge behaviour, disposal, keyed services and MS.DI conformance.
- `Tests/Awaiten.ExampleTests`: the coffee-shop examples from the docs. The generator's `launchSettings.json` debugs against this project.
- `Tests/Awaiten.Docs.Tests` (net10.0): compiles every `csharp` code block of `Docs/pages` with the generator and analyzers, against the placeholder types in `Scaffold/`.
- `Tests/Awaiten.Api.Tests`: public API snapshots of both packages for each target framework.
- `Samples/Awaiten.AotSample` and `Samples/Awaiten.WebSample`: native-AOT and ASP.NET Core proofs of the bridge.

## Build settings

- `Directory.Build.props` (root): strong naming with `strongname.snk` for every project; test, sample and benchmark projects turn signing off.
- `Source/Directory.Build.props`: package metadata, target frameworks, warnings as errors (CS1591 and NU5104 stay warnings), packing with symbols. The generator project overrides the target framework and does not pack itself.
- `Tests/Directory.Build.props`: TUnit (`TUnit.Engine`) as executables of the Microsoft.Testing.Platform, its TRX and code coverage extensions, and aweXpect for every test project; implicit usings off (each project has a `Usings.cs`).
- `Pipeline/` has its own `Directory.Build.props` so the root settings do not leak into the build project.
