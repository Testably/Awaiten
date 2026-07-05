using System.Text;
using Awaiten.SourceGenerators.Entities;

namespace Awaiten.SourceGenerators;

internal static partial class Sources
{
	/// <summary>
	///     A disposable build-on-demand service (a disposable transient or parameterized service) is withheld from
	///     by-type resolution on the container Root under strict lifetime safety: off the Root its bare type and
	///     plain Func factory throw a guidance exception, and it gets no typed resolver, so the leak-prone ways to
	///     reach it from the Root are constructor injection and <c>Owned&lt;T&gt;</c> / <c>Func&lt;…, Owned&lt;T&gt;&gt;</c>.
	///     It stays resolvable from a child scope, where its lifetime is bounded by the scope (the Root mask, not
	///     the table, gates it).
	/// </summary>
	private static bool IsWithheld(InstanceModel instance, bool strict)
		=> strict && instance.NeedsDisposal && (instance.Lifetime == Lifetime.Transient || instance.IsParameterized);

	/// <summary>
	///     Whether a plain <c>Func&lt;…&gt;</c> over this service is withheld from by-type resolution on the Root
	///     under strict lifetime safety: the service is built on demand (transient or parameterized) and building
	///     it tracks a fresh disposable on its owner (the service itself is disposable, or its construction
	///     transitively rebuilds one). Such a Func re-invoked off a root binding accumulates those disposables for
	///     the container's lifetime, so off the Root only the <c>Func&lt;…, Owned&lt;T&gt;&gt;</c> form (which drains
	///     into a throwaway scope) is offered; the plain Func stays resolvable from a child scope, which bounds the
	///     disposables it builds.
	/// </summary>
	private static bool IsFuncWithheld(InstanceModel[] instances, int index, Dictionary<ServiceKey, int> serviceToIndex, CollectionMembership membership, bool strict)
		=> strict
		   && (instances[index].Lifetime == Lifetime.Transient || instances[index].IsParameterized)
		   && AwaitenGenerator.BuildsFreshDisposable(instances, serviceToIndex, membership, index);

	/// <summary>
	///     The guidance message (a quoted string literal) thrown by Resolve(Type) on the Root when a service
	///     withheld there under strict lifetime safety is requested by its bare type: it is itself a disposable
	///     build-on-demand service, reachable from the Root only by injection or through an <c>Owned&lt;T&gt;</c>
	///     handle, but still resolvable from a child scope, which bounds its lifetime.
	/// </summary>
	private static string BareWithheldMessage(string service)
	{
		string display = service.Replace("global::", string.Empty);
		return $"\"Awaiten: '{display}' is withheld from by-type resolution on the container root under strict lifetime safety; obtain it through Owned<{display}> or Func<…, Owned<{display}>>, resolve it from a child scope, inject it directly, or set LifetimeSafety.Loose on the [Container].\"";
	}

	/// <summary>
	///     The guidance thrown by Resolve(Type) on the Root for a collection (<c>IEnumerable&lt;T&gt;</c> / <c>T[]</c>)
	///     that holds a build-on-demand disposable member: materializing it on the Root would accumulate those
	///     disposables for the container's lifetime. Unlike a single service there is no <c>Owned&lt;T&gt;</c> form
	///     for a collection, so the guidance steers to a child scope, direct injection, or <c>LifetimeSafety.Loose</c>.
	/// </summary>
	private static string CollectionWithheldMessage(string collection)
	{
		string display = collection.Replace("global::", string.Empty);
		return $"\"Awaiten: the collection '{display}' has a build-on-demand disposable member and is withheld from by-type resolution on the container root under strict lifetime safety; resolve it from a child scope, inject it directly, or set LifetimeSafety.Loose on the [Container].\"";
	}

	/// <summary>
	///     The guidance thrown by Resolve(Type) on the Root for a keyed dictionary
	///     (<c>IReadOnlyDictionary&lt;string, T&gt;</c>) withheld there under strict lifetime safety: it has a
	///     build-on-demand disposable keyed member, so materializing it by type off the Root would accumulate those
	///     disposables for the container's lifetime. The keyed counterpart of <see cref="CollectionWithheldMessage" />.
	/// </summary>
	private static string KeyedCollectionWithheldMessage(string dictionary)
	{
		string display = dictionary.Replace("global::", string.Empty);
		return $"\"Awaiten: the keyed dictionary '{display}' has a build-on-demand disposable member and is withheld from by-type resolution on the container root under strict lifetime safety; resolve it from a child scope, inject it directly, or set LifetimeSafety.Loose on the [Container].\"";
	}

	/// <summary>
	///     The guidance thrown by Resolve(Type) on the Root for an awaited collection (<c>Task&lt;C&gt;</c>) that
	///     holds a build-on-demand disposable member: the produced task materializes its members eagerly, so
	///     resolving it by type on the Root would accumulate those disposables for the container's lifetime. The
	///     awaited counterpart of <see cref="CollectionWithheldMessage" />; like it, there is no <c>Owned&lt;T&gt;</c>
	///     form for a collection, so the guidance steers to a child scope, direct injection, or <c>LifetimeSafety.Loose</c>.
	/// </summary>
	private static string AwaitedCollectionWithheldMessage(string collection)
	{
		string display = collection.Replace("global::", string.Empty);
		return $"\"Awaiten: the awaited collection '{display}' has a build-on-demand disposable member and is withheld from by-type resolution on the container root under strict lifetime safety; resolve it from a child scope, inject it directly, or set LifetimeSafety.Loose on the [Container].\"";
	}

	/// <summary>
	///     The guidance thrown by Resolve(Type) on the Root for an awaited keyed dictionary
	///     (<c>Task&lt;IReadOnlyDictionary&lt;string, T&gt;&gt;</c>) that holds a build-on-demand disposable member:
	///     the produced task materializes its members eagerly, so resolving it by type on the Root would accumulate
	///     those disposables for the container's lifetime. The keyed counterpart of
	///     <see cref="AwaitedCollectionWithheldMessage" />; like it there is no <c>Owned&lt;T&gt;</c> form for a
	///     dictionary, so the guidance steers to a child scope, direct injection, or <c>LifetimeSafety.Loose</c>.
	/// </summary>
	private static string AwaitedKeyedCollectionWithheldMessage(string dictionary)
	{
		string display = dictionary.Replace("global::", string.Empty);
		return $"\"Awaiten: the awaited keyed dictionary '{display}' has a build-on-demand disposable member and is withheld from by-type resolution on the container root under strict lifetime safety; resolve it from a child scope, inject it directly, or set LifetimeSafety.Loose on the [Container].\"";
	}

	/// <summary>
	///     The guidance thrown by Resolve(Type) for a collection (<c>IEnumerable&lt;T&gt;</c> / <c>T[]</c>) that
	///     holds an async-tainted member: a collection is materialized synchronously (built eagerly into an array,
	///     with no place to await an initialization), so it has no synchronous and no asynchronous resolution
	///     on any scope. Mirrors AWT122; the pragmatic SyncResolveAfterInit mode (which warms the graph) is
	///     the way to make it synchronously resolvable after InitializeAsync.
	/// </summary>
	private static string CollectionAsyncMessage(string collection)
	{
		string display = collection.Replace("global::", string.Empty);
		return $"\"Awaiten: the collection '{display}' has an async-tainted member and cannot be materialized synchronously (a collection is built eagerly, with no place to await an initialization); consume it as IAsyncEnumerable<T> and resolve it through ResolveAsync, set SyncResolveAfterInit on the [Container] and resolve it after InitializeAsync, or remove the async member from the collection.\"";
	}

	/// <summary>
	///     The guidance thrown by Resolve(Type) for an <c>IAsyncEnumerable&lt;T&gt;</c> collection that holds an
	///     async-tainted member: unlike the synchronous shapes it is resolvable (it awaits each member) but only
	///     asynchronously, so synchronous resolution steers to <c>ResolveAsync</c> (the async counterpart of
	///     <see cref="AsyncWithheldMessage" /> for the collection shape).
	/// </summary>
	private static string AsyncCollectionAsyncMessage(string collection)
	{
		string display = collection.Replace("global::", string.Empty);
		return $"\"Awaiten: the async collection '{display}' has a member that requires asynchronous initialization, so it awaits its members and cannot be materialized synchronously; resolve it through ResolveAsync (or warm it through InitializeAsync / CreateScopeAsync), or set SyncResolveAfterInit on the [Container].\"";
	}

	/// <summary>
	///     The guidance thrown by ResolveAsync(Type) on the Root for an <c>IAsyncEnumerable&lt;T&gt;</c> collection
	///     that holds a build-on-demand disposable member: materializing it on the Root would accumulate those
	///     disposables for the container's lifetime. The async counterpart of <see cref="CollectionWithheldMessage" />.
	/// </summary>
	private static string CollectionAsyncRootWithheldMessage(string collection)
	{
		string display = collection.Replace("global::", string.Empty);
		return $"\"Awaiten: the async collection '{display}' has a build-on-demand disposable member and is withheld from by-type resolution on the container root under strict lifetime safety; resolve it from a child scope (await CreateScopeAsync(), ResolveAsync from that scope, then dispose the scope), inject it directly, or set LifetimeSafety.Loose on the [Container].\"";
	}

	/// <summary>
	///     The guidance message (a quoted string literal) thrown by Resolve(Type) on the Root when a plain Func over
	///     the service is requested: that factory accumulates disposables on the root, so the leak-free
	///     <c>Func&lt;…, Owned&lt;T&gt;&gt;</c> form is offered instead. The Func stays resolvable from a child scope,
	///     which bounds the disposables it builds; the bare type may also be resolvable (its single resolution is
	///     bounded).
	/// </summary>
	private static string FuncWithheldMessage(string service)
	{
		string display = service.Replace("global::", string.Empty);
		return $"\"Awaiten: a plain Func over '{display}' is withheld from by-type resolution on the container root under strict lifetime safety because building it on demand accumulates disposables on the container root; resolve it as Func<…, Owned<{display}>> for per-use disposal, resolve it from a child scope, or set LifetimeSafety.Loose on the [Container].\"";
	}

	/// <summary>
	///     The guidance message (a quoted string literal) thrown by Resolve(Type) when an async-tainted service is
	///     requested by type: it is async-initialized. It implements IAsyncInitializable, is produced by an
	///     asynchronous Task&lt;T&gt; / ValueTask&lt;T&gt; factory, or reaches one through its non-deferred
	///     dependencies, so it has no synchronous resolution path in the strict default and must be obtained
	///     asynchronously.
	/// </summary>
	private static string AsyncWithheldMessage(string service)
	{
		string display = service.Replace("global::", string.Empty);
		return $"\"Awaiten: '{display}' requires asynchronous initialization (it is IAsyncInitializable, is produced by an async Task<T> factory, or depends on one) and cannot be resolved synchronously; resolve it through ResolveAsync (or warm it through InitializeAsync / CreateScopeAsync), or set SyncResolveAfterInit on the [Container].\"";
	}

	/// <summary>
	///     The guidance message (a quoted string literal) thrown by ResolveAsync(Type) on the Root when a disposable
	///     build-on-demand service that needs asynchronous initialization is requested by its bare type: building it
	///     on demand from the Root tracks a fresh disposable on the root for the container's lifetime (an unbounded
	///     leak), so the Root withholds it. It stays resolvable from a child scope, whose disposal bounds it. The
	///     async counterpart to BareWithheldMessage (a synchronous <c>Owned&lt;T&gt;</c> cannot await
	///     initialization, so the async owned form <c>Func&lt;…, Task&lt;Owned&lt;T&gt;&gt;&gt;</c> is offered).
	/// </summary>
	private static string AsyncRootWithheldMessage(string service)
	{
		string display = service.Replace("global::", string.Empty);
		return $"\"Awaiten: '{display}' is a disposable transient that needs asynchronous initialization; resolving it through ResolveAsync on the container root would track a fresh disposable on the root for the container's lifetime, so it is withheld there under strict lifetime safety. Obtain it through Func<…, Task<Owned<{display}>>> (or Task<Owned<{display}>>) for per-use disposal, resolve it from a child scope (await CreateScopeAsync(), ResolveAsync from that scope, then dispose the scope), or set LifetimeSafety.Loose on the [Container].\"";
	}

	/// <summary>
	///     The root-withheld entries (dispatchable, but carrying guidance). Their types and messages populate the
	///     <c>__withheld</c> table that Resolve throws from on the Root, while the parallel <c>__rootWithheld</c>
	///     mask (keyed by dispatch case) is what TryResolve consults to return false for them on the Root only.
	/// </summary>
	private static List<DispatchEntry> Withheld(List<DispatchEntry> entries)
	{
		List<DispatchEntry> withheld = new();
		foreach (DispatchEntry entry in entries)
		{
			if (entry.RootWithheld)
			{
				withheld.Add(entry);
			}
		}

		return withheld;
	}

	/// <summary>
	///     The (service type, guidance) pairs that populate the static <c>__withheld</c> table: the
	///     dispatchable root-withheld disposable services (<paramref name="rootWithheld" />), plus the
	///     async-tainted services excluded from synchronous resolution entirely. Deduplicated by service type
	///     (the two sources are disjoint, since an async-tainted service emits no dispatch entry, but a
	///     belt-and-braces dedup keeps the emitted dictionary initializer free of a duplicate-key throw).
	/// </summary>
	private static List<(string Type, string Guidance)> WithheldTypes(
		List<DispatchEntry> rootWithheld, InstanceModel[] instances, Names names, Dictionary<ServiceKey, int> serviceToIndex, bool syncResolveAfterInit)
	{
		List<(string Type, string Guidance)> result = new();
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (DispatchEntry entry in rootWithheld.Where(entry => seen.Add(entry.Type)))
		{
			result.Add((entry.Type, entry.Guidance!));
		}

		foreach ((string service, string guidance) in AsyncWithheldServices(instances, syncResolveAfterInit))
		{
			if (seen.Add(service))
			{
				result.Add((service, guidance));
			}
		}

		foreach ((string type, string guidance) in AsyncWithheldCollections(names, serviceToIndex, syncResolveAfterInit))
		{
			if (seen.Add(type))
			{
				result.Add((type, guidance));
			}
		}

		return result;
	}

	/// <summary>
	///     The collection shape types that are reachable only after asynchronous warm-up, paired with their
	///     guidance: every shape of an unkeyed, non-synthesis-suppressed collection that holds an async-tainted
	///     member, when the container is not in pragmatic SyncResolveAfterInit mode. Unlike a single async service
	///     a collection has no <c>ResolveAsync</c> form (it is built eagerly, with no place to await), so its
	///     shapes have no synchronous dispatch entry on any scope; without this they would surface as a generic
	///     "no registration" rather than the AWT122-style guidance.
	/// </summary>
	private static IEnumerable<(string Type, string Guidance)> AsyncWithheldCollections(
		Names names, Dictionary<ServiceKey, int> serviceToIndex, bool syncResolveAfterInit)
	{
		if (syncResolveAfterInit)
		{
			yield break;
		}

		foreach (ServiceMembers collection in names.Collections)
		{
			if (collection.Key is not null
			    || SynthesisSuppressed(serviceToIndex, collection.Service)
			    || names.IsSyncCollection(new ServiceKey(collection.Service, collection.Key)))
			{
				continue;
			}

			foreach (string shape in AwaitenGenerator.CollectionShapeTypes(collection.Service))
			{
				yield return (shape, CollectionAsyncMessage(shape));
			}

			// The IAsyncEnumerable<T> shape IS resolvable (it awaits its members) but only asynchronously, so its
			// synchronous Resolve throws guidance toward ResolveAsync (an async arm serves ResolveAsync itself).
			// Unless that shape is explicitly registered: then the registration owns the slot and its own dispatch
			// entry or guidance applies, not the synthesized view's.
			if (!AsyncShapeRegistered(serviceToIndex, collection.Service))
			{
				string asyncShape = AwaitenGenerator.AsyncEnumerableShapeType(collection.Service);
				yield return (asyncShape, AsyncCollectionAsyncMessage(asyncShape));
			}
		}
	}

	/// <summary>
	///     The service types that are reachable only asynchronously, paired with their guidance: each
	///     async-tainted, non-parameterized service's (non-keyed) service types, when the container is not in
	///     pragmatic mode (where the same services are synchronously resolvable after warm-up). They have no
	///     synchronous dispatch entry, so without this they would surface as a generic "no registration".
	///     A parameterized service is excluded: it is never resolvable by its bare type (it needs its runtime
	///     arguments through a <c>Func&lt;TArg…, …&gt;</c>), so the "resolve through ResolveAsync" guidance would
	///     not fit; its bare-type unavailability is governed by parameterization, not asynchronous initialization.
	/// </summary>
	private static IEnumerable<(string Service, string Guidance)> AsyncWithheldServices(
		InstanceModel[] instances, bool syncResolveAfterInit)
	{
		if (syncResolveAfterInit)
		{
			yield break;
		}

		HashSet<string> seen = new(StringComparer.Ordinal);
		for (int i = 0; i < instances.Length; i++)
		{
			if (!instances[i].IsAsyncTainted || instances[i].IsParameterized)
			{
				continue;
			}

			foreach (ServiceKey service in instances[i].Services.AsArray())
			{
				// Keyed registrations are reached only by [FromKey] injection, never by-type resolution.
				if (service.Key is not null || !seen.Add(service.Service))
				{
					continue;
				}

				yield return (service.Service, AsyncWithheldMessage(service.Service));
			}
		}
	}

	/// <summary>
	///     Emits the static <c>__withheld</c> table mapping each withheld service type to its guidance message.
	///     <c>Resolve</c> consults it (after <c>TryResolve</c> returned <see langword="false" />) to throw the
	///     targeted guidance instead of the generic "no registration" message: for a root-withheld disposable
	///     on the Root, or an async-only service on any scope.
	/// </summary>
	private static void EmitWithheldTable(StringBuilder builder, int depth, List<(string Type, string Guidance)> entries)
	{
		Indent(builder, depth).AppendLine(
			"private static readonly global::System.Collections.Generic.Dictionary<global::System.Type, string> __withheld = new global::System.Collections.Generic.Dictionary<global::System.Type, string>");
		Indent(builder, depth).AppendLine("{");
		foreach ((string type, string guidance) in entries)
		{
			Indent(builder, depth + 1).Append("{ typeof(").Append(type).Append("), ").Append(guidance).AppendLine(" },");
		}

		Indent(builder, depth).AppendLine("};");
	}
}
