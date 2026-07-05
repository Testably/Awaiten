using System;

namespace Awaiten;

/// <summary>
///     Marks a constructor parameter as supplied at resolve time rather than from the object graph. A service
///     with one or more <c>[Arg]</c> parameters is parameterized: it is built fresh on every request and is
///     resolvable only through a <c>Func&lt;TArg…, TService&gt;</c> whose leading type arguments are the runtime
///     arguments, matched positionally to the marked parameters. The remaining parameters resolve from the graph.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ArgAttribute : Attribute;
