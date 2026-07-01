namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     How a constructor parameter is satisfied from the graph. <see cref="Direct" /> resolves the
///     service itself; <see cref="Func" /> and <see cref="Lazy" /> are relationship types that defer
///     resolution behind a <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c> over the owning container or scope.
///     <see cref="Arg" /> is supplied at resolve time from a <c>Func&lt;TArg…, T&gt;</c> relationship
///     rather than from the graph. <see cref="Owned" /> resolves the service into a dedicated throwaway
///     scope and hands the caller an <c>Owned&lt;T&gt;</c> disposal handle (it defers like a relationship
///     type, so it contributes no graph edge). <see cref="CancellationToken" /> is an asynchronous factory
///     method's <c>System.Threading.CancellationToken</c> parameter, satisfied by forwarding the resolve-time
///     token (the async creator's) rather than from the graph - so, like <see cref="Arg" />, it contributes no
///     edge. A synchronous factory or a constructor has no ambient token, so its <c>CancellationToken</c> is an
///     ordinary <see cref="Direct" /> dependency instead. <see cref="Task" />, <see cref="FuncTask" /> and
///     <see cref="LazyTask" /> are the asynchronous counterparts of <see cref="Direct" />/<see cref="Func" />/
///     <see cref="Lazy" />: awaitable relationships that resolve (and initialize) the target through its async
///     resolver. All of these hand back a handle/awaitable rather than the resolved-and-initialized value, so
///     they launder async taint - which is what lets a synchronously-resolvable consumer hold one over an
///     async-initialized service without becoming async-tainted (and without tripping AWT119/AWT120). The
///     <see cref="Func" />/<see cref="Lazy" /> forms (and <see cref="FuncTask" />/<see cref="LazyTask" />)
///     additionally defer resolution behind a stored closure, so they also break dependency cycles. A bare
///     <see cref="Owned" /> or <see cref="Task" />, by contrast, resolves its target at construction time
///     (synchronously, or in an async resolver's synchronous prefix), so a cycle closed through one of them
///     still overflows at runtime and is reported as AWT102 (the construction graph in BuildConstructionGraph).
/// </summary>
internal enum DependencyKind
{
	Direct,
	Func,
	Lazy,
	Arg,
	Owned,
	CancellationToken,

	/// <summary>A <c>Task&lt;T&gt;</c> dependency: an awaitable that resolves (and initializes) <c>T</c>.</summary>
	Task,

	/// <summary>A <c>Func&lt;…, Task&lt;T&gt;&gt;</c> async factory; like <see cref="Func" /> but awaitable.</summary>
	FuncTask,

	/// <summary>A <c>Lazy&lt;Task&lt;T&gt;&gt;</c> async dependency; like <see cref="Lazy" /> but awaitable.</summary>
	LazyTask,

	/// <summary>
	///     A collection dependency (<c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>,
	///     <c>IReadOnlyCollection&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c> or
	///     <c>T[]</c>): resolves to every registration of the element type <c>T</c> (the parameter's
	///     <c>ServiceType</c>) under the parameter's <c>Key</c> - the unkeyed registrations by default, the
	///     registrations under a <c>[FromKey]</c> key when one is present - materialized eagerly into an array
	///     in registration order. Unlike the relationship kinds it captures its members, so it contributes a
	///     graph edge to each of them (for cycle, captive and async-taint analysis) and never launders their taint.
	/// </summary>
	Enumerable,
}