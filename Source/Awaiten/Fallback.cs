namespace Awaiten;

/// <summary>
///     Whether a <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c> registration is an overridable default,
///     and how a collision between two such defaults is handled. An overridable default applies only when
///     nothing else registers the same service; a stronger (non-fallback) registration, or an earlier
///     fallback, replaces it entirely, even in collections. The two overridable modes differ only in whether
///     an unresolved collision between them is reported.
/// </summary>
public enum Fallback
{
	/// <summary>A normal registration that participates fully; a conflict with another is reported (the default).</summary>
	None,

	/// <summary>
	///     An overridable default that expects to be the only one: if another <c>Warn</c> registration provides
	///     the same service with nothing stronger to resolve the tie, AWT148 warns and the first declared wins.
	/// </summary>
	Warn,

	/// <summary>
	///     An overridable default that defers silently: it contributes only when the service is not already
	///     registered, and nothing is reported if something else also provides it.
	/// </summary>
	Silent,
}
