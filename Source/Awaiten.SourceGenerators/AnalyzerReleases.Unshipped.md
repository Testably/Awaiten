### New Rules

 Rule ID | Category | Severity | Notes
---------|----------|----------|----------------------------------------------------
 AWT101  | Awaiten   | Error    | A required dependency has no registration
 AWT102  | Awaiten   | Error    | A dependency cycle exists in the object graph
 AWT103  | Awaiten   | Error    | An implementation type is abstract or an interface
 AWT104  | Awaiten   | Error    | An implementation type has no accessible constructor
 AWT105  | Awaiten   | Error    | A singleton captures a shorter-lived scoped dependency
 AWT106  | Awaiten   | Warning  | A synchronous factory's body provably produces an IAsyncInitializable concrete type its declared return type hides
 AWT107  | Awaiten   | Error    | An implementation is registered with conflicting lifetimes
 AWT108  | Awaiten   | Error    | A Factory registration names a member that is not a usable factory method
 AWT109  | Awaiten   | Error    | An Instance registration names a member that is not a usable instance member
 AWT110  | Awaiten   | Error    | A registration sets both Factory and Instance
 AWT111  | Awaiten   | Error    | An implementation is registered with conflicting production strategies
 AWT112  | Awaiten   | Error    | A Factory registration names an overloaded method
 AWT113  | Awaiten   | Error    | A Func<TArg...,T> or Func<TArg...,Task<T>> relationship's runtime arguments do not match the service's [Arg] parameters
 AWT114  | Awaiten   | Error    | A service with [Arg] parameters is registered with a non-Transient lifetime
 AWT115  | Awaiten   | Error    | A service with [Arg] parameters is required as a plain, Lazy<T> or Task<T> dependency instead of a Func<TArg...,T>
 AWT116  | Awaiten   | Error    | A [Container] class is not declared static
 AWT117  | Awaiten   | Error    | Two registrations share the same service type and key
 AWT118  | Awaiten   | Warning  | A root-owned instance holds a Func or Func<…,Task<T>> over a disposable build-on-demand service
 AWT119  | Awaiten   | Error    | A synchronous Func/Lazy/Owned relationship targets an async-initialized service
 AWT120  | Awaiten   | Error    | A synchronous Func/Lazy/Owned relationship reaches an async-tainted service transitively
 AWT121  | Awaiten   | Error    | An Owned<T> disposal handle is requested through a Lazy<Owned<T>> or Lazy<Task<Owned<T>>> relationship
 AWT122  | Awaiten   | Error    | A collection dependency has an async-tainted member but is materialized synchronously
 AWT123  | Awaiten   | Error    | A [Decorate] names a service with no registration to decorate
 AWT124  | Awaiten   | Error    | A decorator has no single constructor parameter assignable to the decorated service type
 AWT125  | Awaiten   | Error    | An open generic registration's implementation and service have different arity
 AWT126  | Awaiten   | Error    | A required closed type violates the open generic implementation's type-parameter constraints
 AWT127  | Awaiten   | Error    | The typeof-argument form of a lifetime attribute must receive an unbound open generic type
 AWT128  | Awaiten   | Error    | An open generic implementation does not expose its service with type parameters in declaration order
 AWT129  | Awaiten   | Error    | Open generic expansion nested too deep, indicating an unbounded generic recursion
 AWT130  | Awaiten   | Error    | A [Composite] implementation has no collection parameter of the composed service to fan out to
 AWT131  | Awaiten   | Warning  | A [Composite] type is also registered as an ordinary member of the service it composes
 AWT132  | Awaiten   | Error    | More than one [Composite] names the same service
 AWT133  | Awaiten   | Error    | A [Composite]'s collection parameter is of a base type of the composed service, not the composed service itself
 AWT134  | Awaiten   | Warning  | A composition attribute ([Singleton], [Scan], [Decorate], …) is applied in an assembly that declares no [Container]; composition belongs on the [Container] or an imported [Module]
 AWT135  | Awaiten   | Warning  | A resolver interface (IAwaitenResolver/IAwaitenScope/IAwaitenRoot) is injected into a type that is not a [Container] composition root (Service Locator)
 AWT136  | Awaiten   | Error    | An [Inject] property has no set or init accessor the container can assign through
 AWT137  | Awaiten   | Error    | An injected property is marked [Arg]
 AWT138  | Awaiten   | Warning  | A [Scan] matched no concrete type assignable to its marker
 AWT139  | Awaiten   | Warning  | A [Scan(As = ScanAs.Marker)] matched a type with no assignable interface
 AWT140  | Awaiten   | Warning  | A [Scan(InAssembliesOf = …)] named an assembly with no candidate types
 AWT141  | Awaiten   | Warning  | A [Scan(SkipUnconstructable = true)] match the container cannot construct is skipped
 AWT142  | Awaiten   | Warning  | Scans register one implementation with conflicting lifetimes
 AWT143  | Awaiten   | Error    | A [Scan(InAssembliesOf = …)] resolved to no assembly at all
 AWT144  | Awaiten   | Error    | An [Inject(Deferred = true)] property is init-only or required rather than assignable after construction
 AWT145  | Awaiten   | Error    | A deferred property cycle consists entirely of transients and cannot terminate
 AWT146  | Awaiten   | Error    | A deferred property cycle includes an async-initialized service and cannot terminate
 AWT147  | Awaiten   | Error    | A deferred property cycle still traverses a construction-time edge that duplicates a cached participant or recurses forever
 AWT148  | Awaiten   | Warning  | Two overridable default registrations provide the same service ambiguously
 AWT149  | Awaiten   | Error    | An [Import] names a type that is not marked [Module]
 AWT150  | Awaiten   | Error    | An imported module has its own [Import], which is not followed
 AWT151  | Awaiten   | Warning  | An imported module declares no registrations
 AWT152  | Awaiten   | Error    | An imported [Module] class is not declared static
 AWT153  | Awaiten   | Error    | A module Factory/Instance member is not accessible from the generated container
 AWT154  | Awaiten   | Error    | An imported module declares a [Scan] but its assembly carries no generated expansion, so it was built without the Awaiten generator (or a version predating self-compiled module scans)
 AWT155  | Awaiten   | Warning  | Two imported modules strongly register the same service with different implementations
 AWT156  | Awaiten   | Warning  | A generated Root/Scope is disposed synchronously although its container owns a service that implements IAsyncDisposable but not IDisposable
 AWT157  | Awaiten   | Error    | An [Inject(Optional = true)] property is required and cannot be omitted from the object initializer
 AWT158  | Awaiten   | Warning  | An [Inject(Optional = true)] property is init-only, so an unregistered dependency leaves it permanently at its default
 AWT159  | Awaiten   | Error    | A keyed dictionary (IReadOnlyDictionary<TKey, TService>) has a key type that is neither string nor an enum, or that mismatches its keyed registrations
 AWT160  | Awaiten   | Error    | A [FromKey] is applied to a synthesized keyed collection (IReadOnlyDictionary<string, TService>), which resolves every key
 AWT161  | Awaiten   | Error    | An Eager singleton is async-initialized and cannot be constructed synchronously at container build time
 AWT162  | Awaiten   | Error    | A [RequestingType] factory parameter is not of type System.Type
 AWT163  | Awaiten   | Error    | A factory has both a [RequestingType] parameter and an [Arg] runtime-argument parameter
 AWT164  | Awaiten   | Error    | An OnActivated/OnRelease registration names a member that is not a usable lifecycle hook
 AWT165  | Awaiten   | Error    | An OnActivated/OnRelease lifecycle hook is set on a pre-built Instance registration, which the container does not own
 AWT166  | Awaiten   | Error    | An implementation is registered with conflicting OnActivated/OnRelease/Eager directives that coalescing would silently drop
 AWT167  | Awaiten   | Warning  | A WhenInjectedInto contextual binding never applies because the named consumer has no unkeyed direct constructor parameter to redirect
 AWT168  | Awaiten   | Error    | A registration sets both WhenInjectedInto and Key, which claim the same resolution slot, so the Key is silently dropped
 AWT169  | Awaiten   | Error    | Two implementations set WhenInjectedInto for the same service and consumer, so the contextual resolution would be ambiguous
 AWT170  | Awaiten   | Error    | A [Key] or [FromKey] uses a constant whose type is not a supported key type (string, enum, or typeof)
 AWT171  | Awaiten   | Warning  | A closing of an open generic [Decorate]/[Composite] violates the decorator's or composite's constraints and is skipped
 AWT172  | Awaiten   | Warning  | A [Scan]'s name, namespace or exclude filters removed every marker-assignable match, so the scan registers nothing
 AWT173  | Awaiten   | Warning  | A [Scan] exclusion (an Exclude type or a !-prefixed name/namespace pattern) matched no candidate and is likely stale
 AWT174  | Awaiten   | Warning  | A [Scan] include pattern (* for a name, ** for a namespace) matches every candidate and does not narrow the scan
 AWT175  | Awaiten   | Error    | A type is declared [ImportService<T>] but is also registered on the container
 AWT176  | Awaiten   | Warning  | A type declared [ImportService<T>] is never consumed by any dependency in the graph
 AWT177  | Awaiten   | Error    | An [InjectProperty<TImplementation>] names a member that is not a settable property on TImplementation
 AWT178  | Awaiten   | Error    | An [InjectProperty<TImplementation>] targets a TImplementation produced by a Factory or Instance registration
 AWT179  | Awaiten   | Warning  | Two [InjectProperty<TImplementation>] entries name the same property of the same implementation
 AWT180  | Awaiten   | Warning  | An [InjectProperty<TImplementation>] targets a TImplementation with no container-constructed registration, so the entry is never applied
 AWT181  | Awaiten   | Warning  | A property is named by both [Inject] and an [InjectProperty<TImplementation>] entry, so the entry's flags are ignored
 AWT182  | Awaiten   | Warning  | A [Scan(As = ScanAs.MatchingInterface)] matched a type that implements no interface named I + its own name
 AWT183  | Awaiten   | Error    | A markerless [Scan] includes the Marker exposure or declares no scoping filter
 AWT184  | Awaiten   | Warning  | A markerless [Scan] matched candidates but registered none of them
 AWT185  | Awaiten   | Error    | A [Scan]'s As resolved to no ScanAs flag, so it would register nothing
 AWT186  | Awaiten   | Error    | An Owned<T> relationship (or its Func/Task forms) targets a service produced by a requesting-type factory, which has no owner scope
 AWT187  | Awaiten   | Warning  | A [Scan(As = ScanAs.MatchingInterface)] matched a type implementing several same-named convention interfaces, so it registers under each
 AWT188  | Awaiten   | Warning  | A scan match's only exposure interface is inaccessible to the generated container, so the match is not registered
 AWT189  | Awaiten   | Error    | A lifecycle hook parameter (after the instance) is marked [Arg], but a hook resolves its parameters from the graph
 AWT190  | Awaiten   | Error    | A lifecycle hook (OnActivated / OnRelease) names an overloaded method, so there is no way to choose which one to call
 AWT191  | Awaiten   | Error    | An OnRelease hook parameter is a Func/Lazy relationship, which would defer resolution past the owner's teardown
 AWT192  | Awaiten   | Error    | SuppressDisposal is set on a pre-built Instance, which the container does not own or dispose, so it has no effect
 AWT193  | Awaiten   | Warning  | A [Scan] matched a type that is inaccessible to the generated container, so it is not registered
 AWT194  | Awaiten   | Error    | A [Module] that declares a [Scan] is not partial (or is nested in a non-partial type), so its scan cannot be self-compiled
 AWT195  | Awaiten   | Error    | A self-compiled module [Scan] match has a constructor parameter type inaccessible outside the module's assembly
 AWT196  | Awaiten   | Warning  | A self-compiled module [Scan] match has no exposure interface accessible outside the module's assembly, so it is skipped
 AWT197  | Awaiten   | Error    | A self-compiled module [Scan] match would be exposed under multiple interfaces, which a single-exposure factory cannot express
 AWT198  | Awaiten   | Error    | A generic lifecycle hook on an open-generic [Scan] marker could bind a match through more than one closed marker form, so its type arguments are ambiguous
 AWT199  | Awaiten   | Warning  | Two [Scan] attributes match one implementation with conflicting OnActivated/OnRelease hooks; the first scan's hook is used
 AWT200  | Awaiten   | Error    | A self-compiled module [Scan] match carries [Inject]/[Arg] metadata the generated factory cannot mirror
 AWT201  | Awaiten   | Error    | A generic [Module] (or one nested in a generic type) declares a [Scan], so no closed module exists for a consumer to import
 AWT202  | Awaiten   | Error    | A module [Scan] declares InAssembliesOf, but a self-compiled scan sweeps only the module's own assembly
 AWT203  | Awaiten   | Error    | A self-compiled module [Scan] hook has a parameter type inaccessible outside the module's assembly, so the generated hook wrapper cannot expose it
 AWT204  | Awaiten   | Error    | A self-compiled module [Scan] hook parameter carries [FromKey]/[Inject] metadata the generated hook wrapper cannot mirror
