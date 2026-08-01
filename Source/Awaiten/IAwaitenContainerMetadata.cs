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
/// <remarks>
///     Implemented by the generator, not by hand, so it may gain members as the container learns to advertise
///     more about itself. Consume it; do not implement it.
/// </remarks>
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

	/// <summary>
	///     Why the container has <paramref name="serviceType" /> but will not hand it over synchronously, or
	///     <see langword="null" /> when it has no such reason. This is the message <c>Resolve</c> throws, made
	///     available without throwing.
	/// </summary>
	/// <remarks>
	///     <para>
	///         Ask this after <c>TryResolve</c> reports no service, to tell the two negatives apart: "no
	///         registration at all" and "registered, but withheld from the synchronous path" are the same
	///         <see langword="false" /> there, yet call for opposite responses. The first is genuinely not the
	///         container's; the second is a mistake with a named fix. A host adapter that must answer an unknown
	///         type with <see langword="null" />, because that is how it says "not mine", needs this to report the
	///         container's specific failure rather than the framework's generic one.
	///     </para>
	///     <para>
	///         A service is withheld when it needs asynchronous initialization but was reached synchronously, or
	///         when the strict lifetime default declines to build it on the root because the root would then track
	///         it for the container's lifetime, which covers a disposable or release-hooked transient and the
	///         shapes over one. A variance-compatible closing reports its nearest withheld candidate's reason,
	///         matching what <c>Resolve</c> throws for it. Rather than enumerate the cases, read the reason: each
	///         names the service and what to change.
	///     </para>
	///     <para>
	///         <see cref="IsResolvable" /> is no substitute, in either direction. It answers existence, so it
	///         reports a root-withheld transient as resolvable and an async-initialized service as not, while both
	///         have a reason here.
	///     </para>
	///     <para>
	///         The answer belongs to the container root: a child scope builds a root-withheld transient normally,
	///         so only the root can say it is withholding one.
	///     </para>
	/// </remarks>
	/// <param name="serviceType">The service type to ask about.</param>
	/// <param name="key">The resolution key, or <see langword="null" /> for the unkeyed registration.</param>
	string? WithheldReason(Type serviceType, object? key);
}
