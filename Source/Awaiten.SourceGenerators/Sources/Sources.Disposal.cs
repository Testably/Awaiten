using System.Text;
using Awaiten.SourceGenerators.Entities;

namespace Awaiten.SourceGenerators;

internal static partial class Sources
{
	/// <summary>
	///     How a resolver tracks its instance for disposal. None: not disposable, nothing to track. Static: the
	///     declared type is known IDisposable, so the instance is cast and added directly. Runtime: a factory's
	///     declared return type can hide a concrete IDisposable behind a non-disposable service interface, so the
	///     static IsDisposable flag under-reports; the output is added behind a runtime <c>is IDisposable</c> check
	///     on the realized instance (retaining only genuinely-disposable outputs). Constructed and pre-built-Instance
	///     production never need Runtime: info.Symbol is the concrete type, and a pre-built Instance is never owned.
	/// </summary>
	private enum DisposalTracking
	{
		None,
		Static,
		Runtime,
	}

	/// <summary>
	///     Runtime and Static are mutually exclusive: RuntimeDisposalCheck is set by the model only when the static
	///     IsDisposable flag is false (the declared type does not reveal the disposable), so a factory output is
	///     either statically disposable or runtime-checked, never both.
	/// </summary>
	private static DisposalTracking DisposalOf(InstanceModel instance)
		=> (instance.RuntimeDisposalCheck, instance.NeedsDisposal) switch
		{
			(true, _) => DisposalTracking.Runtime,
			(_, true) => DisposalTracking.Static,
			_ => DisposalTracking.None,
		};

	// Whether any instance registers an OnRelease hook, so the base Scope needs the __releases queue, its drain
	// in Dispose/DisposeAsync, and the per-instance release registration in the resolvers.
	private static bool HasReleaseHooks(InstanceModel[] instances)
	{
		foreach (InstanceModel instance in instances)
		{
			if (instance.HasReleaseHook)
			{
				return true;
			}
		}

		return false;
	}

	// Emits the OnActivated hook call for a freshly built instance: OnActivated(variable);. The hook is a static
	// container method reached by simple name from the nested Root/Scope, exactly like a factory method. Emitted
	// only on the success path (a raced fresh resolve throws before reaching it), so an instance that was torn
	// down during a concurrent dispose is never activated. Nothing when the instance names no activation hook.
	private static void EmitActivation(StringBuilder builder, int depth, InstanceModel instance, string variable)
	{
		if (instance.OnActivated is not null)
		{
			Indent(builder, depth).Append(instance.OnActivated).Append('(').Append(variable).AppendLine(");");
		}
	}

	// Queues the OnRelease hook for a freshly built instance on the owner's __releases list:
	// (__s.__releases ??= new List<Action>()).Add(() => OnRelease(variable));. The queue is drained (reverse
	// creation order) ahead of __disposables when the owner is disposed. The caller registers it only where the
	// owner is known not disposed (a fresh resolver's !__raced branch), and only after OnActivated has run, so a
	// release is queued exactly when a fully-activated instance is retained. The variable is a resolver local
	// (`created`), captured by value, so a later teardown releases the instance it was queued for. Nothing when
	// the instance names no hook. The cached (singleton/scoped) path uses EmitCachedReleaseRegistration instead,
	// which captures the published field into a local for the same by-value guarantee under wiring rollback.
	private static void EmitReleaseRegistration(StringBuilder builder, int depth, InstanceModel instance, string variable)
	{
		if (instance.OnRelease is not null)
		{
			Indent(builder, depth).Append("(__s.__releases ??= new global::System.Collections.Generic.List<global::System.Action>()).Add(() => ")
				.Append(instance.OnRelease).Append('(').Append(variable).AppendLine("));");
		}
	}

	// Queues the OnRelease hook for a just-published cached (singleton/scoped) instance, capturing it into a local
	// first: (var __released = __s.field; __releases.Add(() => OnRelease(__released));). The by-value capture
	// mirrors the disposal registration (which adds __s.field by value) so a wiring-episode rollback - which nulls
	// the field of every instance the failed episode published, including peers that had already queued a release -
	// cannot orphan the closure onto a null-or-rebuilt field. Used on the cache-miss path where the field is
	// published before its release is queued (a deferred wiring episode, or a plain cache with no activation hook);
	// a non-deferred instance with an activation hook stores the field only after activating, so it queues the
	// release against that local via EmitReleaseRegistration instead. Nothing when the instance names no hook.
	private static void EmitCachedReleaseRegistration(StringBuilder builder, int depth, InstanceModel instance, string field, string type)
	{
		if (instance.OnRelease is not null)
		{
			Indent(builder, depth).Append(type).Append(" __released = __s.").Append(field).AppendLine(";");
			Indent(builder, depth).Append("(__s.__releases ??= new global::System.Collections.Generic.List<global::System.Action>()).Add(() => ")
				.Append(instance.OnRelease).AppendLine("(__released));");
		}
	}

	/// <summary>
	///     Emits the reverse-order synchronous drain of the (possibly null) <c>__toDispose</c> list captured
	///     under the lock - disposing what the owner created, newest first. When <paramref name="asyncDisposal" />
	///     is set, an instance that is <c>IAsyncDisposable</c> but not <c>IDisposable</c> cannot be torn down on
	///     this synchronous path, so it throws guidance to use <c>DisposeAsync</c> (matching
	///     Microsoft.Extensions.DependencyInjection rather than blocking on an async dispose).
	/// </summary>
	private static void EmitDrainDisposables(StringBuilder builder, int depth, bool asyncDisposal)
	{
		Indent(builder, depth).AppendLine("if (__toDispose is not null)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("for (int __index = __toDispose.Count - 1; __index >= 0; __index--)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("if (__toDispose[__index] is global::System.IDisposable __disposable)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("__disposable.Dispose();");
		Indent(builder, depth + 2).AppendLine("}");
		if (asyncDisposal)
		{
			Indent(builder, depth + 2).AppendLine("else if (__toDispose[__index] is global::System.IAsyncDisposable)");
			Indent(builder, depth + 2).AppendLine("{");
			Indent(builder, depth + 3).AppendLine("throw new global::System.InvalidOperationException(\"Awaiten: a resolved service requires asynchronous disposal (it implements IAsyncDisposable but not IDisposable); dispose this scope or container with DisposeAsync ('await using') instead of a synchronous Dispose().\");");
			Indent(builder, depth + 2).AppendLine("}");
		}

		Indent(builder, depth + 1).AppendLine("}");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits the reverse-order asynchronous drain of the captured <c>__toDispose</c> list: each instance is
	///     torn down newest-first, awaiting <c>IAsyncDisposable.DisposeAsync</c> when available and falling back
	///     to a synchronous <c>IDisposable.Dispose</c> otherwise.
	/// </summary>
	private static void EmitDrainDisposablesAsync(StringBuilder builder, int depth)
	{
		Indent(builder, depth).AppendLine("if (__toDispose is not null)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("for (int __index = __toDispose.Count - 1; __index >= 0; __index--)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("if (__toDispose[__index] is global::System.IAsyncDisposable __asyncDisposable)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("await __asyncDisposable.DisposeAsync().ConfigureAwait(false);");
		Indent(builder, depth + 2).AppendLine("}");
		Indent(builder, depth + 2).AppendLine("else if (__toDispose[__index] is global::System.IDisposable __disposable)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("__disposable.Dispose();");
		Indent(builder, depth + 2).AppendLine("}");
		Indent(builder, depth + 1).AppendLine("}");
		Indent(builder, depth).AppendLine("}");
	}

	// <paramref name="receiver" /> is the member-access prefix for the disposed flag: empty in an instance
	// context (the public async entries, which guard `this`), or "__s." inside a static resolver (which guards
	// the owner it was handed). The owner reference also names the type in the thrown exception.
	private static void EmitDisposedGuard(StringBuilder builder, int depth, string receiver = "")
	{
		Indent(builder, depth).Append("if (").Append(receiver).AppendLine("__disposed)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).Append("throw new global::System.ObjectDisposedException(").Append(receiver).AppendLine("GetType().FullName);");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits the disposal tracking for a freshly built <c>created</c> instance: under <c>lock (__gate)</c>,
	///     re-check <c>__disposed</c> so one built during a concurrent dispose is torn down here rather than
	///     leaked, otherwise add it to <c>__disposables</c>. A factory output whose declared return type may hide
	///     a concrete disposable is added behind a runtime test so only genuinely-disposable outputs are retained,
	///     and the whole lock is skipped when that test fails. When <paramref name="asyncDisposal" /> is set the
	///     tracked set and the raced-teardown also cover <c>IAsyncDisposable</c>; the teardown runs outside the
	///     lock (an <c>await</c> cannot occur inside one) and, in an <paramref name="asyncContext" /> (the async
	///     fresh resolver), awaits <c>DisposeAsync</c>. Shared by the synchronous and asynchronous fresh resolvers.
	///     Callers emit this before OnActivated (so a constructed disposable is torn down even if activation
	///     throws), and register the OnRelease hook separately after activation succeeds
	///     (<see cref="EmitFreshReleaseRegistration" />), so a failed activation queues no release.
	/// </summary>
	private static void EmitFreshDisposalTracking(StringBuilder builder, int depth, InstanceModel instance, bool asyncDisposal, bool asyncContext)
	{
		DisposalTracking disposal = DisposalOf(instance);

		// A runtime disposal check lets the whole lock be skipped for a realized output that turns out not to be
		// disposable (release, when present, takes its own lock after activation, so it no longer forces this one).
		bool wrapInRuntimeCheck = disposal == DisposalTracking.Runtime;
		if (wrapInRuntimeCheck)
		{
			string test = asyncDisposal
				? "created is global::System.IDisposable or global::System.IAsyncDisposable"
				: "created is global::System.IDisposable";
			Indent(builder, depth).Append("if (").Append(test).AppendLine(")");
			Indent(builder, depth).AppendLine("{");
			depth++;
		}

		// Record whether the scope was already disposed under the lock, then tear down the raced instance outside
		// it (so an async teardown can await, and user code never runs under the lock). The owner is the static
		// resolver's `__s` parameter (the scope for a scoped/transient resolver, the root for a singleton one). A
		// raced instance is torn down here and the resolver throws before reaching OnActivated, so it is never
		// activated (and so never released).
		Indent(builder, depth).AppendLine("bool __raced;");
		Indent(builder, depth).AppendLine("lock (__s.__gate)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("__raced = __s.__disposed;");
		Indent(builder, depth + 1).AppendLine("if (!__raced)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("(__s.__disposables ??= new global::System.Collections.Generic.List<object>()).Add(created);");
		Indent(builder, depth + 1).AppendLine("}");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth).AppendLine("if (__raced)");
		Indent(builder, depth).AppendLine("{");
		EmitRacedTeardown(builder, depth + 1, asyncDisposal, asyncContext);
		Indent(builder, depth + 1).AppendLine("throw new global::System.ObjectDisposedException(__s.GetType().FullName);");
		Indent(builder, depth).AppendLine("}");

		if (wrapInRuntimeCheck)
		{
			depth--;
			Indent(builder, depth).AppendLine("}");
		}
	}

	// Queues the OnRelease hook for a freshly built fresh-resolver instance after OnActivated has run, under a
	// raced-safe lock: the instance is captured by value (`created`), and the release is added only when the owner
	// is not disposed. When the owner was disposed since construction - a rare resolve-during-dispose race - the
	// release is skipped (a disposable instance was already registered before activation, so that concurrent
	// dispose tears it down; a non-disposable one simply is not released). Nothing when the instance names no hook.
	private static void EmitFreshReleaseRegistration(StringBuilder builder, int depth, InstanceModel instance)
	{
		if (instance.OnRelease is null)
		{
			return;
		}

		Indent(builder, depth).AppendLine("lock (__s.__gate)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("if (!__s.__disposed)");
		Indent(builder, depth + 1).AppendLine("{");
		EmitReleaseRegistration(builder, depth + 2, instance, "created");
		Indent(builder, depth + 1).AppendLine("}");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Tears down a single freshly built <paramref name="instance" /> (the <c>created</c> local of a fresh
	///     resolver, or the <c>__s.&lt;field&gt;</c> of a caching resolver) built during a concurrent dispose or
	///     abandoned on a failed wiring/initialization. Without async disposal it is a synchronous <c>Dispose</c>.
	///     With async disposal in an async resolver it awaits <c>DisposeAsync</c> (preferring it), falling back to
	///     <c>Dispose</c>; in a synchronous resolver it disposes a synchronous <c>IDisposable</c> and leaves an
	///     async-only instance to its (rare) race - a synchronous path cannot await, matching the synchronous
	///     Dispose contract. The runtime checks go through <c>(object)instance</c> so a sealed concrete type that
	///     implements only one of the disposal interfaces still compiles (a direct <c>is</c> against such a type
	///     would be a CS8121 error).
	/// </summary>
	private static void EmitRacedTeardown(StringBuilder builder, int depth, bool asyncDisposal, bool asyncContext, string instance = "created")
	{
		if (asyncDisposal && asyncContext)
		{
			Indent(builder, depth).Append("if ((object)").Append(instance).AppendLine(" is global::System.IAsyncDisposable __racedAsync)");
			Indent(builder, depth).AppendLine("{");
			Indent(builder, depth + 1).AppendLine("await __racedAsync.DisposeAsync().ConfigureAwait(false);");
			Indent(builder, depth).AppendLine("}");
			Indent(builder, depth).Append("else if ((object)").Append(instance).AppendLine(" is global::System.IDisposable __racedSync)");
			Indent(builder, depth).AppendLine("{");
			Indent(builder, depth + 1).AppendLine("__racedSync.Dispose();");
			Indent(builder, depth).AppendLine("}");
			return;
		}

		Indent(builder, depth).Append("if ((object)").Append(instance).AppendLine(" is global::System.IDisposable __racedSync)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("__racedSync.Dispose();");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits the failure-path teardown of a partially-built <paramref name="instance" />: a
	///     <see cref="EmitRacedTeardown" /> wrapped in its own <c>try</c>/<c>catch</c> that swallows a throw from
	///     the instance's own <c>Dispose</c>/<c>DisposeAsync</c>, so the disposal failure cannot mask the original
	///     wiring/initialization failure the caller is about to see (the caller rethrows after this). Shared by the
	///     async and synchronous guarded-wiring resolvers.
	/// </summary>
	private static void EmitGuardedTeardown(StringBuilder builder, int depth, bool asyncDisposal, bool asyncContext, string instance = "created")
	{
		Indent(builder, depth).AppendLine("try");
		Indent(builder, depth).AppendLine("{");
		EmitRacedTeardown(builder, depth + 1, asyncDisposal, asyncContext, instance);
		Indent(builder, depth).AppendLine("}");
		Indent(builder, depth).AppendLine("catch");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("// Swallowed: a throw from the instance's own disposal must not mask the original failure below.");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Registers a freshly built disposable instance for teardown on the owner under <c>__gate</c>,
	///     re-checking <c>__disposed</c> so one built during a concurrent dispose is disposed here rather than
	///     leaked. Mirrors the synchronous fresh-resolver registration, but runs in an async context so its
	///     raced-teardown can await <c>DisposeAsync</c>.
	/// </summary>
	private static void EmitAsyncDisposableRegistration(StringBuilder builder, int depth, InstanceModel instance, bool asyncDisposal)
	{
		// Only disposal is registered here (before activation); the release hook is queued after activation runs,
		// via EmitFreshReleaseRegistration, exactly as on the synchronous fresh path.
		if (DisposalOf(instance) == DisposalTracking.None)
		{
			return;
		}

		EmitFreshDisposalTracking(builder, depth, instance, asyncDisposal, asyncContext: true);
	}

	private static void EmitDispose(StringBuilder builder, int depth, bool asyncDisposal, bool hasReleases)
	{
		AppendXmlSummary(builder, depth,
			"Disposes every tracked instance in reverse creation order.");
		Indent(builder, depth).AppendLine("public void Dispose()");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("global::System.Collections.Generic.List<object>? __toDispose;");
		if (hasReleases)
		{
			Indent(builder, depth + 1).AppendLine("global::System.Collections.Generic.List<global::System.Action>? __toRelease;");
		}

		Indent(builder, depth + 1).AppendLine("lock (__gate)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("if (__disposed)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("return;");
		Indent(builder, depth + 2).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 2).AppendLine("__disposed = true;");
		// Hand off the lists rather than copying them: nulling the fields releases ownership without allocating.
		Indent(builder, depth + 2).AppendLine("__toDispose = __disposables;");
		Indent(builder, depth + 2).AppendLine("__disposables = null;");
		if (hasReleases)
		{
			Indent(builder, depth + 2).AppendLine("__toRelease = __releases;");
			Indent(builder, depth + 2).AppendLine("__releases = null;");
		}

		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		// Outside the lock so user code does not run under it: run the release hooks (reverse creation order)
		// first, so each instance is released while its dependencies are still alive, then dispose.
		if (hasReleases)
		{
			EmitDrainReleases(builder, depth + 1);
		}

		EmitDrainDisposables(builder, depth + 1, asyncDisposal);
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits the reverse-order drain of the (possibly null) captured <c>__toRelease</c> list: each queued
	///     <c>OnRelease</c> action runs newest-first, before the instances are disposed. Release actions are
	///     synchronous, so the synchronous and asynchronous disposal paths drain them identically.
	/// </summary>
	private static void EmitDrainReleases(StringBuilder builder, int depth)
	{
		Indent(builder, depth).AppendLine("if (__toRelease is not null)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("for (int __index = __toRelease.Count - 1; __index >= 0; __index--)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("__toRelease[__index]();");
		Indent(builder, depth + 1).AppendLine("}");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits <c>DisposeAsync</c> (the <c>IAsyncDisposable</c> member): the asynchronous counterpart of
	///     <see cref="EmitDispose" />. It captures and clears the disposables list under the lock exactly as the
	///     synchronous path does (so a single drain happens once), then awaits each instance's teardown outside
	///     the lock, preferring <c>DisposeAsync</c> over <c>Dispose</c>. Emitted only when the compilation can see
	///     <c>IAsyncDisposable</c>; the base <c>Scope</c> defines it and the <c>Root</c> inherits it.
	/// </summary>
	private static void EmitDisposeAsync(StringBuilder builder, int depth, bool hasReleases)
	{
		AppendXmlSummary(builder, depth,
			"Asynchronously disposes every tracked instance in reverse creation order.");
		Indent(builder, depth).AppendLine("public async global::System.Threading.Tasks.ValueTask DisposeAsync()");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("global::System.Collections.Generic.List<object>? __toDispose;");
		if (hasReleases)
		{
			Indent(builder, depth + 1).AppendLine("global::System.Collections.Generic.List<global::System.Action>? __toRelease;");
		}

		Indent(builder, depth + 1).AppendLine("lock (__gate)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("if (__disposed)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("return;");
		Indent(builder, depth + 2).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 2).AppendLine("__disposed = true;");
		Indent(builder, depth + 2).AppendLine("__toDispose = __disposables;");
		Indent(builder, depth + 2).AppendLine("__disposables = null;");
		if (hasReleases)
		{
			Indent(builder, depth + 2).AppendLine("__toRelease = __releases;");
			Indent(builder, depth + 2).AppendLine("__releases = null;");
		}

		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		// Outside the lock so an awaited DisposeAsync (user code) never runs under it: run the synchronous
		// release hooks (reverse creation order) first, then tear down.
		if (hasReleases)
		{
			EmitDrainReleases(builder, depth + 1);
		}

		EmitDrainDisposablesAsync(builder, depth + 1);
		Indent(builder, depth).AppendLine("}");
	}
}
