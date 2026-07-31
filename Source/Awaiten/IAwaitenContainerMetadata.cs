using System.Collections.Generic;

namespace Awaiten;

/// <summary>
///     Compile-time registration metadata exposed by every generated container root, so a host can discover what
///     the container resolves and what it expects from an external provider, without reflection. The
///     <c>Awaiten.Extensions.DependencyInjection</c> companion uses it to project the container into a
///     Microsoft.Extensions.DependencyInjection service collection and to wire the container's external
///     dependencies to the host's provider.
/// </summary>
public interface IAwaitenContainerMetadata : IAwaitenRoot, IExternalResolverHost
{
	/// <summary>
	///     The public service registrations the container can resolve, with their lifetimes. Both unkeyed
	///     registrations and user-keyed ones (each carrying its <c>[Key]</c>) are advertised; the container's
	///     internal synthetic keys are not. The implementations of one service type coalesce into a single entry,
	///     the winning registration's, so this advertises what resolves rather than how many members back it.
	/// </summary>
	IReadOnlyList<AwaitenRegistration> Registrations { get; }

	/// <summary>
	///     The <c>[ImportService&lt;T&gt;]</c> / <c>[ImportServices]</c> dependencies the container expects from an external
	///     provider, each carrying its service type and optional <c>[FromKey]</c> resolution key. A host satisfies
	///     these through <see cref="IExternalResolverHost.ExternalResolver" />. Empty when there are none.
	/// </summary>
	IReadOnlyList<AwaitenExternalDependency> ExternalDependencies { get; }
}
