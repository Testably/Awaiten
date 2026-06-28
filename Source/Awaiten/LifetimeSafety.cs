namespace Awaiten;

/// <summary>
///     How strictly a container guards against unbounded disposal leaks, set on
///     <see cref="ContainerAttribute" />.
/// </summary>
public enum LifetimeSafety
{
	/// <summary>
	///     The default. A disposable transient (or parameterized service) cannot be reached in a way that
	///     accumulates on the container root: a root-owned <c>Func&lt;…&gt;</c> over one is a compile-time
	///     error (AWT117), and its bare type and plain <c>Func</c> factory are withheld from by-type
	///     resolution - they throw a guidance exception pointing at <c>Owned&lt;T&gt;</c>. Such services
	///     remain reachable through constructor injection and <c>Owned&lt;T&gt;</c> /
	///     <c>Func&lt;…, Owned&lt;T&gt;&gt;</c>. This deliberately diverges from
	///     Microsoft.Extensions.DependencyInjection, which lets a disposable transient be resolved (and leak)
	///     from any provider including the root.
	/// </summary>
	Strict = 0,

	/// <summary>
	///     Maximum interoperability. Everything is resolvable everywhere, matching
	///     Microsoft.Extensions.DependencyInjection semantics; the root-accumulating <c>Func</c> pattern is
	///     reported as a warning (AWT117) rather than an error, and <c>Owned&lt;T&gt;</c> is available as the
	///     leak-free alternative.
	/// </summary>
	Loose = 1,
}
