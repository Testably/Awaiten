using System;

namespace Awaiten.WebSample.Domain;

/// <summary>
///     A scoped service. One Awaiten scope is aligned to each MS.DI scope, and ASP.NET Core opens an MS.DI
///     scope per request, so exactly one of these exists per HTTP request.
/// </summary>
public sealed class OrderContext
{
	public Guid OrderId { get; } = Guid.NewGuid();
}
