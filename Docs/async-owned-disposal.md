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

**Constraint to keep the promise honest:** disposal is still synchronous `IDisposable` at this stage. For an `IAsyncDisposable`-**only** target (no `IDisposable`), the child scope's synchronous `Dispose()` would find nothing to dispose — a silent leak. So Stage 2 must **diagnose** an `IAsyncDisposable`-only owned target ("async disposal not yet supported; implement `IDisposable`, or await Stage 3") rather than appear to handle it. Stage 3 lifts that restriction with no API change.

## Stage 3 — `IAsyncDisposable` support (async disposal pipeline)

The actual async-disposal feature lives in the disposal pipeline, independent of the Stage 2 surface syntax:

- Track `IAsyncDisposable` instances in the generated scope, not just `IDisposable`.
- Give the generated `Scope`/`Root` a `DisposeAsync()` that awaits `IAsyncDisposable.DisposeAsync()` with an `IDisposable` fallback.
- Make `Owned<T>` implement `IAsyncDisposable` **additively**:
  ```csharp
  public readonly struct Owned<T> : IDisposable, IAsyncDisposable
  {
      public void Dispose() => _scope?.Dispose();
      public ValueTask DisposeAsync() => _scope is IAsyncDisposable a ? a.DisposeAsync() : default;
  }
  ```
  Existing `using`/`.Dispose()` callers are unchanged; new code opts into `await using`. The relationship type (`Task<Owned<T>>`) does not change shape — so Stage 2 does not need to be revisited.
- Lift the Stage 2 diagnostic that rejected `IAsyncDisposable`-only owned targets.

**Trap to avoid:** never make `Owned<T>.Dispose()` block on `DisposeAsync().GetAwaiter().GetResult()` to fake async disposal. Sync-over-async risks deadlock and bakes the wrong contract into the public API — *that* is the choice that would foreclose Stage 3. Keep synchronous `Dispose()` for `IDisposable`; add real `DisposeAsync()` here.

## Why the ordering is safe

| Decision | Forecloses `IAsyncDisposable`? |
|---|---|
| Stage 2 via `Func<…, Task<Owned<T>>>` reusing `Owned<T>` | No — `Owned<T>` gains `IAsyncDisposable` additively; the scope async-dispose path is needed regardless |
| A separate new `AsyncOwned<T>` type | No, but leaves two parallel handle types to reconcile |
| Sync-over-async `Dispose()` shim | **Yes — avoid** |
