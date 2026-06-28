using System;

namespace Awaiten;

// S2326: the type parameter is the Awaiten source generator's input — it reads the runtime-argument
// type from the attribute's type argument via Roslyn symbols, so it is intentionally not referenced
// in the attribute body.
#pragma warning disable S2326

/// <summary>
///     Marks a constructor parameter as supplied at resolve time rather than from the object graph.
///     A service with one or more <c>[Arg]</c>-marked parameters is parameterized: it is built fresh on
///     every request from the runtime arguments and is resolvable only through a
///     <c>Func&lt;TArg…, TService&gt;</c> relationship whose leading type arguments are the runtime
///     arguments, matched positionally to the marked parameters. The remaining parameters are resolved
///     from the graph as usual.
/// </summary>
/// <typeparam name="T">The runtime argument type (the marked parameter's own type).</typeparam>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ArgAttribute<T> : Attribute;

#pragma warning restore S2326
