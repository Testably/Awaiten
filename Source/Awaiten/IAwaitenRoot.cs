using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     The composition root: the outermost <see cref="IAwaitenScope" /> (the generated <c>Root</c>, the usable
///     container instance) that owns the singletons. It is the only scope constructed directly
///     (<c>new MyContainer.Root()</c>) rather than through <see cref="IAwaitenScope.CreateScope" /> /
///     <see cref="IAwaitenScope.CreateScopeAsync" />, so it is the only one that exposes an explicit
///     <see cref="InitializeAsync" /> to warm itself. Child scopes are warmed when they are created.
/// </summary>
/// <remarks>
///     The root also carries the compile-time metadata a host needs to discover what the container resolves and
///     what it expects from an external provider, without reflection. The
///     <c>Awaiten.Extensions.DependencyInjection</c> companion uses it to project the container into a
///     Microsoft.Extensions.DependencyInjection service collection and to wire the container's external
///     dependencies to the host's provider. The generator implements this interface, so it may gain members as the
///     container learns to advertise more about itself. Consume it; do not implement it.
/// </remarks>
public interface IAwaitenRoot : IAwaitenScope, IAwaitenExternalResolverHost
{
	/// <summary>
	///     The public service registrations the container can resolve, with their lifetimes. Both unkeyed
	///     registrations and keyed ones (each carrying its <c>Key</c> value) are advertised; the container's
	///     internal synthetic keys are not. The implementations of one service type coalesce into a single entry,
	///     the winning registration's, so this advertises what resolves rather than how many members back it.
	/// </summary>
	IReadOnlyList<AwaitenRegistration> Registrations { get; }

	/// <summary>
	///     The <c>[ImportService&lt;T&gt;]</c> / <c>[ImportServices]</c> dependencies the container expects from an external
	///     provider, each carrying its service type and optional <c>[FromKey]</c> resolution key. A host satisfies
	///     these through <see cref="IAwaitenExternalResolverHost.ExternalResolver" />. Empty when there are none.
	/// </summary>
	IReadOnlyList<AwaitenExternalDependency> ExternalDependencies { get; }

	/// <summary>
	///     Eagerly constructs and initializes the async-initialized singletons in dependency order. Idempotent and
	///     thread-safe: each singleton is initialized at most once. Call this once at startup so later resolution
	///     hands back warmed singletons. Scoped async services are warmed per scope by
	///     <see cref="IAwaitenScope.CreateScopeAsync" />.
	/// </summary>
	Task InitializeAsync(CancellationToken cancellationToken = default);

	/// <summary>
	///     Whether the root can resolve <paramref name="serviceType" /> under <paramref name="key" /> synchronously,
	///     answered from the generated dispatch tables without constructing anything. When it cannot, the result
	///     tells "no registration at all" apart from "registered, but not served synchronously here" and carries the
	///     reason <c>Resolve</c> would throw.
	/// </summary>
	/// <remarks>
	///     <para>
	///         This is the question a host has to answer before it decides where a value comes from. The
	///         <c>Awaiten.Extensions.DependencyInjection</c> companion serves <c>IServiceProviderIsService</c> from
	///         it. It covers every shape the container dispatches, not just what <see cref="Registrations" />
	///         advertises: the relationship shapes (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>,
	///         <c>Owned&lt;T&gt;</c>), the synthesized collections and keyed dictionaries, the awaited views over
	///         them, and a variance-compatible closing of a registered variant generic interface, which reports its
	///         nearest candidate's answer.
	///     </para>
	///     <para>
	///         The answer belongs to the root: a child scope builds a service the root withholds
	///         (<see cref="AwaitenResolvabilityStatus.WithheldOnRoot" />) normally.
	///     </para>
	/// </remarks>
	/// <param name="serviceType">The service type to ask about.</param>
	/// <param name="key">The resolution key, or <see langword="null" /> for the unkeyed registration.</param>
	AwaitenResolvability GetResolvability(Type serviceType, object? key);
}
