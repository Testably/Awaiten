# Follow-up: awaited keyed dictionary — `Task<IReadOnlyDictionary<string, TService>>`

Task brief for a fresh agent. Implement the awaited view of the keyed dictionary, mirroring how the awaited collection (`Task<IReadOnlyList<T>>` etc., `DependencyKind.AwaitedEnumerable`) relates to the synchronous collection shapes.

## Motivation

The keyed dictionary (`IReadOnlyDictionary<string, TService>`, `DependencyKind.KeyedCollection`, branch `feat/keyed-dictionary`) is materialized synchronously, so a keyed member that is async-tainted (async-initialized, or produced by an async factory) makes the dictionary unusable: injecting it is AWT122 and there is no by-type entry. Plain collections have two async escape hatches — `IAsyncEnumerable<T>` and the awaited `Task<C>` shapes — but neither preserves keys. `Task<IReadOnlyDictionary<string, TService>>` closes that gap: it hands back a task that resolves every keyed registration, awaiting each async-initialized member's initialization behind the task, exactly as `Task<IReadOnlyList<T>>` does for a collection.

## Current state (read these first)

- `feat/keyed-dictionary` commits `89f34a8` (feature), `ee63735` (owner-qualified static resolver calls), plus the suppression/AWT157 work on top. Read `Tests/Awaiten.Tests/KeyedDictionaryTests.cs` and the keyed tests in `Tests/Awaiten.SourceGenerators.Tests/GeneralTests.cs` for the intended semantics.
- The synchronous keyed dictionary: classification in `AwaitenGenerator.Classification.cs` (`TryGetKeyedCollectionElement`, the `KeyedCollection` branch of `ClassifyDependency`), membership in `AwaitenGenerator.Coalescing.cs` (`AddKeyedMember`), graph edges in `AwaitenGenerator.Analysis.cs` (`AddKeyedCollectionMemberEdges`, `PushTransientKeyedMembers`), emission in `Sources.Construction.cs` (`KeyedCollectionLiteral`) and `Sources.Dispatch.cs` (`AddKeyedCollectionEntries`), names/resolvers in `Sources.Names.cs` (`BuildKeyedNames`).
- The awaited-collection precedent to mirror: `TryGetAwaitedCollection` (classification), the `AwaitedEnumerable` branches throughout `AwaitenGenerator.Analysis.cs` / `Production.cs` / `Scan.cs`, `AwaitedCollectionExpression` (`Sources.Construction.cs`), `AddAwaitedCollectionEntries` + `AwaitedCollectionWithheldMessage` (`Sources.Dispatch.cs` / `Sources.Guidance.cs`).

## Semantics to implement

`Task<IReadOnlyDictionary<string, TService>>` (constructor parameter, `[Inject]` property, and by-type resolution):

1. Resolves every keyed registration of `TService`, keyed by `[Key]`, in registration order, each member keeping its own lifetime — the same membership (`KeyedServiceMembers`) the synchronous dictionary uses.
2. Async-tainted members are **legal** through this shape (no AWT122): the returned task awaits each member's async resolver before completing. All-sync membership completes synchronously (`Task.FromResult`-style), matching how awaited collections degrade.
3. The task itself is handed back synchronously, so the shape joins the synchronous dispatch by type (like awaited collections do) rather than an async arm.
4. Eager behind the task: it materializes its members during construction of the consumer's value, so it contributes construction-graph edges (`includeEagerBare`, cycle detection AWT102), dependency edges (captive AWT105, taint propagation), and is followed by the transitive-disposable walk (AWT118 / strict root-withholding — add the awaited-keyed analogue of the `AwaitedCollectionWithheldMessage` guidance).
5. Empty membership yields a completed task over an empty dictionary, not AWT101.
6. AWT156 for a non-`string` key type, AWT157 for a `[FromKey]` — same reports as the synchronous dictionary, and only when the dependency stays synthesized (see 7).
7. Suppression (all-or-nothing, mirroring `SuppressRegisteredCollectionSynthesis` and `SynthesisSuppressed`):
   - an explicitly registered `Task<IReadOnlyDictionary<string, TService>>` claims its own exact shape (like a registered `Task<C>`);
   - a registered synchronous `IReadOnlyDictionary<string, TService>` claims the awaited view too (like a registered sync collection shape claims `Task<C>`): injecting the awaited sibling is then AWT101 rather than a second dictionary synthesized behind the opaque registration. Extend both the classification-side gate and the dispatch-side gate in `AddKeyedCollectionEntries`'s awaited analogue.

## Implementation sketch

- New `DependencyKind.AwaitedKeyedCollection` (document eagerness, AWT122 exemption, suppression), or fold into the existing kinds if a cleaner factoring emerges — decide after reading how `AwaitedEnumerable` carries `AwaitedCollectionType`.
- Classification: recognize `Task<IReadOnlyDictionary<TKey, T>>` **before** the bare `Task<T>` relationship (mirror the `TryGetAwaitedCollection` ordering comment); carry the declared collection type for suppression and emission.
- Update every `DependencyKind` switch/list that names `AwaitedEnumerable` (satisfiability in `Production.cs`/`Scan.cs`, the unguarded-indexer skip list near the end of `AwaitenGenerator.Analysis.cs`, `ReportWhenUnregistered`, the property AWT101 check) — grep for `AwaitedEnumerable` and decide each site deliberately.
- Emission: an awaited-dictionary expression mirroring `AwaitedCollectionExpression` — note the net48/netstandard2.0 constraints documented at `__AsyncArray` (no async-iterator machinery; awaited collections already solve this, follow their pattern) and the owner-qualified resolver-call rules from commit `ee63735` (`ResolveCall`, `RootOwned`).
- Names: reuse `KeyedNames`; the awaited view needs the members' async resolvers where tainted (`AsyncResolver`), sync resolvers otherwise — mirror how awaited collections pick per-member calls.
- Analyzer parity: `AwaitenAnalyzer` (AWT118) must follow the new kind the same way the generator side does — both go through `AwaitenGenerator.MembershipIndices`/`BuildsFreshDisposable`; mirror the existing `AwaitedEnumerable` handling in `PushFreshTransientDependencies` and the analyzer's `PushTransientDependencies`.

## Tests (all three suites)

- Generator (`GeneralTests.cs` or a dedicated file): materialization with all-sync members (completed task), with an async-tainted member (no AWT122, awaits the member), empty membership, suppression by a registered sync dictionary and by a registered `Task<...>` dictionary, AWT156/AWT157 for the awaited form, by-type dispatch entry.
- Diagnostics: AWT102 cycle closed through the awaited keyed edge (eager bare), AWT105 captive scoped member, AWT118 analyzer walk through the awaited keyed edge, AWT122 **not** reported.
- Runtime (`Awaiten.Tests`, net48 included): awaiting the injected task, async-initialized member is initialized when the task completes, lifetimes (singleton shared / transient fresh / scoped per scope), strict root-withholding parity with the synchronous dictionary.

## Acceptance

`dotnet test -c Release` fully green on all target frameworks; no changes to the behavior of the synchronous keyed dictionary or the existing awaited collections beyond the deliberate suppression interplay in point 7.
