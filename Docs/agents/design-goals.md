# Design goals

These goals decide between changes that are all reasonable. They come from what Awaiten promises its users: the configuration is verified by the compiler, the container is plain generated C# without reflection, and async initialization is tracked through the graph.

Correctness is a hard requirement. Every other goal is a strong default: follow it, and when a change has to break one, say so in the pull request and explain why.

## Decision test

Before proposing a change, ask: does the generated container still do exactly what the graph declares, and does the change catch a mistake earlier, keep the generated code plain, or make a graph simpler to declare? If it does none of these, reconsider it.

## Goals

When goals conflict, the earlier one wins.

1. **Correct (must).** A generated container constructs, shares, initializes and disposes exactly what the graph declares: lifetimes, scopes, decorator order, collection order, keyed lookups, async initialization once per instance and disposal in reverse order of creation. Runtime tests in `Tests/Awaiten.Tests` pin the behaviour, generator tests pin the diagnostics.
2. **Compile time over run time.** No graph Awaiten accepts may fail at run time for a reason visible at compile time. Every misuse the generator can see is a diagnostic with a clear cause and remedy. Run-time errors remain only for what the compiler cannot see (an imported service missing from the host, a key only known at run time, a synchronous call into an async-only service on an untyped path), and they name the subject, the cause and the remedy like the messages in `Sources/Sources.Guidance.cs`.
3. **Plain generated code, AOT and trimming clean.** The container is ordinary C# that a user can read and step through. No reflection on any runtime path (`MakeGenericType`, `Activator`, `GetMethod` and the like): what would need it is decided in the generator and emitted as code or as a closed delegate (see `AwaitenTaskProjection`). `Samples/Awaiten.AotSample` proves it with native AOT and warnings as errors.
4. **Explicit async.** Asynchronous work stays visible: `Resolve`/`ResolveAsync`, `CreateScope`/`CreateScopeAsync` and `Dispose`/`DisposeAsync` come in pairs, an async-initialized (async-tainted) service cannot be obtained synchronously, and an async-only disposable cannot be disposed synchronously. A relaxation such as `SyncResolveAfterInit` or `LifetimeSafety.Loose` is an explicit opt-in on the `[Container]`, never a default.
5. **Stable and compatible.** Breaking changes are deliberate, marked with `!` in the commit and listed in the PR. Until 1.0 they are still allowed when they make the API clearer; afterwards only in a major version. Public API changes show up in the API snapshots. Diagnostic ids never change meaning and are never reused. `Awaiten` has no runtime dependencies, the generator keeps its Roslyn floor (Dependabot ignores `Microsoft.CodeAnalysis*` on purpose), and generated code compiles for every consumer framework down to net48 through the netstandard2.0 asset.
6. **One consistent declaration language.** Registration reads the same for every lifetime: one attribute per lifetime (`[Singleton]`, `[Scoped]`, `[Transient]`) with the same shape and options, generic and `typeof` forms side by side. Public types are named by role (see `CONTRIBUTING.md`): the container and host-facing types carry the `Awaiten` prefix, the declaration attributes, their enums and the types users implement or name do not. Every public member has XML documentation.
7. **Domain code stays free of Awaiten.** Configuration belongs on the composition root. Prefer a container-side form (`[ImportService<T>]`, `WhenInjectedInto`, a factory `[Arg]`, `[InjectProperty<T>]`) over an attribute a user would have to put on their own class; the consumer-side attributes (`[Arg]`, `[FromKey]`, `[Inject]`) stay escape hatches. Extension points are narrow and dependency-free seams such as `IExternalResolver`, not hooks into the generator.
8. **Fast resolution, simply.** Resolution is the hot path: generated resolve code avoids allocations, LINQ, closures and boxing. Simplicity still wins over a micro-optimization: take a performance change only when a benchmark before and after shows a measurable gain, and prefer the simpler generated code when the gain is marginal.
9. **Documented.** A public feature or a new diagnostic is not done until it is on the docs site (`Docs/pages/`), with examples that `Tests/Awaiten.ExampleTests` covers where they matter. Feature pages that let users paper over a design problem (keyed services, scanning, context-aware factories, property injection) keep their "When not to reach for this" note. Use the Docusaurus features the existing pages use: front matter, admonitions (`:::tip`, `:::warning`, `:::danger` for the error diagnostics) and tabs where alternatives sit side by side.

## Code

- **Generator.** Keep the pipeline incremental: models are records with `EquatableArray<T>` instead of arrays, and Roslyn symbols, `Location`s and `Compilation`s never reach a model (diagnostics travel as `DiagnosticInfo` with a `LocationInfo`). A container with errors still emits its shell (`model.HasErrors`) so the user's code keeps compiling around the diagnostic.
- **Generated code.** Starts with `// <auto-generated/>` and `#nullable enable`, references every type `global::`-qualified, uses `ConfigureAwait(false)` on every `await` and only uses APIs that the consumer's framework has: probe the compilation for a newer type (as `AwaitenGenerator.Symbols.cs` does for `IAsyncDisposable`) instead of assuming it.
- **Async in the libraries.** The public async surface uses `Task` (netstandard2.0 without extra dependencies), takes a `CancellationToken` and passes it on, and awaits with `ConfigureAwait(false)`. Do not block on async code except at a deliberate synchronous boundary.
- **Language level.** `LangVersion` is latest, so no preview language features, and nullable is on. net48 and netstandard2.0 have `init` and records (through `IsExternalInit`) but no `System.Index`/`System.Range`, so avoid `^1`, ranges and list patterns in code that compiles for them.
- **Style.** Follow `.editorconfig`: tabs, file-scoped namespaces, explicit types instead of `var`, expression bodies for short members. Prefer XML documentation over comments, and keep comments to the reason behind code that is not obvious. No em-dashes in comments, XML docs or documentation.
- **Avoid.** Catching exceptions without handling or rethrowing them, string concatenation in loops (the generator emits through a `StringBuilder`), and suppressing an analyzer or Sonar finding without asking the maintainer first.

## Tests

- Tests use TUnit with aweXpect: `await That(...)`, with the reason in `.Because(...)` instead of a comment, so each test explains itself and the tests stay reorderable. Group related cases in nested classes rather than separating them with comments.
- A diagnostic test runs the generator over an in-memory source and asserts the id by wildcard (`Contains("*AWT130*").AsWildcard()`), with cases that report and cases that must not. A runtime exception is asserted by its characteristic fragments (`WithMessage("*ResolveAsync*").AsWildcard()`), not by the whole text.
- A behaviour change has a runtime test that declares a real `[Container]` in the test file and resolves from it; a generator-only test is not enough.
- A fix starts with a test that fails without it.

## Before you commit

Run the checks that match the change:

- **Any code change:** the tests of the affected projects (see [workflows.md](workflows.md)), on net10.0 and, when the change touches code that compiles for net48, on net48 too.
- **New or changed diagnostic:** the descriptor in `Diagnostics.cs`, a row in `AnalyzerReleases.Unshipped.md`, a `DiagnosticTests.AwtNNN*.cs` test and a `### AWTnnn` section in `Docs/pages/09-diagnostics.md`.
- **Public API:** the API snapshot tests; review and accept intentional changes, never commit pending snapshots.
- **Reflection, trimming, AOT or the MS.DI bridge:** `./build.ps1 AotSample` and `./build.ps1 WebSample`.
- **Resolve hot path or a performance claim:** a benchmark before and after, in Release on the same machine.
- **New public feature:** documentation and its examples.

## Reviews

Verify every review finding against the code before changing anything. If you disagree, push back once with concrete reasoning; if the reviewer or the maintainer confirms the finding, implement it. Some tradeoffs are accepted and need no finding: `[Scan]` enumerates the whole compilation inside the generator transform, which costs IDE incrementality on large solutions but keeps scanning compile-time.
