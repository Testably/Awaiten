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
 AWT134  | Awaiten   | Error    | A constructor parameter is marked both [FromServices] and [Arg]
 AWT135  | Awaiten   | Error    | A decorator's inner parameter is marked [FromServices]
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
 AWT154  | Awaiten   | Error    | An imported module declares a [Scan], which is not collected from modules
 AWT155  | Awaiten   | Warning  | Two imported modules strongly register the same service with different implementations
 AWT156  | Awaiten   | Warning  | A generated Root/Scope is disposed synchronously although its container owns a service that implements IAsyncDisposable but not IDisposable
 AWT157  | Awaiten   | Error    | An [Inject(Optional = true)] property is required and cannot be omitted from the object initializer
 AWT158  | Awaiten   | Warning  | An [Inject(Optional = true)] property is init-only, so an unregistered dependency leaves it permanently at its default
 AWT159  | Awaiten   | Error    | A keyed collection (IReadOnlyDictionary<TKey, TService>) uses a non-string key type
 AWT160  | Awaiten   | Error    | A [FromKey] is applied to a synthesized keyed collection (IReadOnlyDictionary<string, TService>), which resolves every key
 AWT161  | Awaiten   | Error    | An Eager singleton is async-initialized and cannot be constructed synchronously at container build time
 AWT162  | Awaiten   | Error    | A [RequestingType] factory parameter is not of type System.Type
 AWT163  | Awaiten   | Error    | A factory has both a [RequestingType] parameter and an [Arg] runtime-argument parameter
 AWT164  | Awaiten   | Error    | An OnActivated/OnRelease registration names a member that is not a usable lifecycle hook
 AWT165  | Awaiten   | Error    | An OnActivated/OnRelease lifecycle hook is set on a pre-built Instance registration, which the container does not own
 AWT166  | Awaiten   | Error    | An implementation is registered with conflicting OnActivated/OnRelease/Eager directives that coalescing would silently drop
