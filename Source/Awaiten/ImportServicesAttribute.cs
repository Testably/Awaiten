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
///         Only unkeyed direct dependencies fall through to the external provider. Unregistered keyed
///         (<c>[FromKey]</c>) and relationship-typed dependencies (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>,
///         <c>Task&lt;T&gt;</c>) are still reported as missing.
///     </para>
///     <para>
///         This trades compile-time safety for flexibility: a forgotten or mistyped dependency is no longer
///         reported as missing (AWT101), but routed to the external provider and, if the host cannot supply it,
///         throws only on first resolve. It also makes every direct dependency satisfiable, so constructor
///         selection can prefer a greedier constructor. Annotate individual parameters with
///         <see cref="FromServicesAttribute" /> instead to keep the missing-dependency check in force elsewhere.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ImportServicesAttribute : Attribute
{
}
