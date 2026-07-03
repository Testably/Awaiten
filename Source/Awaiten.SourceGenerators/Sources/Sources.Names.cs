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

		private Names(string[] resolvers, string[] fields, ServiceMembers[] collections, Dictionary<ServiceKey, string[]> collectionResolvers, Dictionary<ServiceKey, int[]> collectionMemberIndices, HashSet<ServiceKey> syncCollections)
		{
			_resolvers = resolvers;
			_fields = fields;
			_collections = collections;
			_collectionResolvers = collectionResolvers;
			_collectionMemberIndices = collectionMemberIndices;
			_syncCollections = syncCollections;
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

		// The async members are named off the synchronous resolver/field so they stay collision-safe
		// together: ResolveFoo -> ResolveFooAsync / CreateFooAsync, _foo -> _fooAsyncTask.
		public string AsyncResolver(int index) => _resolvers[index] + "Async";

		public string AsyncCreator(int index) => "Create" + _resolvers[index].Substring("Resolve".Length) + "Async";

		public string AsyncField(int index) => _fields[index] + "AsyncTask";

		public static Names Build(InstanceModel[] instances, ServiceMembers[] collections, bool syncResolveAfterInit)
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
				while (!used.Add(name))
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

			return new Names(resolvers, fields, collections, collectionResolvers, collectionMemberIndices, syncCollections);
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
