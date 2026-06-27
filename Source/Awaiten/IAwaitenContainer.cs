using System;

namespace Awaiten;

/// <summary>
///     A generated container: the root <see cref="IAwaitenResolver" /> that owns the singletons and
///     hands out scopes.
/// </summary>
/// <remarks>
///     Disposing the container disposes the singletons it owns (and any disposables created while
///     building them), in reverse order of creation.
/// </remarks>
public interface IAwaitenContainer : IAwaitenResolver, IDisposable
{
	/// <summary>
	///     Creates a new <see cref="IAwaitenScope" />. Scoped registrations resolve to a single
	///     instance per scope; disposing the scope disposes the instances it owns.
	/// </summary>
	IAwaitenScope CreateScope();
}
