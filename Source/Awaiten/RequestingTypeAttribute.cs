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
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class RequestingTypeAttribute : Attribute;
