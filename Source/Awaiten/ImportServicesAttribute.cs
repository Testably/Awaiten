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
///     Only direct dependencies fall through to the external provider; relationship-typed dependencies
///     (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>, <c>Task&lt;T&gt;</c>) that are unregistered are still
///     reported as missing.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ImportServicesAttribute : Attribute
{
}
