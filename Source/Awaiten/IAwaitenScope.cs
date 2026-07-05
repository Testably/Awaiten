using System;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     A resolution scope: the unit that resolves services, owns the instances it creates, and can open nested
///     child scopes. The outermost scope (the generated <c>Root</c>) additionally owns the singletons and is an
///     <see cref="IAwaitenRoot" /> that exposes <c>InitializeAsync</c> to warm them. Every scope created from it
///     shares those singletons.
/// </summary>
/// <remarks>
///     Scoped registrations resolve to one instance per scope; transients resolved from a scope are owned by it.
///     Disposing a scope disposes the instances it owns (scoped instances and disposable transients) in reverse
///     order of creation. Disposing the root additionally disposes the singletons; disposing a child scope does not.
/// </remarks>
public interface IAwaitenScope : IAwaitenAsyncResolver, IDisposable
{
	/// <summary>Creates a new child <see cref="IAwaitenScope" />. Disposing the scope disposes the instances it owns.</summary>
	IAwaitenScope CreateScope();

	/// <summary>
	///     Creates a new child <see cref="IAwaitenScope" /> whose async-initialized scoped services have been
	///     eagerly constructed and initialized in dependency order. If initialization throws, the new scope is
	///     disposed rather than leaked.
	/// </summary>
	Task<IAwaitenScope> CreateScopeAsync(CancellationToken cancellationToken = default);
}
