using System;

namespace Awaiten;

/// <summary>
///     Marks a <c>Factory =</c> method parameter as the slot for the <em>requesting type</em> - the type
///     being constructed that triggered the resolution. The parameter must be of type <see cref="Type" />;
///     at each construction site the generator fills it with a <c>typeof(…)</c> of the consumer (the
///     declaring type of the constructor parameter or <c>[Inject]</c> property being satisfied), so a
///     factory can build a context-aware dependency (the canonical case being a logger named after its
///     consumer). The factory's other parameters resolve from the object graph as usual. When the service
///     is resolved from the container or scope root (e.g. <c>Resolve&lt;T&gt;()</c>), where there is no
///     requesting type, the parameter receives <c>null</c> - so declare it <see cref="Type" />? and decide
///     what a null context means. A <c>[RequestingType]</c> parameter that is not <see cref="Type" /> is
///     AWT162.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class RequestingTypeAttribute : Attribute;
