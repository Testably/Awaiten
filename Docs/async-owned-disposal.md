# Async owned disposal — roadmap

Tracks the staged path from "async services can leak at the root" to "async services support `IAsyncDisposable` with a leak-free per-use handle". Each stage is additive on the previous one; nothing here forecloses the next step.

## Background

The container tracks disposables on the **scope that builds them**. A `Func<…>` is a factory: every call builds a fresh instance. When a **root-owned** holder (a singleton or pre-built instance) captures that factory, it is bound to the root scope, so every instance the factory ever builds is tracked on the root and only released when the whole container is disposed — an unbounded leak. AWT118 catches this for synchronous factories and points the developer at `Func<…, Owned<T>>`, whose handle drains into a throwaway child scope.

`Owned<T>` is structurally synchronous: `__Owned<T>` opens a scope and resolves `T` through a `Func<Scope, T>` with no `await`, and `Owned<T>.Value` is a synchronous property. An async-tainted service has no synchronous resolver to call (it needs `InitializeAsync`), which is why a synchronous `Owned`/`Func`/`Lazy` over one is **AWT119**. So async services have **no `Owned`-based escape hatch** — confirmed by the existing `AsyncRootWithheldMessage` (`Emitter.cs`), whose comment already states "`Owned<T>` is a synchronous relationship, so it is not offered for an async-initialized service" and which directs callers to a child scope instead.

Disposability today is keyed strictly on `System.IDisposable` (`AwaitenGenerator.BuildInstance`). `IAsyncDisposable`-only services are treated as non-disposable everywhere: never tracked, never disposed. The scope exposes `Dispose()` only — there is no `DisposeAsync()`.

## Stage 1 — close the silent leak (done)

AWT118 now also fires for `Func<…, Task<T>>` (`DependencyKind.FuncTask`): the async fresh resolver tracks disposables identically to the synchronous one, so the async factory leaks the same way, and — because `Owned<T>` is unavailable for async — `Func<…, Task<T>>` is the *only* deferred factory that can reach an async-tainted target, so this was the one place the leak could occur unguarded. Because the `Owned<T>` remedy is illegal for async, the AWT118 message carries the remedy as a `{2}` argument: the sync case keeps "resolve it as `Func<…, Owned<T>>`", the async case points at an explicitly scoped resolution (`await CreateScopeAsync()`, `ResolveAsync` from that scope, then dispose the scope), mirroring `AsyncRootWithheldMessage`. Strict lifetime safety reports it as the same non-suppressible error as the sync case.

This is correct but blunt: under strict safety it hard-blocks a root-held `Func<…, Task<T>>` over a disposable async transient, and the only remedy is manual scoping. Stage 2 gives async services a real one-liner.

## Stage 2 — `Func<…, Task<Owned<T>>>` (async owned handle, sync disposal)

Add the async mirror of `Func<…, Owned<T>>`, **reusing the existing `Owned<T>` struct** — no new public type:

- Recognize `Task<Owned<T>>` / `Func<…, Task<Owned<T>>>` in `ClassifyRelationship` (a branch beside the existing `IsOwned(service)` one).
- Emit an async `__OwnedAsync<T>` helper: open a child scope with `CreateScopeAsync` (which already warms async-initialized scoped services), `await` the target's async resolver into it (covering resolution *and* `InitializeAsync`), and return an `Owned<T>` over that scope.
- AWT113 runtime-argument validation already generalizes to `FuncTask`; extend it to this kind too.

Once this exists, the Stage 1 AWT118 message (and `AsyncRootWithheldMessage`) can additionally point at `Func<…, Task<Owned<T>>>` rather than only "open a child scope by hand".

Stage 2 needs **no** `IAsyncDisposable` at all — it only async-resolves into a child scope and returns an ordinary `Owned<T>`, so it ships on every target framework (net48 included), with synchronous `IDisposable` disposal of the handle. Async *disposal* of what the handle owns is Stage 3.

**Status: done.** `Task<Owned<T>>` and `Func<…, Task<Owned<T>>>` (`DependencyKind` `Task`/`FuncTask` with `ProducesOwned`) are classified in `ClassifyParameter`/`ClassifyRelationship`, emitted through the new `__OwnedAsync<T>` helper (the async twin of `__Owned<T>`: open a child scope, await the target's async resolver into it, return the `Owned<T>`), and validated by AWT113. The AWT118 message and `AsyncRootWithheldMessage` now point at `Func<…, Task<Owned<T>>>` as the leak-free async remedy.

## Stage 3 — `IAsyncDisposable` support (async disposal pipeline) — done

Implemented as an additive, **net8.0+ / polyfilled-only** capability, gated by detection rather than a forced dependency (per the project decision: net8.0+ async disposal, no new NuGet dependency):

- **Detection, not dependency.** The generator checks `compilation.GetTypeByMetadataName("System.IAsyncDisposable")`. When present (net5.0+/netstandard2.1+ in-box, or an older target that added `Microsoft.Bcl.AsyncInterfaces`), it emits the async-disposal machinery; when absent (e.g. net48 without the polyfill) the container is synchronous-dispose only and references no `IAsyncDisposable`, so it still compiles. No dependency was added to the library.
- **Tracking.** `IAsyncDisposable` is folded into the disposal decision (`InstanceModel.NeedsDisposal = IsDisposable || IsAsyncDisposable`), which also flows into the leak analyses (AWT118 / by-type withholding / `BuildsFreshDisposable`). Tracked instances live in the same `List<object>`; the drain pattern-matches at runtime.
- **`DisposeAsync`.** The generated **concrete `Scope`** implements `IAsyncDisposable` (the `Root` inherits it) and gets a `DisposeAsync()` that drains newest-first, awaiting `IAsyncDisposable.DisposeAsync()` and falling back to `Dispose()`. This was **not** added to the `IAwaitenScope` interface — doing so would break every hand-implementer (the MS.DI adapter, test doubles, external code); a concrete-class implementation gives `await using` on the container/scope without that break.
- **Sync `Dispose()` throws.** When the synchronous `Dispose()` meets an instance that is `IAsyncDisposable` but not `IDisposable`, it throws `InvalidOperationException` (matching Microsoft.Extensions.DependencyInjection) rather than blocking on an async dispose.
- **`Owned<T>`** implements `IAsyncDisposable` under `#if NET || NETSTANDARD2_1_OR_GREATER` (additive; `using`/`.Dispose()` callers unchanged), with `DisposeAsync()` routing to the backing scope via `_scope is IAsyncDisposable`.

**Trap avoided:** `Owned<T>.Dispose()` / the scope's `Dispose()` never block on `DisposeAsync().GetAwaiter().GetResult()`. Sync-over-async risks deadlock and would bake the wrong contract into the public API; the synchronous path stays synchronous and throws for async-only services instead.

## Why the design holds

| Decision | Outcome |
|---|---|
| Async-owned resolution (`Func<…, Task<Owned<T>>>`) reuses `Owned<T>` | One handle type; ships on all TFMs with sync disposal |
| `IAsyncDisposable` detected in the compilation, not depended upon | net8.0+ (and polyfilled) get async disposal; net48 stays sync-only and still compiles; no new dependency |
| `IAsyncDisposable` on the concrete `Scope`, not the `IAwaitenScope` interface | `await using` works on containers/scopes without breaking hand-implementers |
| Sync `Dispose()` throws on an async-only service | No sync-over-async; clear contract |
