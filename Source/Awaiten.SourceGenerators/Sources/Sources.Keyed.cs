using System.Text;
using Awaiten.SourceGenerators.Entities;

namespace Awaiten.SourceGenerators;

internal static partial class Sources
{
	/// <summary>
	///     Emits the keyed resolution surface on the base <c>Scope</c> (inherited by the <c>Root</c>): the
	///     <c>Resolve(Type, object?)</c> / <c>TryResolve(Type, object?, out object?)</c> / <c>ResolveAsync(Type,
	///     object?, CancellationToken)</c> overloads. A <see langword="null" /> key delegates to the unkeyed
	///     overload, so a keyed call is a strict superset. A non-null key dispatches through a static
	///     <c>__keyed</c> table keyed by <c>(Type, key)</c>, populated only from user-declared <c>[Key]</c>s
	///     (<see cref="AwaitenGenerator.IsUserKey" />): the synthetic decorator/contextual keys stay internal and
	///     unreachable. Async-taint and strict lifetime-safety withholding follow the same rules as the unkeyed
	///     dispatch, with the same guidance messages. Returns whether the <c>__keyed</c> table was emitted, so the
	///     resolvability probe knows whether it may look there.
	/// </summary>
	private static bool EmitKeyedResolutionApi(ApiRegions regions, int depth, EmitContext context, bool strict, bool syncResolveAfterInit, bool asObjectEmitted)
	{
		(StringBuilder members, StringBuilder fields, StringBuilder helpers) = regions;
		List<KeyedDispatchEntry> entries = BuildKeyedEntries(context, strict, syncResolveAfterInit);
		bool hasEntries = entries.Count > 0;
		bool keyedAsync = entries.Any(entry => entry.Async is not null);

		StringBuilder builder = members;
		Separate(members);

		// Resolve(Type, object?): the keyed sibling of Resolve(Type). A miss throws the same targeted guidance
		// (async-taint / withholding) as the synchronous path, or a "no registration" message naming the key.
		AppendXmlSummary(builder, depth,
			"Resolves the service registered for <paramref name=\"serviceType\" /> under <paramref name=\"key\" />, throwing when it is not resolvable.");
		Indent(builder, depth).AppendLine("public object Resolve(global::System.Type serviceType, object? key)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("if (key is null)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return Resolve(serviceType);");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		// TryResolve carries the disposed-scope guard, so a keyed Resolve rejects a disposed scope exactly like the
		// unkeyed Resolve(Type), whether or not any keyed registration exists.
		Indent(builder, depth + 1).AppendLine("if (TryResolve(serviceType, key, out object? instance))");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return instance!;");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		if (hasEntries)
		{
			// A registered keyed service that could not be resolved synchronously (async-tainted, or a disposable
			// build-on-demand service withheld on the Root) carries targeted guidance instead of "no registration".
			Indent(builder, depth + 1).AppendLine("if (__keyed.TryGetValue(new __KeyedKey(serviceType, key), out __KeyedEntry __entry) && __entry.Guidance is not null)");
			Indent(builder, depth + 1).AppendLine("{");
			Indent(builder, depth + 2).AppendLine("throw new global::System.InvalidOperationException(__entry.Guidance);");
			Indent(builder, depth + 1).AppendLine("}");
			builder.AppendLine();
		}

		Indent(builder, depth + 1).AppendLine(
			"throw new global::System.InvalidOperationException($\"No registration for type '{serviceType}' with key '{key}' on this container.\");");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();

		// TryResolve(Type, object?, out object?): the keyed sibling of TryResolve(Type, out object?). A hit invokes
		// the slot's synchronous resolver over this scope; an async-only slot, a root-withheld one on the Root, or a
		// miss returns false.
		AppendXmlSummary(builder, depth,
			"Attempts to resolve <paramref name=\"serviceType\" /> under <paramref name=\"key\" />, returning <see langword=\"false\" /> when it is not resolvable.");
		Indent(builder, depth).AppendLine("public bool TryResolve(global::System.Type serviceType, object? key, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out object? instance)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("if (key is null)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return TryResolve(serviceType, out instance);");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		// A disposed scope rejects every keyed by-type resolution, whether or not any keyed registration exists,
		// mirroring the unkeyed TryResolve(Type)'s unconditional guard.
		EmitDisposedGuard(builder, depth + 1);
		if (hasEntries)
		{
			Indent(builder, depth + 1).AppendLine("if (__keyed.TryGetValue(new __KeyedKey(serviceType, key), out __KeyedEntry __entry)");
			Indent(builder, depth + 1).AppendLine("    && __entry.Sync is not null");
			// A root-withheld disposable resolves from a child scope (its lifetime bounded there) but not from the
			// Root, mirroring the unkeyed dispatch's RootWithheld slot flag.
			Indent(builder, depth + 1).AppendLine("    && !(__entry.RootWithheld && this is Root))");
			Indent(builder, depth + 1).AppendLine("{");
			Indent(builder, depth + 2).AppendLine("instance = __entry.Sync(this);");
			Indent(builder, depth + 2).AppendLine("return true;");
			Indent(builder, depth + 1).AppendLine("}");
			builder.AppendLine();
		}

		Indent(builder, depth + 1).AppendLine("instance = null;");
		Indent(builder, depth + 1).AppendLine("return false;");
		Indent(builder, depth).AppendLine("}");
		builder.AppendLine();

		// ResolveAsync(Type, object?, CancellationToken): an async-tainted keyed slot routes to its memoizing async
		// resolver; everything else completes immediately, deferring to Resolve for the same guidance as the
		// synchronous path.
		AppendXmlSummary(builder, depth,
			"Asynchronously resolves the service registered for <paramref name=\"serviceType\" /> under <paramref name=\"key\" />, awaiting any async initialization.");
		Indent(builder, depth).AppendLine("public global::System.Threading.Tasks.Task<object> ResolveAsync(global::System.Type serviceType, object? key, global::System.Threading.CancellationToken cancellationToken = default)");
		Indent(builder, depth).AppendLine("{");
		Indent(builder, depth + 1).AppendLine("if (key is null)");
		Indent(builder, depth + 1).AppendLine("{");
		Indent(builder, depth + 2).AppendLine("return ResolveAsync(serviceType, cancellationToken);");
		Indent(builder, depth + 1).AppendLine("}");
		builder.AppendLine();
		if (keyedAsync)
		{
			EmitDisposedGuard(builder, depth + 1);
			Indent(builder, depth + 1).AppendLine("if (__keyed.TryGetValue(new __KeyedKey(serviceType, key), out __KeyedEntry __entry) && __entry.Async is not null)");
			Indent(builder, depth + 1).AppendLine("{");
			Indent(builder, depth + 2).AppendLine("return __entry.Async(this, cancellationToken);");
			Indent(builder, depth + 1).AppendLine("}");
			builder.AppendLine();
		}

		Indent(builder, depth + 1).AppendLine("return global::System.Threading.Tasks.Task.FromResult(Resolve(serviceType, key));");
		Indent(builder, depth).AppendLine("}");

		if (!hasEntries)
		{
			return false;
		}

		EmitKeyedTable(fields, helpers, depth, entries);
		// The keyed async delegates convert their Task<T> to Task<object> through __AsObject, the same helper the
		// unkeyed async dispatch uses; emit it here only when that dispatch did not already.
		if (keyedAsync && !asObjectEmitted)
		{
			EmitAsObjectHelper(helpers, depth);
		}

		return true;
	}

	/// <summary>
	///     The keyed dispatch cases: each user-declared <c>(service type, [Key])</c> mapped to the winning
	///     registration's resolver, matching what <c>[FromKey]</c> injection resolves. A parameterized service or a
	///     requesting-type factory has no bare (key-only) resolution (it is reached only through a <c>Func&lt;…&gt;</c>
	///     relationship) and is skipped. Each entry carries its synchronous resolver (null when async-only in the
	///     strict default), its async resolver (null when synchronous), whether it is root-withheld under strict
	///     lifetime safety, and the guidance a synchronous miss throws.
	/// </summary>
	private static List<KeyedDispatchEntry> BuildKeyedEntries(EmitContext context, bool strict, bool syncResolveAfterInit)
	{
		List<KeyedDispatchEntry> entries = new();
		HashSet<ServiceKey> seen = new();

		foreach (InstanceModel owner in context.Instances)
		{
			foreach (ServiceKey serviceKey in owner.Services.AsArray())
			{
				// Only user-declared [Key]s are reachable; the synthetic decorator/contextual keys stay internal.
				if (!AwaitenGenerator.IsUserKey(serviceKey.Key) || !seen.Add(serviceKey))
				{
					continue;
				}

				if (TryBuildKeyedEntry(serviceKey, context, strict, syncResolveAfterInit) is { } entry)
				{
					entries.Add(entry);
				}
			}
		}

		return entries;
	}

	/// <summary>
	///     The keyed dispatch case for one user-declared <c>(service, [Key])</c>, or <see langword="null" /> when the
	///     winning registration has no bare (key-only) resolution (a parameterized service or a requesting-type
	///     factory, reachable only through its <c>Func&lt;…&gt;</c> relationship).
	/// </summary>
	private static KeyedDispatchEntry? TryBuildKeyedEntry(ServiceKey serviceKey, EmitContext context, bool strict, bool syncResolveAfterInit)
	{
		// The authoritative winner of a (service, key) slot, so imperative keyed resolution agrees with
		// [FromKey] injection (which resolves through the same map).
		int index = context.ServiceToIndex[serviceKey];
		InstanceModel instance = context.Instances[index];

		// A parameterized service or a requesting-type factory cannot be built from a bare (type, key) request
		// (it needs its runtime arguments / the requesting type), so it is offered only through its relationship.
		if (instance.IsParameterized || instance.IsRequestingTypeFactory)
		{
			return null;
		}

		string service = serviceKey.Service;
		string resolver = context.Names.Resolver(index);
		bool rootOwned = IsRootOwned(instance);
		bool emitsSync = EmitsSync(instance, syncResolveAfterInit);
		bool withheld = IsWithheld(instance, strict);

		string? sync = KeyedSyncCall(emitsSync, rootOwned, resolver);
		string? asyncArm = KeyedAsyncArm(instance, context.Names, index, rootOwned, withheld, service);
		string? guidance = KeyedGuidance(emitsSync, withheld, service);

		return new KeyedDispatchEntry(service, AwaitenGenerator.KeyLiteral(serviceKey.Key!), sync, asyncArm, emitsSync && withheld, guidance);
	}

	/// <summary>
	///     The synchronous resolver call over the resolving scope <c>__s</c>: a singleton through the Root over the
	///     shared root, a scoped/transient over this scope. Null for an async-tainted service in the strict default
	///     (no synchronous path).
	/// </summary>
	private static string? KeyedSyncCall(bool emitsSync, bool rootOwned, string resolver)
	{
		if (!emitsSync)
		{
			return null;
		}

		return rootOwned ? $"Root.{resolver}(__s.__root)" : $"{resolver}(__s)";
	}

	/// <summary>
	///     The async arm of an async-tainted keyed service (null otherwise): its memoizing async resolver adapted to
	///     <c>Task&lt;object&gt;</c>. A disposable async transient is withheld from by-type resolution on the Root (an
	///     unbounded leak), mirroring the unkeyed async arm; a child scope still resolves it.
	/// </summary>
	private static string? KeyedAsyncArm(InstanceModel instance, Names names, int index, bool rootOwned, bool withheld, string service)
	{
		if (!instance.IsAsyncTainted)
		{
			return null;
		}

		string asyncResolver = names.AsyncResolver(index);
		string call = rootOwned ? $"Root.{asyncResolver}(__s.__root, __ct)" : $"{asyncResolver}(__s, __ct)";
		return withheld
			? $"__s is Root ? throw new global::System.InvalidOperationException({AsyncRootWithheldMessage(service)}) : __AsObject({call})"
			: $"__AsObject({call})";
	}

	/// <summary>
	///     The guidance a synchronous Resolve throws on a miss: async-taint takes precedence (no sync path on any
	///     scope), otherwise a disposable build-on-demand service withheld on the Root, otherwise none.
	/// </summary>
	private static string? KeyedGuidance(bool emitsSync, bool withheld, string service)
	{
		if (!emitsSync)
		{
			return AsyncWithheldMessage(service);
		}

		return withheld ? BareWithheldMessage(service) : null;
	}

	/// <summary>
	///     Emits the static <c>__keyed</c> dispatch table and its <c>__KeyedKey</c> / <c>__KeyedEntry</c> slot types.
	///     The table lives on the base <c>Scope</c>, shared by every scope and the <c>Root</c>; each entry binds its
	///     synchronous resolver as a <c>Func&lt;Scope, object&gt;</c> and (for an async-tainted service) its async
	///     resolver as a <c>Func&lt;Scope, CancellationToken, Task&lt;object&gt;&gt;</c> over the resolving scope.
	/// </summary>
	private static void EmitKeyedTable(StringBuilder fields, StringBuilder helpers, int depth, List<KeyedDispatchEntry> entries)
	{
		const string syncFunc = "global::System.Func<Scope, object>";
		const string asyncFunc = "global::System.Func<Scope, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<object>>";

		Separate(fields);
		AppendXmlSummary(fields, depth, "The keyed by-type dispatch table: each user-declared <c>[Key]</c> mapped to its resolver.");
		Indent(fields, depth).AppendLine(
			"private static readonly global::System.Collections.Generic.Dictionary<__KeyedKey, __KeyedEntry> __keyed = new global::System.Collections.Generic.Dictionary<__KeyedKey, __KeyedEntry>");
		Indent(fields, depth).AppendLine("{");
		foreach (KeyedDispatchEntry entry in entries)
		{
			string sync = entry.Sync is null ? "null" : $"static __s => {entry.Sync}";
			string asyncArm = entry.Async is null ? "null" : $"static (__s, __ct) => {entry.Async}";
			string guidance = entry.Guidance ?? "null";
			Indent(fields, depth + 1).Append("{ new __KeyedKey(typeof(").Append(entry.Type).Append("), ").Append(entry.KeyLiteral)
				.Append("), new __KeyedEntry(").Append(sync).Append(", ").Append(asyncArm).Append(", ")
				.Append(entry.RootWithheld ? "true" : "false").Append(", ").Append(guidance).AppendLine(") },");
		}

		Indent(fields, depth).AppendLine("};");

		Separate(helpers);
		AppendXmlSummary(helpers, depth, "The composite key of the keyed dispatch table: a service type paired with a user-declared <c>[Key]</c>.");
		Indent(helpers, depth).AppendLine("private readonly struct __KeyedKey : global::System.IEquatable<__KeyedKey>");
		Indent(helpers, depth).AppendLine("{");
		Indent(helpers, depth + 1).AppendLine("public readonly global::System.Type Type;");
		Indent(helpers, depth + 1).AppendLine("public readonly object Key;");
		Indent(helpers, depth + 1).AppendLine("public __KeyedKey(global::System.Type type, object key)");
		Indent(helpers, depth + 1).AppendLine("{");
		Indent(helpers, depth + 2).AppendLine("Type = type;");
		Indent(helpers, depth + 2).AppendLine("Key = key;");
		Indent(helpers, depth + 1).AppendLine("}");
		Indent(helpers, depth + 1).AppendLine("public bool Equals(__KeyedKey other) => (object)Type == (object)other.Type && global::System.Object.Equals(Key, other.Key);");
		Indent(helpers, depth + 1).AppendLine("public override bool Equals(object? obj) => obj is __KeyedKey other && Equals(other);");
		Indent(helpers, depth + 1).AppendLine("public override int GetHashCode() => unchecked((Type.GetHashCode() * 397) ^ (Key?.GetHashCode() ?? 0));");
		Indent(helpers, depth).AppendLine("}");
		helpers.AppendLine();

		AppendXmlSummary(helpers, depth, "One slot of the keyed dispatch table: the synchronous and async resolvers, the root-withheld flag, and the synchronous-miss guidance.");
		Indent(helpers, depth).AppendLine("private readonly struct __KeyedEntry");
		Indent(helpers, depth).AppendLine("{");
		Indent(helpers, depth + 1).Append("public readonly ").Append(syncFunc).AppendLine("? Sync;");
		Indent(helpers, depth + 1).Append("public readonly ").Append(asyncFunc).AppendLine("? Async;");
		Indent(helpers, depth + 1).AppendLine("public readonly bool RootWithheld;");
		Indent(helpers, depth + 1).AppendLine("public readonly string? Guidance;");
		Indent(helpers, depth + 1).Append("public __KeyedEntry(").Append(syncFunc).Append("? sync, ").Append(asyncFunc).AppendLine("? async, bool rootWithheld, string? guidance)");
		Indent(helpers, depth + 1).AppendLine("{");
		Indent(helpers, depth + 2).AppendLine("Sync = sync;");
		Indent(helpers, depth + 2).AppendLine("Async = async;");
		Indent(helpers, depth + 2).AppendLine("RootWithheld = rootWithheld;");
		Indent(helpers, depth + 2).AppendLine("Guidance = guidance;");
		Indent(helpers, depth + 1).AppendLine("}");
		Indent(helpers, depth).AppendLine("}");
	}

	/// <summary>
	///     A single keyed dispatch case: the service <see cref="Type" /> and its user-declared <see cref="KeyLiteral" />,
	///     the <see cref="Sync" /> resolver expression over <c>__s</c> (null when async-only), the <see cref="Async" />
	///     resolver expression over <c>__s</c>/<c>__ct</c> producing a <c>Task&lt;object&gt;</c> (null when synchronous),
	///     whether it is <see cref="RootWithheld" /> from synchronous by-type resolution on the Root, and the
	///     <see cref="Guidance" /> a synchronous miss throws (null when it resolves synchronously).
	/// </summary>
	private readonly struct KeyedDispatchEntry(string type, string keyLiteral, string? sync, string? asyncArm, bool rootWithheld, string? guidance)
	{
		public string Type { get; } = type;

		public string KeyLiteral { get; } = keyLiteral;

		public string? Sync { get; } = sync;

		public string? Async { get; } = asyncArm;

		public bool RootWithheld { get; } = rootWithheld;

		public string? Guidance { get; } = guidance;
	}
}
