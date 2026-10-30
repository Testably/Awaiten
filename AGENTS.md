# Awaiten agent guide

Awaiten is a compile-time dependency injection container for .NET: a Roslyn incremental source generator that wires the object graph at build time, emits plain C# without reflection, tracks async initialization through the graph and reports wiring mistakes as `AWT###` diagnostics.

## Design goals

Correctness is a must. The other goals are strong defaults and win in this order when they conflict: compile time over run time, plain generated code that is AOT and trimming clean, explicit async, stable and compatible, one consistent declaration language, domain code free of Awaiten, fast resolution kept simple, documented. [Docs/agents/design-goals.md](Docs/agents/design-goals.md) explains each goal, the code and test rules, and the checks before a commit.

## Hard rules

- Use the SDK pinned in `global.json` (10.0.300, `latestPatch`). Package versions live only in `Directory.Packages.props` (central package management); never put a version on a `PackageReference`.
- `Source/Directory.Build.props` sets `TargetFrameworks` to `netstandard2.0;net8.0;net10.0`, `LangVersion` latest, nullable (annotations only on netstandard2.0) and `TreatWarningsAsErrors`. Test projects target `net10.0;net8.0;net48` unless they override it.
- `Awaiten.SourceGenerators` targets only `netstandard2.0`, is not a package of its own and ships inside the `Awaiten` package under `analyzers/dotnet/cs`. Its models stay equatable (`EquatableArray<T>`, no Roslyn symbols or locations) so incremental caching works. Do not raise its `Microsoft.CodeAnalysis` versions: they set the oldest compiler that can load the generator.
- No reflection on any runtime path. The generated code and both libraries stay trim-safe and native-AOT clean; do not add `MakeGenericType`, `Activator` or similar.
- `Awaiten` has no runtime dependencies (only private build-time packages), except `Microsoft.Bcl.AsyncInterfaces` on netstandard2.0, which brings `IAsyncDisposable` to net48 and other older frameworks. Do not add others. Microsoft.Extensions.DependencyInjection interop belongs in `Awaiten.Extensions.DependencyInjection`.
- Generated code compiles for every consumer framework, including net48 through the netstandard2.0 asset.
- Every new diagnostic needs a descriptor in `Diagnostics.cs`, a row in `AnalyzerReleases.Unshipped.md`, a `DiagnosticTests.AwtNNN*.cs` test and a section in `Docs/pages/09-diagnostics.md`. Ids are stable; never reuse one.
- Public API changes are deliberate: update the API snapshots in `Tests/Awaiten.Api.Tests` in the same PR. Name public types by role as `CONTRIBUTING.md` describes. Public members carry XML docs.

## Validation

- The tests run on TUnit (Microsoft.Testing.Platform, selected in `global.json`): `dotnet test --project Tests/Awaiten.Tests -f net10.0`, filter with `--treenode-filter "/*/*/<class>/<test>"`. Tests run in parallel, also within a class; mark a class whose tests share static state with `[NotInParallel(nameof(...))]`.
- Run the tests relevant to your change before pushing. Generator and diagnostic changes: `Tests/Awaiten.SourceGenerators.Tests`. Runtime behaviour: `Tests/Awaiten.Tests`. MS.DI bridge: `Tests/Awaiten.Extensions.DependencyInjection.Tests`. Examples and docs: `Tests/Awaiten.ExampleTests`, and the slow `Tests/Awaiten.Docs.Tests` for any change to a code block in `Docs/pages`.
- Public API change: run `Tests/Awaiten.Api.Tests`, accept with `AWEXPECT_ACCEPT_SNAPSHOTS=all` (or the explicit `AcceptApiChanges` test) and commit the changed `*.awexpect-snapshot.txt` files. Never commit a pending snapshot.
- Code that compiles for net48 or netstandard2.0 has no `System.Index`/`System.Range`, so avoid `^1`, ranges and list patterns there, and run the net48 leg (Windows only) when using a newer language feature.
- Reflection, trimming, AOT or MS.DI bridge changes: also run `./build.ps1 AotSample` (needs a platform linker) and `./build.ps1 WebSample`.

## Conventions

- Files are stored with LF in git and checked out per platform (`* text=auto`); `.editorconfig` asks for CRLF and tabs. Raw string literals therefore contain the checkout's line endings, so do not assert on exact newlines in generated text.
- Most files have no UTF-8 BOM, a few do. Keep a file's existing BOM state: PowerShell rewrites strip or add BOMs, so prefer careful editors and check `git diff` for a stray U+FEFF.
- Comments are rare and explain why; prefer XML docs. In tests, put the reason into `.Because(...)` instead of a comment.
- No em-dashes in docs, XML docs or comments. `Docs/pages/` is published to docs.testably.org; keep it user-facing and describe the current behaviour, not its history.

## Git workflow

- Work on a feature branch named after the change type (for example `feat/...`, `fix/...`, `docs/...`) and open a PR against `main`; PRs are squash-merged.
- Conventional commits: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`, `ci:`, `build:`, and `!` after the type for a breaking change. The subject becomes the PR title, the body explains why (the diff shows what), one line per paragraph, no co-author or attribution trailers.
- Never commit `.claude/` or other local agent notes.
- Releases are pushed to nuget.org from a `v*` tag by the `Build` workflow; do not tag unless asked.

## References

- [Docs/agents/design-goals.md](Docs/agents/design-goals.md): the goals, code and test rules for every change, the checks before a commit and how to handle reviews.
- [Docs/agents/workflows.md](Docs/agents/workflows.md): build, test, benchmark and docs commands, the Fallout pipeline targets and the CI workflows.
- [Docs/agents/project-structure.md](Docs/agents/project-structure.md): architecture, source locations and where to change what.
- [Docs/pages/02-design-principles.md](Docs/pages/02-design-principles.md): the user-facing design Awaiten serves (composition root, the four anti-patterns); features must not push users towards them.
