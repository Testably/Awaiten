namespace Awaiten.SourceGenerators.Entities;

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

	/// <summary>
	///     An asynchronous collection dependency (<c>IAsyncEnumerable&lt;T&gt;</c>): like <see cref="Enumerable" />
	///     it resolves to every registration of the element type <c>T</c> under the parameter's <c>Key</c> and
	///     materializes them eagerly in registration order, but it <em>awaits</em> each async-initialized member -
	///     so, unlike <see cref="Enumerable" />, an async-tainted member is legal (and does not trip AWT122). It
	///     captures its members exactly like <see cref="Enumerable" />, contributing the same graph edge to each of
	///     them (cycle, captive and async-taint analysis) and never laundering their taint: a consumer that injects
	///     an async-tainted async-collection is itself async-tainted and is built through its asynchronous resolver.
	/// </summary>
	AsyncEnumerable,

	/// <summary>
	///     An external dependency: a <c>[FromServices]</c> parameter (or, under <c>[ImportServices]</c>, an
	///     otherwise-unresolved direct dependency) that is satisfied from the container's
	///     <c>IExternalResolver</c> rather than the Awaiten graph. Its <c>ServiceType</c> is the external
	///     service type. It is not registered in the graph, so - like <see cref="Arg" /> - it contributes no
	///     edge and never taints the async analysis; it is resolved at construction time through the container's
	///     external resolver.
	/// </summary>
	External,

	/// <summary>
	///     An awaited collection dependency (<c>Task&lt;C&gt;</c> where <c>C</c> is one of the
	///     <see cref="Enumerable" /> shapes, e.g. <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> or
	///     <c>Task&lt;T[]&gt;</c>): resolves to every registration of the element type <c>T</c> under the
	///     parameter's <c>Key</c>, materialized eagerly in registration order with each async-initialized member
	///     awaited behind the returned task - the second shape (besides <see cref="AsyncEnumerable" />) through
	///     which an async-tainted member is legal. Unlike <see cref="AsyncEnumerable" /> it launders the members'
	///     taint, exactly as the bare <see cref="Task" /> relationship does: the members are awaited inside the
	///     produced task, not at the consumer's construction, so the consumer stays synchronously constructible.
	///     The task still starts materializing at construction time, so - again like the bare <see cref="Task" /> -
	///     it closes cycles (the construction graph) even though it contributes no taint/captive edge.
	///     <c>ValueTask&lt;C&gt;</c> is deliberately not recognized, for the same reason a bare
	///     <c>ValueTask&lt;T&gt;</c> is not a relationship type: a stored ValueTask may only be awaited once.
	/// </summary>
	AwaitedEnumerable,

	/// <summary>
	///     A keyed-collection dependency (<c>IReadOnlyDictionary&lt;string, T&gt;</c>): resolves to every
	///     <em>keyed</em> registration of the service type <c>T</c> (the parameter's <c>ServiceType</c>),
	///     materialized eagerly into a dictionary keyed by each registration's <c>[Key]</c>, in registration
	///     order (each member keeping its own lifetime). Like <see cref="Enumerable" /> it captures its members,
	///     so it contributes a graph edge to each of them (cycle, captive and async-taint analysis) and never
	///     launders their taint - a synchronous dictionary cannot await an async-initialized member, so such a
	///     member trips AWT122 exactly as it does through <see cref="Enumerable" />. An empty index is legal (it
	///     yields an empty dictionary, not AWT101). An explicitly registered dictionary service of the exact
	///     declared type (under the dependency's key) preempts synthesis: the dependency is rewritten to
	///     <see cref="Direct" /> and resolves that registration, whatever its key type. v1 synthesis supports
	///     <c>string</c> keys only; a non-<c>string</c> key type is reported as AWT156, and a <c>[FromKey]</c>
	///     that survives suppression as AWT157 (the synthesized dictionary resolves every key).
	/// </summary>
	KeyedCollection,
}
