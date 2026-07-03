namespace Awaiten;

/// <summary>
///     The lifetime a generated container owns a service under, as advertised through
///     <see cref="AwaitenRegistration" />. A <see cref="Singleton" /> is created once and shared for the
///     container's life; a <see cref="Transient" /> is created per request; a <see cref="Scoped" />
///     instance is created once per scope (the container root acts as the outermost scope). A host such as
///     the <c>Awaiten.Extensions.DependencyInjection</c> companion maps these to the matching
///     Microsoft.Extensions.DependencyInjection <c>ServiceLifetime</c>.
/// </summary>
public enum AwaitenLifetime
{
	/// <summary>Created once and shared for the container's life.</summary>
	Singleton,

	/// <summary>Created fresh on every request.</summary>
	Transient,

	/// <summary>Created once per scope.</summary>
	Scoped,
}
