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
///     resolver. Like the synchronous relationship types they defer resolution, so they contribute no graph
///     edge and launder async taint - which is what lets a synchronously-resolvable consumer hold one over an
///     async-initialized service without becoming async-tainted (and without tripping AWT119/AWT120).
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
}