using System;

namespace Awaiten;

/// <summary>
///     Declares that the <see cref="ContainerAttribute">container</see> draws on an external provider:
///     any constructor dependency that has no Awaiten registration is satisfied from the container's
///     <see cref="IExternalResolver" /> instead of being reported as a missing dependency (AWT101). This
///     is the blanket counterpart of annotating individual parameters with
///     <see cref="FromServicesAttribute" />.
/// </summary>
/// <remarks>
///     <para>
///         Only unkeyed direct dependencies fall through to the external provider; a keyed
///         (<c>[FromKey]</c>) dependency and relationship-typed dependencies (<c>Func&lt;T&gt;</c>,
///         <c>Lazy&lt;T&gt;</c>, <c>Task&lt;T&gt;</c>) that are unregistered are still reported as missing.
///     </para>
///     <para>
///         This trades compile-time safety for flexibility: a dependency that is simply forgotten or
///         mistyped is no longer reported as missing (AWT101) at build time but is routed to the external
///         provider and, if the host cannot supply it, throws only when first resolved. It also makes every
///         direct dependency satisfiable, so constructor selection can prefer a greedier constructor than it
///         would without the attribute. Annotate individual parameters with
///         <see cref="FromServicesAttribute" /> instead when you want the missing-dependency check to remain
///         in force for everything else.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ImportServicesAttribute : Attribute
{
}
