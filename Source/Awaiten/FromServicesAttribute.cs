using System;

namespace Awaiten;

/// <summary>
///     Marks a constructor parameter as satisfied from the <em>external</em> provider rather than the
///     Awaiten graph. The generator routes the parameter through the container's
///     <see cref="IExternalResolver" /> (wired by a host, for example to a
///     Microsoft.Extensions.DependencyInjection provider) and does not require an Awaiten registration
///     for it. The parameter's own type is the external service type.
/// </summary>
/// <remarks>
///     Mutually exclusive with <see cref="ArgAttribute" />: a parameter cannot be both a runtime
///     argument and an external dependency (AWT131). Use <see cref="ImportServicesAttribute" /> on the
///     container to make every otherwise-unresolved dependency external without annotating each one.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FromServicesAttribute : Attribute
{
}
