using System;

namespace Awaiten;

/// <summary>
///     Marks a <c>Factory =</c> method parameter as the slot for the requesting type: the type being constructed
///     that triggered the resolution. The parameter must be <see cref="Type" />. At each construction site the
///     generator fills it with a <c>typeof(…)</c> of the consumer (the type whose constructor parameter or
///     <c>[Inject]</c> property is being satisfied), so a factory can build a context-aware dependency. The
///     canonical case is a logger named after its consumer. The factory's other parameters resolve from the graph.
///     When the service is resolved from the container or scope root there is no requesting type, so the parameter
///     receives <c>null</c>. Declare it <see cref="Type" />? and decide what a null context means. A
///     <c>[RequestingType]</c> parameter that is not <see cref="Type" /> is AWT162.
///     <para>
///         A requesting-type factory is called per consumer and decides its own disposal, so it has no owner scope
///         and does not support the <c>Owned&lt;T&gt;</c> family of relationships (<c>Owned&lt;T&gt;</c>,
///         <c>Func&lt;…, Owned&lt;T&gt;&gt;</c>, <c>Task&lt;Owned&lt;T&gt;&gt;</c> or
///         <c>Func&lt;…, Task&lt;Owned&lt;T&gt;&gt;&gt;</c>): an injected owned form is AWT186 (consume the service
///         directly, or through <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c>). The <c>Lazy&lt;Owned&lt;T&gt;&gt;</c> /
///         <c>Lazy&lt;Task&lt;Owned&lt;T&gt;&gt;&gt;</c> forms are the more specific AWT121 instead (<c>Lazy</c> never
///         unwraps an <c>Owned&lt;T&gt;</c> handle). Consistently, a by-type <c>Resolve&lt;Owned&lt;T&gt;&gt;()</c> of such a service is
///         not offered and throws "not registered" at run time, while <c>Resolve&lt;T&gt;()</c>,
///         <c>Resolve&lt;Func&lt;T&gt;&gt;()</c> and <c>Resolve&lt;Lazy&lt;T&gt;&gt;()</c> succeed.
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class RequestingTypeAttribute : Attribute;
