using System;

namespace Awaiten;

/// <summary>
///     Marks a constructor parameter as satisfied from the external provider rather than the Awaiten graph. The
///     generator routes it through the container's <see cref="IExternalResolver" /> (wired by a host, for example
///     to a Microsoft.Extensions.DependencyInjection provider) and requires no Awaiten registration for it. The
///     parameter's own type is the external service type.
/// </summary>
/// <remarks>
///     Combine with <see cref="FromKeyAttribute" /> to resolve a keyed external service; the key is forwarded to
///     the resolver. Mutually exclusive with <see cref="ArgAttribute" />: a parameter cannot be both a runtime
///     argument and an external dependency (AWT134). It also cannot mark the decorator parameter that receives the
///     decorated inner instance (AWT135), since the inner comes from the decorator chain. Use
///     <see cref="ImportServicesAttribute" /> on the container to make every unresolved dependency external without
///     annotating each one.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FromServicesAttribute : Attribute
{
}
