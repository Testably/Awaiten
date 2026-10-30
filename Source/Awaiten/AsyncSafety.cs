namespace Awaiten;

/// <summary>
///     How strictly a container keeps asynchronous initialization explicit, set on
///     <see cref="ContainerAttribute" />.
/// </summary>
/// <remarks>
///     The generator reads these as their underlying int off the attribute's <c>TypedConstant</c> (it cannot
///     reference this type across the compilation boundary), so the numeric values are load-bearing: do not
///     renumber or reorder. See <c>ReadSyncAfterInit</c> in the generator.
/// </remarks>
public enum AsyncSafety
{
	/// <summary>
	///     The default. An async-initialized (async-tainted) service is reachable only asynchronously: through
	///     <c>ResolveAsync</c> or a relationship that awaits it, such as <c>Task&lt;T&gt;</c> or
	///     <c>Func&lt;Task&lt;T&gt;&gt;</c>. A synchronous <c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>,
	///     <c>Owned&lt;T&gt;</c> or collection over one is a compile-time error (AWT119, AWT120, AWT122), as is an
	///     async-initialized eager singleton (AWT161), and a synchronous by-type resolve of one throws a guidance
	///     exception pointing at <c>ResolveAsync</c>.
	/// </summary>
	Strict = 0,

	/// <summary>
	///     Opt into synchronous resolution of async-initialized services after <c>InitializeAsync</c> has completed.
	///     A warmed service returns immediately from a synchronous resolve, but resolving one before warm-up blocks
	///     the calling thread until its initialization completes. Prefer warming up first, and beware blocking a
	///     captured synchronization context.
	/// </summary>
	SyncAfterInit = 1,
}
