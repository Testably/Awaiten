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
