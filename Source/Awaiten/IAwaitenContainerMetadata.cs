using System.Collections.Generic;

namespace Awaiten;

/// <summary>
///     Compile-time registration metadata exposed by every generated container root, so a host can
///     discover what the container resolves without reflection. The
///     <c>Awaiten.Extensions.DependencyInjection</c> companion uses it to project the container into a
///     Microsoft.Extensions.DependencyInjection service collection.
/// </summary>
public interface IAwaitenContainerMetadata : IAwaitenRoot
{
	/// <summary>
	///     The public, unkeyed service registrations the container can resolve, with their lifetimes.
	/// </summary>
	IReadOnlyList<AwaitenRegistration> Registrations { get; }
}
