namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     The lifetime of a registration. A <see cref="Singleton" /> is created once and shared for the
///     container's life; a <see cref="Transient" /> is created per request; a <see cref="Scoped" />
///     instance is created once per scope (the container acts as the root scope).
///     Mirrors the public <c>Awaiten.AwaitenLifetime</c> by integer value, cast from the attribute's named
///     argument, so these values must stay aligned with it.
/// </summary>
internal enum Lifetime
{
	Singleton = 0,
	Transient = 1,
	Scoped = 2,
}