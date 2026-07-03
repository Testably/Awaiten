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

	private static void EmitDisposedGuard(StringBuilder builder, int depth)
	{
		Indent(builder, depth).AppendLine("if (__disposed)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("throw new global::System.ObjectDisposedException(GetType().FullName);");
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits the disposal tracking for a freshly built <c>created</c> instance: under <c>lock (__gate)</c>,
	///     re-check <c>__disposed</c> so one built during a concurrent dispose is torn down here rather than
	///     leaked, otherwise add it to <c>__disposables</c>. When <paramref name="runtimeCheck" /> is set (a
	///     factory output, whose declared return type may hide a concrete disposable), the whole block is gated on
	///     a runtime test so only genuinely-disposable outputs are retained; otherwise the static flag already
	///     guarantees disposability. When <paramref name="asyncDisposal" /> is set the tracked set and the
	///     raced-teardown also cover <c>IAsyncDisposable</c>; the teardown runs outside the lock (an <c>await</c>
	///     cannot occur inside one) and, in an <paramref name="asyncContext" /> (the async fresh resolver), awaits
	///     <c>DisposeAsync</c>. Shared by the synchronous and asynchronous fresh resolvers.
	/// </summary>
	private static void EmitFreshDisposalTracking(StringBuilder builder, int depth, bool runtimeCheck, bool asyncDisposal, bool asyncContext)
	{
		if (runtimeCheck)
		{
			string test = asyncDisposal
				? "created is global::System.IDisposable or global::System.IAsyncDisposable"
				: "created is global::System.IDisposable";
			Indent(builder, depth).Append("if (").Append(test).AppendLine(")");
			Indent(builder, depth).AppendLine("{");
			depth++;
		}

		// Record whether the scope was already disposed under the lock, then tear down the raced instance outside
		// it (so an async teardown can await, and user code never runs under the lock).
		Indent(builder, depth).AppendLine("bool __raced;");
		Indent(builder, depth).AppendLine("lock (__gate)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("__raced = __disposed;");
		Indent(builder, depth + 1).AppendLine("if (!__raced)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("(__disposables ??= new global::System.Collections.Generic.List<object>()).Add(created);");
		Indent(builder, depth + 1).AppendLine("}");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth).AppendLine("if (__raced)");
		Indent(builder, depth).AppendLine("{");
		EmitRacedTeardown(builder, depth + 1, asyncDisposal, asyncContext);
		Indent(builder, depth + 1).AppendLine("throw new global::System.ObjectDisposedException(GetType().FullName);");
		Indent(builder, depth).AppendLine("}");

		if (runtimeCheck)
		{
			depth--;
			Indent(builder, depth).AppendLine("}");
		}
	}

	/// <summary>
	///     Tears down a single <c>created</c> instance built during a concurrent dispose. Without async disposal
	///     it is a synchronous <c>Dispose</c>. With async disposal in an async resolver it awaits
	///     <c>DisposeAsync</c> (preferring it), falling back to <c>Dispose</c>; in a synchronous resolver it
	///     disposes a synchronous <c>IDisposable</c> and leaves an async-only instance to its (rare) race - a
	///     synchronous path cannot await, matching the synchronous Dispose contract. The runtime checks go through
	///     <c>(object)created</c> so a sealed concrete type that implements only one of the disposal interfaces
	///     still compiles (a direct <c>is</c> against such a type would be a CS8121 error).
	/// </summary>
	private static void EmitRacedTeardown(StringBuilder builder, int depth, bool asyncDisposal, bool asyncContext)
	{
		if (asyncDisposal && asyncContext)
		{
			Indent(builder, depth).AppendLine("if ((object)created is global::System.IAsyncDisposable __racedAsync)");
			Indent(builder, depth).AppendLine("{");
			Indent(builder, depth + 1).AppendLine("await __racedAsync.DisposeAsync().ConfigureAwait(false);");
			Indent(builder, depth).AppendLine("}");
			Indent(builder, depth).AppendLine("else if ((object)created is global::System.IDisposable __racedSync)");
			Indent(builder, depth).AppendLine("{");
			Indent(builder, depth + 1).AppendLine("__racedSync.Dispose();");
			Indent(builder, depth).AppendLine("}");
			return;
		}

		Indent(builder, depth).AppendLine("if ((object)created is global::System.IDisposable __racedSync)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("__racedSync.Dispose();");
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
		DisposalTracking disposal = DisposalOf(instance);
		if (disposal == DisposalTracking.None)
		{
			return;
		}

		EmitFreshDisposalTracking(builder, depth, disposal == DisposalTracking.Runtime, asyncDisposal, asyncContext: true);
	}

	private static void EmitDispose(StringBuilder builder, int depth, bool asyncDisposal)
	{
		AppendXmlSummary(builder, depth,
			"Disposes every tracked instance in reverse creation order.");
		Indent(builder, depth).AppendLine("public void Dispose()");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("global::System.Collections.Generic.List<object>? __toDispose;");
		Indent(builder, depth + 1).AppendLine("lock (__gate)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("if (__disposed)");
		Indent(builder, depth + 2).AppendLine("{");
		Indent(builder, depth + 3).AppendLine("return;");
		Indent(builder, depth + 2).AppendLine("}");
		builder.AppendLine();
		Indent(builder, depth + 2).AppendLine("__disposed = true;");
		// Hand off the list rather than copying it: nulling the field releases ownership without allocating.
		Indent(builder, depth + 2).AppendLine("__toDispose = __disposables;");
		Indent(builder, depth + 2).AppendLine("__disposables = null;");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		// Dispose outside the lock so user code does not run under the lock.
		EmitDrainDisposables(builder, depth + 1, asyncDisposal);
		Indent(builder, depth).AppendLine("}");
	}

	/// <summary>
	///     Emits <c>DisposeAsync</c> (the <c>IAsyncDisposable</c> member): the asynchronous counterpart of
	///     <see cref="EmitDispose" />. It captures and clears the disposables list under the lock exactly as the
	///     synchronous path does (so a single drain happens once), then awaits each instance's teardown outside
	///     the lock, preferring <c>DisposeAsync</c> over <c>Dispose</c>. Emitted only when the compilation can see
	///     <c>IAsyncDisposable</c>; the base <c>Scope</c> defines it and the <c>Root</c> inherits it.
	/// </summary>
	private static void EmitDisposeAsync(StringBuilder builder, int depth)
	{
		AppendXmlSummary(builder, depth,
			"Asynchronously disposes every tracked instance in reverse creation order.");
		Indent(builder, depth).AppendLine("public async global::System.Threading.Tasks.ValueTask DisposeAsync()");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("global::System.Collections.Generic.List<object>? __toDispose;");
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
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		// Tear down outside the lock so an awaited DisposeAsync (user code) never runs under it.
		EmitDrainDisposablesAsync(builder, depth + 1);
		Indent(builder, depth).AppendLine("}");
	}
}
