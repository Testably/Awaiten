namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     How a constructor parameter is satisfied. <see cref="Direct" /> resolves the service itself. Relationship
///     kinds (<see cref="Func" />, <see cref="Lazy" />, <see cref="Owned" />, <see cref="Task" />,
///     <see cref="FuncTask" />, <see cref="LazyTask" />) hand back a handle or awaitable rather than the resolved
///     value, so they launder async taint and let a synchronous consumer hold an async-initialized service without
///     tripping AWT119/AWT120. <see cref="Func" />/<see cref="Lazy" /> (and their Task forms) also defer behind a
///     stored closure, so they break cycles; a bare <see cref="Owned" /> or <see cref="Task" /> resolves at
///     construction time and so still closes a cycle as AWT102. <see cref="Owned" /> resolves into a dedicated
///     throwaway scope and hands back an <c>Owned&lt;T&gt;</c> disposal handle. <see cref="Arg" />,
///     <see cref="RequestingType" /> and <see cref="External" /> are supplied at resolve time, contributing no
///     edge. <see cref="CancellationToken" /> forwards an async factory's resolve-time token (no edge); a
///     synchronous factory or constructor has none, so there it is an ordinary <see cref="Direct" />.
/// </summary>
internal enum DependencyKind
{
	Direct,
	Func,
	Lazy,
	Arg,
	Owned,
	CancellationToken,

	/// <summary>
	///     The <c>[RequestingType]</c> parameter of a <c>Factory =</c> method: filled at each construction site with
	///     the consumer's <c>typeof(…)</c>, or <c>null</c> at a top-level resolve. Not resolved from the graph, so
	///     it contributes no edge.
	/// </summary>
	RequestingType,

	/// <summary>A <c>Task&lt;T&gt;</c> dependency: an awaitable that resolves (and initializes) <c>T</c>.</summary>
	Task,

	/// <summary>A <c>Func&lt;…, Task&lt;T&gt;&gt;</c> async factory; like <see cref="Func" /> but awaitable.</summary>
	FuncTask,

	/// <summary>A <c>Lazy&lt;Task&lt;T&gt;&gt;</c> async dependency; like <see cref="Lazy" /> but awaitable.</summary>
	LazyTask,

	/// <summary>
	///     A collection dependency (<c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, <c>T[]</c>, etc.):
	///     resolves to every registration of element type <c>T</c> under the parameter's <c>Key</c>, materialized
	///     eagerly into an array in registration order. Unlike the relationship kinds it captures its members,
	///     contributing a graph edge to each (cycle, captive, async taint), so a synchronous collection over an
	///     async-initialized member trips AWT122.
	/// </summary>
	Enumerable,

	/// <summary>
	///     An async collection (<c>IAsyncEnumerable&lt;T&gt;</c>): like <see cref="Enumerable" /> but awaits each
	///     async-initialized member, so an async-tainted member is legal (no AWT122). It still captures its members
	///     (same edges) and does not launder their taint, so a consumer injecting one is itself async-tainted.
	/// </summary>
	AsyncEnumerable,

	/// <summary>
	///     An external dependency: a <c>[FromServices]</c> parameter (or, under <c>[ImportServices]</c>, an
	///     otherwise-unresolved direct dependency) satisfied from the container's <c>IExternalResolver</c>, not the
	///     graph. Contributes no edge.
	/// </summary>
	External,

	/// <summary>
	///     An awaited collection (<c>Task&lt;C&gt;</c> where <c>C</c> is an <see cref="Enumerable" /> shape):
	///     resolves the members eagerly with each async-initialized one awaited behind the returned task. Like the
	///     bare <see cref="Task" /> it launders the members' taint (awaited in the task, not at construction), so the
	///     consumer stays synchronously constructible, but it still closes cycles. <c>ValueTask&lt;C&gt;</c> is not
	///     recognized: a stored ValueTask may be awaited only once.
	/// </summary>
	AwaitedEnumerable,

	/// <summary>
	///     A keyed-collection dependency (<c>IReadOnlyDictionary&lt;string, T&gt;</c>): resolves every keyed
	///     registration of <c>T</c> into a dictionary keyed by each <c>[Key]</c>, in registration order. Captures
	///     its members like <see cref="Enumerable" /> (a synchronous dictionary over an async member trips AWT122).
	///     An empty index yields an empty dictionary. An explicitly registered dictionary of the exact type preempts
	///     synthesis (rewritten to <see cref="Direct" />). Synthesis supports <c>string</c> keys only: a non-string
	///     key is AWT159, and a surviving <c>[FromKey]</c> is AWT160.
	/// </summary>
	KeyedCollection,

	/// <summary>
	///     An awaited keyed-collection (<c>Task&lt;IReadOnlyDictionary&lt;string, T&gt;&gt;</c>): the keyed analogue
	///     of <see cref="AwaitedEnumerable" />. Resolves every keyed registration of <c>T</c> behind the returned
	///     task with each async member awaited, so an async-tainted member is legal and the consumer stays
	///     synchronously constructible; the task still closes cycles. Suppression mirrors <see cref="AwaitedEnumerable" />,
	///     and AWT159/AWT160 apply as for <see cref="KeyedCollection" /> while it stays synthesized.
	/// </summary>
	AwaitedKeyedCollection,
}
