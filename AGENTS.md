# Awaiten agent guide

Awaiten is a compile-time dependency injection container for .NET: a Roslyn incremental source generator that wires the object graph at build time and reports wiring mistakes as `AWT###` diagnostics.

## Design goals

Correctness is a must. The other goals are strong defaults and win in this order when they conflict: stable and compatible, AOT and trimming friendly, a consistent and pleasant API, a fast happy path, extensible, errors at compile time, documented. [Docs/agents/design-goals.md](Docs/agents/design-goals.md) explains each goal, the code rules, the checks before a commit and the goals of this repository.

## Project constraints

- Use the SDK pinned in `global.json` (10.0.300, `latestPatch`). Package versions live only in `Directory.Packages.props` (central package management); never put a version on a `PackageReference`.
- `Source/Directory.Build.props` sets `TargetFrameworks` to `netstandard2.0;net8.0;net10.0`, `LangVersion` preview, nullable (annotations only on netstandard2.0) and `TreatWarningsAsErrors`. Test projects target `net10.0;net8.0;net48` unless they override it.
- `Awaiten.SourceGenerators` targets only `netstandard2.0`, is not a package of its own and ships inside the `Awaiten` package under `analyzers/dotnet/cs`. Keep its models equatable (`EquatableArray<T>`) so incremental caching works.
- No runtime reflection. The generated code and both libraries must stay trim-safe and native-AOT clean; `Samples/Awaiten.AotSample` publishes with `PublishAot` and warnings as errors to prove it. Do not add `MakeGenericType`, `Activator` or similar to any runtime path.
- `Awaiten` has no third-party runtime dependencies (only private build-time packages). Microsoft.Extensions.DependencyInjection interop belongs in `Awaiten.Extensions.DependencyInjection`.
- Generated code must compile for every consumer framework, including net48 through the netstandard2.0 asset (`Awaiten.Tests` runs on net48).
- Every new diagnostic needs a descriptor in `Diagnostics.cs`, a row in `AnalyzerReleases.Unshipped.md`, a `DiagnosticTests.AwtNNN*.cs` test and an entry in `Docs/pages/09-diagnostics.md`. Ids are stable; never reuse one.
- Public API changes are deliberate: the API snapshots in `Tests/Awaiten.Api.Tests` must be updated in the same PR. Public members carry XML docs.
- Resolution is a hot path. Avoid allocations in generated resolve code and measure changes with the benchmarks.

## Validation

- The tests run on TUnit (Microsoft.Testing.Platform, selected in `global.json`): `dotnet test --project Tests/Awaiten.Tests`, filter with `--treenode-filter "/*/*/<class>/<test>"`. Tests run in parallel, also within a class; mark a class whose tests share static state with `[NotInParallel(nameof(...))]`.
- Run the tests relevant to your change before pushing. Generator and diagnostic changes: `Tests/Awaiten.SourceGenerators.Tests`. Runtime behaviour: `Tests/Awaiten.Tests`. MS.DI bridge: `Tests/Awaiten.Extensions.DependencyInjection.Tests`.
- `Tests/Awaiten.ExampleTests` holds the documented examples and the architecture test; run it when you touch examples or docs.
- Public API change: run `Tests/Awaiten.Api.Tests`, accept with `AWEXPECT_ACCEPT_SNAPSHOTS=all` (or the explicit `AcceptApiChanges` test) and commit the changed `*.awexpect-snapshot.txt` files. Never commit a pending snapshot.
- Diagnostic tests assert by wildcard on the formatted output, for example `await That(result.Diagnostics).Contains("*AWT116*").AsWildcard();`, so a failure shows the actual diagnostics.
- Code that targets net48 can use records and `init` (polyfilled through `IsExternalInit`), but there is no `System.Index`/`System.Range` polyfill, so avoid `^1`, ranges and list patterns there. Run the net48 leg (Windows only) when using a newer language feature.
- AOT or MS.DI bridge changes: also run `./build.ps1 AotSample` (needs a platform linker) and `./build.ps1 WebSample`.

## Conventions

- Files are stored with LF in git and checked out per platform (`* text=auto`); `.editorconfig` asks for CRLF and tabs. Raw string literals therefore contain the checkout's line endings, so do not assert on exact newlines in generated text.
- Most files have no UTF-8 BOM, a few do. Keep a file's existing BOM state: PowerShell rewrites strip or add BOMs, so prefer careful editors and check `git diff` for a stray U+FEFF.
- No em-dashes in docs, XML docs or new comments. `Docs/pages/` is published to docs.testably.org; keep it user-facing.

## Git workflow

- Work on a feature branch named after the change type (for example `chore/...`, `ci/...`) and open a PR against `main`; PRs are squash-merged.
- Conventional commits: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`, `ci:`, and `feat!:` for a breaking change. The subject becomes the PR title, the body explains why (the diff shows what), one line per paragraph, no co-author or attribution trailers.
- Never commit `.claude/` or other local agent notes.
- Releases are pushed to nuget.org from a `v*` tag by the `Build` workflow; do not tag unless asked.

## References

- [Docs/agents/design-goals.md](Docs/agents/design-goals.md): the goals and code rules for every change, the checks before a commit and the goals of this repository.
- [Docs/agents/workflows.md](Docs/agents/workflows.md): build, test, benchmark and docs commands, the Fallout pipeline targets and the CI workflows.
- [Docs/agents/project-structure.md](Docs/agents/project-structure.md): architecture, source locations and where to change what.
