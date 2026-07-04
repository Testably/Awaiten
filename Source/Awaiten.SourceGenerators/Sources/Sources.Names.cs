using System.Text;
using Awaiten.SourceGenerators.Entities;

namespace Awaiten.SourceGenerators;

internal static partial class Sources
{
	/// <summary>
	///     Assigns each instance a readable resolver method name (<c>ResolveGreeter</c>) and cache
	///     field name (<c>_greeter</c>) derived from the implementation's simple type name, appending a
	///     numeric suffix only when two implementations would otherwise collide (case-insensitively).
	/// </summary>
	private sealed class Names
	{
		private readonly string[] _fields;
		private readonly string[] _resolvers;
		private readonly ServiceMembers[] _collections;
		private readonly Dictionary<ServiceKey, string[]> _collectionResolvers;
		private readonly Dictionary<ServiceKey, int[]> _collectionMemberIndices;
		private readonly HashSet<ServiceKey> _syncCollections;
		private readonly KeyedNames _keyed;

		private Names(string[] resolvers, string[] fields, ServiceMembers[] collections, Dictionary<ServiceKey, string[]> collectionResolvers, Dictionary<ServiceKey, int[]> collectionMemberIndices, HashSet<ServiceKey> syncCollections, KeyedNames keyed)
		{
			_resolvers = resolvers;
			_fields = fields;
			_collections = collections;
			_collectionResolvers = collectionResolvers;
			_collectionMemberIndices = collectionMemberIndices;
			_syncCollections = syncCollections;
			_keyed = keyed;
		}

		// The collection-resolvable services in first-seen (type, key) order, driving the public IEnumerable<T> /
		// T[] dispatch (unkeyed collections only) and the injected collection literals.
		public ServiceMembers[] Collections => _collections;

		public string Resolver(int index) => _resolvers[index];

		public string Field(int index) => _fields[index];

		// The resolver method names of a collection's members, in registration order (empty when the (type, key)
		// has no registration, which materializes an empty array).
		public string[] CollectionResolvers(ServiceKey collection)
			=> _collectionResolvers.TryGetValue(collection, out string[]? resolvers) ? resolvers : System.Array.Empty<string>();

		// The instance indices of a collection's members, in the same registration order as CollectionResolvers, so
		// the async-collection materialization can test each member's async taint and pick its resolver accordingly.
		public int[] CollectionMemberIndices(ServiceKey collection)
			=> _collectionMemberIndices.TryGetValue(collection, out int[]? indices) ? indices : System.Array.Empty<int>();

		// Whether a collection can be materialized synchronously - i.e. every member has a synchronous resolver.
		// A collection with an async-tainted member (strict mode) is omitted from the public sync dispatch, so no
		// synchronous resolver is referenced where none is emitted; injecting such a collection is AWT122.
		public bool IsSyncCollection(ServiceKey collection) => _syncCollections.Contains(collection);

		// The keyed-collection-resolvable services in first-seen order, driving the public
		// IReadOnlyDictionary<string, T> dispatch.
		public KeyedServiceMembers[] KeyedCollections => _keyed.Collections;

		// The (key, resolver, root-ownedness) of a keyed collection's members, in registration order (empty when
		// the service has no keyed registration, which materializes an empty dictionary). RootOwned picks the call
		// form of the member's static resolver (Root.ResolveX(__s.__root) vs ResolveX(__s)).
		public (string Key, string Resolver, bool RootOwned)[] KeyedCollectionResolvers(string service)
			=> _keyed.Resolvers.TryGetValue(service, out (string Key, string Resolver, bool RootOwned)[]? resolvers) ? resolvers : System.Array.Empty<(string, string, bool)>();

		// Whether a keyed collection can be materialized synchronously - i.e. every keyed member has a synchronous
		// resolver. One with an async-tainted member is omitted from the public sync dispatch (injecting it is AWT122).
		public bool IsSyncKeyedCollection(string service) => _keyed.Sync.Contains(service);

		// The async members are named off the synchronous resolver/field so they stay collision-safe
		// together: ResolveFoo -> ResolveFooAsync / CreateFooAsync, _foo -> _fooAsyncTask.
		public string AsyncResolver(int index) => _resolvers[index] + "Async";

		public string AsyncCreator(int index) => "Create" + _resolvers[index].Substring("Resolve".Length) + "Async";

		public string AsyncField(int index) => _fields[index] + "AsyncTask";

		// The "wiring complete" flag for a synchronously-cached instance with deferred ([Inject(Deferred = true)])
		// members: _foo -> _fooWired. It gates the lock-free fast path so a concurrent caller returns the cached
		// instance only once its deferred members are wired, while the mid-wiring re-entrant resolve (which sees it
		// still false) skips the fast path and terminates the cycle through the lock instead.
		public string WiredField(int index) => _fields[index] + "Wired";

		public static Names Build(InstanceModel[] instances, ServiceMembers[] collections, KeyedServiceMembers[] keyedCollections, bool syncResolveAfterInit)
		{
			string[] resolvers = new string[instances.Length];
			string[] fields = new string[instances.Length];
			Dictionary<string, string> implToResolver = new(StringComparer.Ordinal);
			Dictionary<string, int> implToIndex = new(StringComparer.Ordinal);
			HashSet<string> syncImpls = new(StringComparer.Ordinal);
			HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);

			for (int i = 0; i < instances.Length; i++)
			{
				string baseName = Sanitize(instances[i].Name);
				string name = baseName;
				int suffix = 2;
				while (!TryReserve(used, name))
				{
					name = baseName + suffix;
					suffix++;
				}

				resolvers[i] = "Resolve" + name;
				fields[i] = "_" + char.ToLowerInvariant(name[0]) + name.Substring(1);
				implToResolver[instances[i].ImplementationType] = resolvers[i];
				implToIndex[instances[i].ImplementationType] = i;

				// A member is synchronously resolvable unless it is async-tainted in strict mode (in pragmatic
				// SyncResolveAfterInit mode every service has a synchronous resolver, delegating to the async one).
				if (!instances[i].IsAsyncTainted || syncResolveAfterInit)
				{
					syncImpls.Add(instances[i].ImplementationType);
				}
			}

			// Map each collection's member implementations to their resolvers, preserving registration order; a
			// collection is sync-materializable only when every member has a synchronous resolver.
			Dictionary<ServiceKey, string[]> collectionResolvers = new();
			Dictionary<ServiceKey, int[]> collectionMemberIndices = new();
			HashSet<ServiceKey> syncCollections = new();
			foreach (ServiceMembers members in collections)
			{
				string[] memberImpls = members.Implementations.AsArray();
				string[] memberResolvers = new string[memberImpls.Length];
				int[] memberIndices = new int[memberImpls.Length];
				bool allSync = true;
				for (int m = 0; m < memberImpls.Length; m++)
				{
					memberResolvers[m] = implToResolver[memberImpls[m]];
					memberIndices[m] = implToIndex[memberImpls[m]];
					allSync &= syncImpls.Contains(memberImpls[m]);
				}

				ServiceKey collectionKey = new(members.Service, members.Key);
				collectionResolvers[collectionKey] = memberResolvers;
				collectionMemberIndices[collectionKey] = memberIndices;
				if (allSync)
				{
					syncCollections.Add(collectionKey);
				}
			}

			return new Names(resolvers, fields, collections, collectionResolvers, collectionMemberIndices, syncCollections,
				BuildKeyedNames(instances, keyedCollections, implToResolver, implToIndex, syncImpls));
		}

		// Maps each keyed collection's (key, implementation) members to their (key, resolver, root-ownedness)
		// entries, preserving registration order; the keyed dictionary is materialized synchronously, so it is
		// sync-materializable only when every member has a synchronous resolver.
		private static KeyedNames BuildKeyedNames(
			InstanceModel[] instances,
			KeyedServiceMembers[] keyedCollections,
			Dictionary<string, string> implToResolver,
			Dictionary<string, int> implToIndex,
			HashSet<string> syncImpls)
		{
			Dictionary<string, (string Key, string Resolver, bool RootOwned)[]> keyedResolvers = new(StringComparer.Ordinal);
			HashSet<string> syncKeyedCollections = new(StringComparer.Ordinal);
			foreach (KeyedServiceMembers keyed in keyedCollections)
			{
				KeyedMember[] keyedMembers = keyed.Members.AsArray();
				(string Key, string Resolver, bool RootOwned)[] members = new (string, string, bool)[keyedMembers.Length];
				bool allSync = true;
				for (int m = 0; m < keyedMembers.Length; m++)
				{
					string implementation = keyedMembers[m].Implementation;
					members[m] = (keyedMembers[m].Key, implToResolver[implementation], IsRootOwned(instances[implToIndex[implementation]]));
					allSync &= syncImpls.Contains(implementation);
				}

				keyedResolvers[keyed.Service] = members;
				if (allSync)
				{
					syncKeyedCollections.Add(keyed.Service);
				}
			}

			return new KeyedNames(keyedCollections, keyedResolvers, syncKeyedCollections);
		}

		// The keyed-collection naming state, grouped so it travels as one unit: the keyed-collection-resolvable
		// services (first-seen order), each service's (key, resolver, root-ownedness) members, and the services
		// whose every member is synchronously resolvable.
		private readonly record struct KeyedNames(
			KeyedServiceMembers[] Collections,
			Dictionary<string, (string Key, string Resolver, bool RootOwned)[]> Resolvers,
			HashSet<string> Sync);

		// Reserves a base name together with the derived member names generated off it (the async resolver's
		// 'Async', the async cache field's 'AsyncTask' and the wiring flag's 'Wired' suffixes). The base names
		// alone are not enough: a service type literally named e.g. 'FooWired' would otherwise collide with the
		// '_fooWired' wiring flag derived for a deferred-member service named 'Foo' (CS0102 in the generated
		// container), and likewise for 'FooAsync'/'FooAsyncTask' against the async members. Rejecting a name when
		// any of the four is already taken keeps every derived name unique in both directions.
		private static bool TryReserve(HashSet<string> used, string name)
		{
			string asyncName = name + "Async";
			string asyncTask = name + "AsyncTask";
			string wired = name + "Wired";
			if (used.Contains(name) || used.Contains(asyncName) || used.Contains(asyncTask) || used.Contains(wired))
			{
				return false;
			}

			used.Add(name);
			used.Add(asyncName);
			used.Add(asyncTask);
			used.Add(wired);
			return true;
		}

		private static string Sanitize(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "Service";
			}

			StringBuilder builder = new(name.Length);
			foreach (char c in name.Where(c => char.IsLetterOrDigit(c) || c == '_'))
			{
				builder.Append(c);
			}

			if (builder.Length == 0 || (!char.IsLetter(builder[0]) && builder[0] != '_'))
			{
				builder.Insert(0, '_');
			}

			return builder.ToString();
		}
	}
}
