using System;

namespace Awaiten;

/// <summary>
///     A resolution scope created from an <see cref="IAwaitenContainer" />. Scoped registrations
///     resolve to a single instance per scope; transients resolved from the scope are owned by it.
///     Disposing the scope disposes the instances it owns (scoped instances and disposable
///     transients) in reverse order of creation; it does not dispose the container's singletons.
/// </summary>
public interface IAwaitenScope : IAwaitenResolver, IDisposable;
