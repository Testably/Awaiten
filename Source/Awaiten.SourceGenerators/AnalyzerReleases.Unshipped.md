### New Rules

 Rule ID | Category | Severity | Notes
---------|----------|----------|----------------------------------------------------
 AWT101  | Awaiten   | Error    | A required dependency has no registration
 AWT102  | Awaiten   | Error    | A dependency cycle exists in the object graph
 AWT103  | Awaiten   | Error    | An implementation type is abstract or an interface
 AWT104  | Awaiten   | Error    | An implementation type has no accessible constructor
 AWT105  | Awaiten   | Error    | A singleton captures a shorter-lived scoped dependency
 AWT106  | Awaiten   | Warning  | A disposable transient resolved from the root accumulates
 AWT107  | Awaiten   | Error    | An implementation is registered with conflicting lifetimes
 AWT108  | Awaiten   | Error    | A Factory registration names a member that is not a usable factory method
 AWT109  | Awaiten   | Error    | An Instance registration names a member that is not a usable instance member
 AWT110  | Awaiten   | Error    | A registration sets both Factory and Instance
 AWT111  | Awaiten   | Error    | An implementation is registered with conflicting production strategies
 AWT112  | Awaiten   | Error    | A Factory registration names an overloaded method
 AWT113  | Awaiten   | Error    | A Func<TArg...,T> relationship's runtime arguments do not match the service's [Arg] parameters
 AWT114  | Awaiten   | Error    | A service with [Arg] parameters is registered with a non-Transient lifetime
 AWT115  | Awaiten   | Error    | A service with [Arg] parameters is required as a plain or Lazy<T> dependency instead of a Func<TArg...,T>
