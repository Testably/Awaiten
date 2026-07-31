using System;
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

	/// <summary>
	///     Whether the container can resolve <paramref name="serviceType" /> under <paramref name="key" /> (pass
	///     <see langword="null" /> for the unkeyed registration), answered from the generated dispatch tables
	///     without constructing anything.
	/// </summary>
	/// <remarks>
	///     <para>
	///         This is the question a host has to answer before it decides where a value comes from. The
	///         <c>Awaiten.Extensions.DependencyInjection</c> companion serves
	///         <c>IServiceProviderIsService</c> from it, and it covers every shape the container dispatches, not
	///         just what <see cref="Registrations" /> advertises: the relationship shapes
	///         (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>, <c>Owned&lt;T&gt;</c>), the synthesized collections and
	///         keyed dictionaries, the awaited views over them, and a variance-compatible closing of a registered
	///         variant generic interface.
	///     </para>
	///     <para>
	///         It reports whether the service <em>exists</em>, not whether resolving it from a particular scope
	///         will succeed. A service the container withholds on the root, a disposable transient under the
	///         strict lifetime default, is resolvable, and <c>Resolve</c> names why the root cannot serve it.
	///         An async-only service is not synchronously resolvable and so reports <see langword="false" />;
	///         ask for its <c>Task&lt;T&gt;</c> instead.
	///     </para>
	/// </remarks>
	/// <param name="serviceType">The service type to test.</param>
	/// <param name="key">The resolution key, or <see langword="null" /> for the unkeyed registration.</param>
	bool IsResolvable(Type serviceType, object? key);
}
