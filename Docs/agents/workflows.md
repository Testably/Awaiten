# Development workflows

Commands run from the repository root. On Linux and macOS use `./build.sh` instead of `./build.ps1`.

## Build and test

- Build everything: `dotnet build Awaiten.slnx`. The solution excludes `Pipeline/Build.csproj` from the build. Library builds also produce packages (`GeneratePackageOnBuild`).
- Run one test project: `dotnet test --project Tests/Awaiten.Tests`. The tests are TUnit executables of the Microsoft.Testing.Platform, which `global.json` selects as the runner of `dotnet test`. Without `-f`, every target framework runs (`net10.0`, `net8.0` and, on Windows, `net48`); pass `-f net10.0` for a quick loop. `dotnet run --project Tests/Awaiten.Tests -f net10.0` runs the same tests.
- Filter: TUnit selects tests with a tree node filter (`/<assembly>/<namespace>/<class>/<test>`, a nested class is `Outer+Inner`), for example `dotnet test --project Tests/Awaiten.SourceGenerators.Tests --treenode-filter "/*/*/*Awt116*/*"`.
- TUnit runs all tests in parallel, also those of one class. A class whose tests share static state (a static probe or counter) is marked `[NotInParallel(nameof(...))]`.
- Test projects:
  - `Tests/Awaiten.Tests`: runtime behaviour of generated containers, declared as `[Container]` classes in the test files.
  - `Tests/Awaiten.SourceGenerators.Tests` (net10.0 only): runs the generator over in-memory sources with `TestHelpers/Generator.cs` and analyzers with `TestHelpers/Analyzer.cs`; one `DiagnosticTests.AwtNNN*.cs` file per diagnostic.
  - `Tests/Awaiten.Extensions.DependencyInjection.Tests`: the MS.DI bridge, including MS.DI conformance and host initialization.
  - `Tests/Awaiten.ExampleTests`: the documented examples, an ASP.NET Core example (not on net48) and the architecture test from the design principles page.
  - `Tests/Awaiten.Api.Tests` (net10.0 only): public API snapshots.
  - `Tests/Awaiten.Tests.Support`: a plain library, not a test project, that gives `Awaiten.Tests` a referenced assembly for cross-assembly `[Scan]` and `[Module]` scenarios.
- Tests assert with aweXpect (`await That(...)`), put the reason in `.Because(...)` instead of a comment and group related cases in a (often nested or partial) class per feature. The test rules are in [design-goals.md](design-goals.md#tests).

## Public API snapshots

`Tests/Awaiten.Api.Tests/ApiApprovalTests.cs` reads the public API of `Awaiten` and `Awaiten.Extensions.DependencyInjection` for every framework in `Source/Directory.Build.props` with `aweXpect.Snapshots.PublicApi` and compares it to `ApiApprovalTests.<assembly>.<framework>.awexpect-snapshot.txt`. The test project builds all target frameworks of the referenced projects, so a plain `dotnet test --project Tests/Awaiten.Api.Tests` is enough.

To accept an intentional change, run the tests with the environment variable `AWEXPECT_ACCEPT_SNAPSHOTS` set to `all` (PowerShell: `$env:AWEXPECT_ACCEPT_SNAPSHOTS = 'all'; dotnet test --project Tests/Awaiten.Api.Tests`), or run the explicit `AcceptApiChanges` test (`--treenode-filter "/*/*/*/AcceptApiChanges"` or from the IDE), then review and commit the snapshot diff.

## Samples

- `Samples/Awaiten.AotSample`: published with native AOT and run. `./build.ps1 AotSample` (needs a platform linker; on Windows the "Desktop development with C++" workload). Manual steps are in its `README.md`.
- `Samples/Awaiten.WebSample`: an ASP.NET Core app using the MS.DI bridge. `./build.ps1 WebSample` builds it and runs `--smoke`, which drives every endpoint over HTTP.

## Benchmarks

`Benchmarks/Awaiten.Benchmarks` (BenchmarkDotNet, net10.0) compares Awaiten with Microsoft.Extensions.DependencyInjection, Autofac, DryIoc, SimpleInjector, Jab and Pure.DI. The classes are `ResolveBenchmarks`, `BuildBenchmarks` and `RealisticResolveBenchmarks`. `ResolveAsyncBenchmarks` measures Awaiten's `ResolveAsync<T>` per lifetime on its own and is not part of the CI benchmark matrix.

- Through the pipeline: `./build.ps1 Benchmarks --benchmark-filter "*BuildBenchmarks*"` (results in `Artifacts/Benchmarks`).
- Directly: `dotnet run -c Release --project Benchmarks/Awaiten.Benchmarks -- --filter "*RealisticResolveBenchmarks*"`.

Always run in Release and compare before and after on the same machine.

## Documentation

- `Docs/pages/` is the user documentation (Docusaurus front matter and admonitions such as `:::tip`). It is not built here: after a successful `Build` on `main`, `notify-docs-site.yml` asks `Testably/Testably.Site` to rebuild docs.testably.org/Awaiten.
- `Docs/pages/11-benchmarks.mdx` renders `data.js`/`limited-data.js` that `PublishBenchmarkReport` commits to the `benchmarks` branch.
- `Docs/pages/09-diagnostics.md` has one `### AWTnnn` section per diagnostic; other pages link to it as `./diagnostics#awtnnn`.
- The `README.md` is packed into the NuGet packages; `UpdateReadme` rewrites badges for a release.
- Code fences in the docs are not compiled. Keep examples that matter covered by `Tests/Awaiten.ExampleTests`.

## Build pipeline (Fallout)

`build.ps1`, `build.sh` and `build.cmd` build and run `Pipeline/Build.csproj` (Fallout 10). `./build.ps1 --help` lists the targets. Running without a target executes the defaults `ApiChecks`, `Benchmarks` and `CodeAnalysis`, so always name a target. `--configuration` defaults to Debug locally and Release on CI.

| Target | What it does |
|---|---|
| `Compile` | Clean `Artifacts/` and `TestResults/`, restore, compute the version with GitVersion and build the solution. |
| `UnitTests` | `dotnet test` for `Awaiten.Tests`, `Awaiten.Extensions.DependencyInjection.Tests`, `Awaiten.SourceGenerators.Tests` and `Awaiten.ExampleTests` per framework (net48 only on Windows), with `<project>_<framework>.trx` and `.cobertura.xml` in `TestResults/`. |
| `CodeCoverage` | `UnitTests`, then ReportGenerator to the generic coverage report of SonarQube. |
| `CodeAnalysis` | SonarCloud scan around `Compile` and `CodeCoverage` (needs `SONAR_TOKEN`). |
| `ApiChecks` | Runs `Awaiten.Api.Tests`. |
| `Pack` | `UpdateReadme` and `Compile`, then moves the `Awaiten` and `Awaiten.Extensions.DependencyInjection` packages to `Artifacts/Packages`. |
| `Benchmarks` | `BenchmarkDotNet` (filter via `--benchmark-filter`) and `BenchmarkResult` (logs the reports and, on a PR build, records the PR number for the comment). |
| `BenchmarkComment` | Downloads the `Benchmarks-*` artifacts and posts or updates the PR comment, with a baseline from recent `main` runs. |
| `PublishBenchmarkReport` | Appends the results to `data.js` on the `benchmarks` branch. |
| `AotSample`, `WebSample` | See Samples. |

## CI workflows (`.github/workflows`)

- `ci.yml` (pull requests to `main`): unit tests via `CodeCoverage` on Ubuntu, Windows and macOS, `ApiChecks`, the three benchmark classes, `AotSample` (Windows), `WebSample`, SonarCloud (not for Dependabot or forks) and published test results.
- `ci-analysis.yml` (after `CI` completes): `BenchmarkComment` on the PR.
- `build.yml` (push to `main`, `v*` tags, manual): the same jobs plus `Pack`, `PublishBenchmarkReport` on `main`, and on a tag `Push` to nuget.org (gated on the test, sample, analysis and pack jobs), a GitHub release for non-prerelease tags and release comments on the linked issues and PRs. Actions are pinned to commit SHAs here.
- `notify-docs-site.yml`: triggers the docs site rebuild.
- Dependabot updates NuGet packages and actions weekly (Thursday night) with a `chore` prefix and ignores `Microsoft.CodeAnalysis*`, which sets the oldest compiler that can load the generator; Mergify squash-merges Dependabot PRs once the unit and API tests pass. Release notes are grouped by label (`.github/release.yml`).
