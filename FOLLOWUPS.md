# Follow-ups

Tracked improvements that are out of scope for the change that surfaced them.

## Diagnose conflicting coalesced registration directives (OnActivated / OnRelease / Eager)

**Status:** done — implemented as **AWT166** (`Diagnostics.ConflictingLifecycleDirectives`).
**Surfaced by:** the lifecycle-hooks review (`feat/lifecycle-hooks`).

### Problem

When one implementation is registered more than once (e.g. a multi-service
`[Singleton<Impl, IA>]` + `[Singleton<Impl, IB>]`), the registrations coalesce onto a
single `ImplInfo` and the **first** one wins. `Coalescing.ConflictsWith`
(`Source/Awaiten.SourceGenerators/AwaitenGenerator.Coalescing.cs:429`) only flags a
conflict when the registrations differ in production *kind / member / origin* — it does
**not** compare `OnActivated`, `OnRelease`, or `Eager`. So:

```csharp
[Singleton<Impl, IA>(OnActivated = nameof(X))]
[Singleton<Impl, IB>(OnActivated = nameof(Y))]   // Y is silently dropped — first wins
```

`Y` (and likewise a differing `OnRelease` or `Eager`) is discarded with no diagnostic.
`EnsureImpl` (`AwaitenGenerator.Coalescing.cs:204`) takes all three from the first
registration only. `Eager` has had this silent-first-wins behavior since before lifecycle
hooks existed; the hooks just add two more directives with the same gap.

### Proposed fix

Extend `ConflictsWith` to also compare `OnActivated`, `OnRelease`, and `Eager` across
registrations of the same implementation, and report a new
`ConflictingProduction`-style diagnostic (AWT111 is the model; next free id is **AWT166**)
instead of silently keeping the first. Point the diagnostic at the losing registration's
location, as the existing conflict reporters do
(`AwaitenGenerator.Coalescing.cs:301-323`).

Cover with generator tests mirroring `DiagnosticTests.Awt107ConflictingLifetime` /
the AWT111 tests: differing `OnActivated`, differing `OnRelease`, and differing `Eager`
each report; identical directives (or one side omitting a directive the other sets — decide
whether "unset vs set" is a conflict or a permissible merge) do not.

### Resolution

Added `ConflictsWith`'s sibling `ConflictingDirective` in `AwaitenGenerator.Coalescing.cs`,
reported through `ReportCoalescingConflicts` as **AWT166** at the losing registration's
location (once per implementation, like AWT107/AWT111). Decision on "unset vs set": a
registration that leaves a directive unset (a null hook, or `Eager` at its default `false`)
states no opinion and **merges** with the winner's — never a conflict. A conflict is only a
later registration explicitly naming a directive value the coalesced first-wins instance
will not use (a differing hook, or opting into `Eager` the winner did not). This closes the
`winner-unset / loser-set` silent drop too, not just `set / set` disagreement. Covered by
`DiagnosticTests.Awt166ConflictingLifecycleDirectives`.
