using System;

namespace Awaiten;

/// <summary>
///     Marks a constructor parameter as supplied at resolve time rather than from the object graph.
///     A service with one or more <c>[Arg]</c>-marked parameters is parameterized: it is built fresh on
///     every request from the runtime arguments and is resolvable only through a
///     <c>Func&lt;TArg…, TService&gt;</c> relationship whose leading type arguments are the runtime
///     arguments, matched positionally to the marked parameters (by the parameter's own declared type).
///     The remaining parameters are resolved from the graph as usual.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ArgAttribute : Attribute;
