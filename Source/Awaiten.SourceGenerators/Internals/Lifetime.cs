namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     The lifetime of a registration. A <see cref="Singleton" /> is created once and shared for the
///     container's life; a <see cref="Transient" /> is created per request; a <see cref="Scoped" />
///     instance is created once per scope (the container acts as the root scope).
/// </summary>
internal enum Lifetime
{
	Singleton,
	Transient,
	Scoped,
}