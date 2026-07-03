using System.Collections.Immutable;
using System.Text;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Awaiten.SourceGenerators;

/// <summary>
///     The Awaiten incremental source generator. Discovers <c>[Container]</c> partial classes,
///     collects their registration attributes, resolves the object graph at compile time and emits
///     the container implementation (resolution, scopes and disposal). Invalid wiring is reported as
///     a diagnostic.
/// </summary>
/// <remarks>
///     Assumptions: a container is a non-generic <c>partial class</c> (it may be nested, in which
///     case every enclosing type must be declared <c>partial</c>); enclosing types are non-generic;
///     each constructed type has a single accessible constructor (when several exist, the one with the
///     most resolvable parameters is chosen). Registrations of the same implementation are coalesced
///     into a single instance, so a multi-service registration shares one object.
/// </remarks>
[Generator]
public sealed class AwaitenGenerator : IIncrementalGenerator
{
	private const string ContainerAttributeName = "Awaiten.ContainerAttribute";

	private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		IncrementalValuesProvider<ContainerModel> models = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				ContainerAttributeName,
				static (node, _) => node is ClassDeclarationSyntax,
				static (ctx, ct) => BuildModel(ctx, ct))
			.Where(static model => model is not null)
			.Select(static (model, _) => model!);

		context.RegisterSourceOutput(models, static (spc, model) =>
		{
			foreach (DiagnosticInfo diagnostic in model.Diagnostics.AsArray())
			{
				spc.ReportDiagnostic(diagnostic.ToDiagnostic());
			}

			spc.AddSource(model.HintName, SourceText.From(Emitter.Emit(model), Encoding.UTF8));
		});
	}

	private static ContainerModel? BuildModel(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
	{
		if (context.TargetSymbol is not INamedTypeSymbol containerSymbol)
		{
			return null;
		}

		Compilation compilation = context.SemanticModel.Compilation;

		// Strict lifetime safety (the default) withholds a disposable build-on-demand service from by-type
		// resolution; Loose relaxes that for MS.DI interop. The root-accumulating Func diagnostic (AWT118) is
		// reported by AwaitenAnalyzer rather than here, so it can be suppressed in source with
		// #pragma warning disable AWT118 / [SuppressMessage] (a generator-reported diagnostic cannot be).
		bool strict = ReadStrict(containerSymbol);

		// Pragmatic loosening: when set, async-tainted services may also be resolved synchronously after
		// InitializeAsync has warmed them, and the AWT119/AWT120 sync-resolution diagnostics are not reported.
		bool syncResolveAfterInit = ReadSyncResolveAfterInit(containerSymbol);

		// IAsyncDisposable support (the async drain, DisposeAsync on the Scope/Root, and tracking of
		// async-disposable services) is emitted only when the referenced Awaiten runtime actually exposes it -
		// AsyncDisposableSupport reads that off Owned<T>, not off the mere presence of System.IAsyncDisposable in
		// the compilation. When it is absent the generated container is synchronous-dispose only and references
		// no IAsyncDisposable, so it still compiles there.
		bool hasAsyncDisposable = AsyncDisposableSupport(compilation) is not null;

		List<DiagnosticInfo> diagnostics = new();

		// The container must be a static class: it is a pure definition (registrations plus static factory
		// and instance members) and the usable instance is the generated Root. A non-static class is an
		// error; the emitter still emits a throwing Root so consumers fail on this AWT116 rather than on a
		// cascade of missing-member errors.
		if (!containerSymbol.IsStatic)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NonStaticContainer,
				LocationInfo.From(containerSymbol.Locations.FirstOrDefault()),
				new EquatableArray<string>([Display(containerSymbol.ToDisplayString(FullyQualified)),])));
		}

		GraphModel graph = BuildGraph(containerSymbol, compilation, diagnostics, cancellationToken);

		ValidateRuntimeArguments(graph.Instances, graph.InstanceLocations, graph.ServiceToImpl, graph.ImplToIndex, diagnostics);

		LocationInfo? containerLocation = LocationInfo.From(containerSymbol.Locations.FirstOrDefault());
		DetectCycles(graph.Instances, graph.ConstructionDependencies, containerLocation, diagnostics);
		DetectCaptiveDependencies(graph.Instances, graph.Dependencies, graph.InstanceLocations, diagnostics);

		// AWT119/AWT120 (strict only): a synchronous Func<T>/Lazy<T>/Owned<T> relationship resolves its
		// target without awaiting initialization, so it may not target an async-tainted service. The
		// pragmatic SyncResolveAfterInit mode allows such resolution (after warm-up) and so is not reported.
		if (!syncResolveAfterInit)
		{
			DetectSynchronousAsyncResolution(
				graph.Instances, graph.Dependencies, graph.ServiceToImpl, graph.ImplToIndex, graph.InstanceLocations, diagnostics);
			DetectSynchronousAsyncCollection(
				graph.Instances, graph.Collections, graph.ImplToIndex, graph.InstanceLocations, diagnostics);
		}

		string? containerNamespace = containerSymbol.ContainingNamespace is { IsGlobalNamespace: false, } ns
			? ns.ToDisplayString()
			: null;

		// Walk the enclosing types (outermost first) so the container can be a nested type.
		List<TypeDeclaration> containingTypes = new();
		for (INamedTypeSymbol? outer = containerSymbol.ContainingType; outer is not null; outer = outer.ContainingType)
		{
			containingTypes.Insert(0, new TypeDeclaration(KeywordOf(outer), outer.Name));
		}

		// Qualify the hint name with namespace and enclosing types so containers that share a simple
		// name in different namespaces (or nesting) do not collide. Nested types use '+' (the metadata
		// separator) so a nested 'Outer+Inner' cannot collide with a namespace-qualified 'Outer.Inner'.
		string typePath = containingTypes.Count > 0
			? $"{string.Join("+", containingTypes.Select(t => t.Name))}+{containerSymbol.Name}"
			: containerSymbol.Name;
		string hintName = containerNamespace is null
			? $"Awaiten.{typePath}.g.cs"
			: $"Awaiten.{containerNamespace}.{typePath}.g.cs";

		return new ContainerModel(
			containerNamespace,
			new EquatableArray<TypeDeclaration>(containingTypes.ToArray()),
			containerSymbol.Name,
			hintName,
			new EquatableArray<InstanceModel>(graph.Instances.ToArray()),
			new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()),
			strict,
			syncResolveAfterInit,
			hasAsyncDisposable,
			new EquatableArray<ServiceMembers>(graph.Collections.ToArray()),
			new EquatableArray<string>(graph.VarianceCandidates.ToArray()));
	}

	/// <summary>
	///     The <c>System.IAsyncDisposable</c> symbol when - and only when - the referenced Awaiten runtime
	///     actually exposes its async-disposal surface, otherwise <see langword="null" />. The runtime gates
	///     that surface (<c>Owned&lt;T&gt;.DisposeAsync</c> and the awaiting scope drain) behind
	///     <c>#if NET || NETSTANDARD2_1_OR_GREATER</c>, so a consumer that binds the netstandard2.0 asset -
	///     net6.0 / net7.0, a netstandard2.1 library, or net48 even with Microsoft.Bcl.AsyncInterfaces - gets an
	///     <c>Owned&lt;T&gt;</c> with no <c>DisposeAsync</c> even though its own compilation can see
	///     <c>System.IAsyncDisposable</c>. Emitting the surface there would hand back a handle that cannot be
	///     <c>await using</c>d and would track async-only services the synchronous drain cannot release. Reading
	///     the capability off <c>Owned&lt;T&gt;</c> keeps the generated container consistent with the exact
	///     runtime asset it compiles against - the realized result of that same <c>#if</c>.
	/// </summary>
	private static INamedTypeSymbol? AsyncDisposableSupport(Compilation compilation)
	{
		if (compilation.GetTypeByMetadataName("System.IAsyncDisposable") is not { } asyncDisposable)
		{
			return null;
		}

		return compilation.GetTypeByMetadataName("Awaiten.Owned`1") is { } owned
		       && owned.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, asyncDisposable))
			? asyncDisposable
			: null;
	}

	/// <summary>
	///     The framework/Awaiten interface symbols an instance is matched against, looked up once and passed
	///     together: <c>System.IDisposable</c>, the async-disposal symbol (the <c>System.IAsyncDisposable</c>
	///     from <see cref="AsyncDisposableSupport" />, so it is non-null only when the runtime actually exposes
	///     async disposal) and <c>Awaiten.IAsyncInitializable</c>. Any may be <see langword="null" /> when the
	///     compilation cannot see (or does not support) it.
	/// </summary>
	private sealed record WellKnownTypes(
		INamedTypeSymbol? Disposable,
		INamedTypeSymbol? AsyncDisposable,
		INamedTypeSymbol? AsyncInitializable);

	/// <summary>
	///     The container-wide inputs threaded to each per-implementation <see cref="BuildInstance" />: the
	///     container and compilation being analyzed, the coalesced resolution maps (the single-dispatch winner
	///     per key, the decorator-chain redirects, and the open generics rejected on a constraint violation),
	///     the well-known framework/Awaiten types, and the diagnostics sink. Passed together so building one
	///     instance takes a single context rather than a long parameter list.
	/// </summary>
	private sealed record BuildContext(
		INamedTypeSymbol ContainerSymbol,
		Compilation Compilation,
		Dictionary<ServiceKey, string> ServiceToImpl,
		Dictionary<string, DecoratorInner> DecoratorInner,
		WellKnownTypes WellKnown,
		HashSet<string> ConstraintRejected,
		bool ImportServices,
		VarianceState Variance,
		List<DiagnosticInfo> Diagnostics);

	/// <summary>
	///     The mutable coalesced graph state the decorator-chain and composite builders rewrite in place: the
	///     single-dispatch winner per service key, the coalesced implementations in declaration order, and the
	///     collection membership per service. Grouped so the rewriting steps take one handle rather than the
	///     three maps separately.
	/// </summary>
	private sealed record CoalescedGraph(
		Dictionary<ServiceKey, string> ServiceToImpl,
		List<ImplInfo> ImplOrder,
		Dictionary<ServiceKey, List<string>> ServiceMembers);

	/// <summary>
	///     Resolves the container's object graph: coalesces its registrations, builds an
	///     <see cref="InstanceModel" /> per implementation (selecting constructors / factories / instance
	///     members) and computes the direct-dependency edges between them. Registration faults discovered on
	///     the way (AWT101/103/104/107-112) are appended to <paramref name="diagnostics" />. Shared by the
	///     generator (which emits from the graph) and <see cref="AwaitenAnalyzer" /> (which walks it for AWT118).
	/// </summary>
	internal static GraphModel BuildGraph(
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics,
		CancellationToken cancellationToken)
	{
		// Detect async disposal off the referenced Awaiten runtime (Owned<T>), so an instance is marked
		// IsAsyncDisposable only when the emitter will actually emit the async-disposal surface for it -
		// see AsyncDisposableSupport for why this differs from the bare System.IAsyncDisposable lookup.
		WellKnownTypes wellKnown = new(
			compilation.GetTypeByMetadataName("System.IDisposable"),
			AsyncDisposableSupport(compilation),
			compilation.GetTypeByMetadataName("Awaiten.IAsyncInitializable"));

		// [ImportServices]: any otherwise-unresolved direct dependency falls through to the external provider
		// instead of being reported as missing (AWT101), the blanket form of per-parameter [FromServices].
		// Computed up front because it widens constructor selection everywhere a constructor is chosen - the
		// open generic expansion seed, decorator inner-parameter detection, composite validation and
		// BuildInstance must all scan the same constructor the emitted container builds through.
		bool importServices = ContainerImportsServices(containerSymbol);

		(List<RawRegistration> raw, HashSet<string> constraintRejected) = ContainerRegistrations.Collect(containerSymbol, importServices, diagnostics);
		List<DecorateRegistration> decorators = ContainerRegistrations.CollectDecorators(containerSymbol);
		List<CompositeRegistration> composites = ContainerRegistrations.CollectComposites(containerSymbol);

		// Coalesce registrations by (service type, key): the first registration per key wins, and
		// registrations of the same implementation share one instance. Declaring one implementation with
		// two different lifetimes is reported as AWT107; two implementations under the same service type and
		// key as AWT117.
		(List<ImplInfo> implOrder, Dictionary<ServiceKey, string> serviceToImpl, Dictionary<ServiceKey, List<string>> serviceMembers, List<ServiceKey> serviceMemberOrder, List<(string ServiceType, INamedTypeSymbol Symbol)> varianceCandidates) =
			CoalesceByImplementation(raw, diagnostics);
		CoalescedGraph graph = new(serviceToImpl, implOrder, serviceMembers);

		// Decorator chains: for each [Decorate]d service, move the base implementation(s) onto a synthetic key
		// and register each decorator as a chain link whose inner parameter is redirected to the next-lower key,
		// rewriting the public winner and collection membership to the outermost decorator. decoratorInner is
		// consumed by ClassifyParameters below to key each link's inner parameter to the link it wraps.
		Dictionary<string, DecoratorInner> decoratorInner = new(StringComparer.Ordinal);
		if (decorators.Count > 0)
		{
			new DecoratorChainBuilder(containerSymbol, compilation, graph, decoratorInner, importServices, diagnostics)
				.Build(decorators);
		}

		// Composites: each [Composite<TComposite, TService>] registers the composite as an ordinary instance,
		// makes it the public single-dispatch winner for TService, and leaves the existing members untouched -
		// the composite is excluded from its own collection, so its collection parameter fans out to the OTHER
		// registrations. Runs after decorator chains so a composite fronts the decorated members.
		if (composites.Count > 0)
		{
			BuildComposites(composites, compilation, containerSymbol, graph, importServices, diagnostics);
		}

		List<InstanceModel> instances = new();
		List<LocationInfo?> instanceLocations = new();
		Dictionary<string, int> implToIndex = new(StringComparer.Ordinal);

		// Variance state: the candidate registrations, plus the accumulators BuildInstance fills as it redirects
		// consumer parameters - the top-level dispatch aliases (Part B) and the requested collection elements
		// (Part C) - both drained after the instance loop below.
		VarianceState variance = new(varianceCandidates, compilation);

		BuildContext buildContext = new(containerSymbol, compilation, serviceToImpl, decoratorInner, wellKnown, constraintRejected, importServices, variance, diagnostics);

		// Validate each implementation, select its constructor and build the instance.
		foreach (ImplInfo info in implOrder)
		{
			cancellationToken.ThrowIfCancellationRequested();
			InstanceModel? instance = BuildInstance(info, buildContext);
			if (instance is not null)
			{
				implToIndex[info.ImplementationType] = instances.Count;
				instances.Add(instance);
				instanceLocations.Add(info.Location);
			}
		}

		// Variance for collections (Part C): union every variance-compatible registration's members into each
		// requested closed-generic collection - IEnumerable<IHandler<OrderPlaced>> includes a registered
		// IHandler<DomainEvent> (in T). Done before the parameterized prune (so a copied parameterized member is
		// removed there too) and before edge building (so the unioned members drive cycle/captive/AWT122 analysis
		// and emission).
		UnionVarianceCollectionMembers(variance, serviceMembers, serviceMemberOrder);

		// Variance for top-level dispatch (Part B): expose each variance-redirected closed generic type as a
		// dispatch alias on its chosen target instance, so an imperative Resolve routes it the same way the
		// consumer parameter does. Only augments the instance's Services, so it is independent of the edges below.
		ApplyVarianceDispatchAliases(variance, instances, implToIndex, serviceToImpl);

		// A parameterized ([Arg]) service cannot be built without its runtime arguments, so it is reachable
		// only through its Func<TArg…, T> factory - never as a collection member. Prune such implementations
		// before they drive collection edges, AWT122 and emission.
		PruneParameterizedMembers(instances, serviceMembers);

		Dictionary<int, List<int>> dependencies = BuildDependencyGraph(instances, serviceToImpl, implToIndex, serviceMembers);
		Dictionary<int, List<int>> constructionDependencies = BuildConstructionGraph(instances, serviceToImpl, implToIndex, serviceMembers);

		// Async taint: an instance is tainted if its implementation is async-initialized, or if it reaches
		// one through non-deferred (Direct) edges. Relationship types (Func/Lazy/Owned/Task/Arg) launder the
		// taint - even the bare eager Owned<T>/Task<T>, which hand back a handle/awaitable rather than the
		// resolved-and-initialized value - so they contribute no edges to BuildDependencyGraph above. (They do
		// still close cycles, since they resolve at construction time; that is the wider construction graph.)
		bool[] tainted = PropagateAsyncTaint(instances, dependencies);
		for (int i = 0; i < instances.Count; i++)
		{
			if (tainted[i])
			{
				instances[i] = instances[i] with { IsAsyncTainted = true, };
			}
		}

		// The collection-resolvable membership, in first-seen service order and deduped by implementation,
		// keeping only members that were actually built (a member whose BuildInstance failed already surfaced
		// its own error and is absent from implToIndex, so it is dropped rather than emitted as a dangling
		// resolver call). A service whose only registrations were pruned contributes no collection.
		List<ServiceMembers> collections = new();
		foreach (ServiceKey service in serviceMemberOrder)
		{
			string[] members = serviceMembers[service].Where(implToIndex.ContainsKey).ToArray();
			if (members.Length > 0)
			{
				collections.Add(new ServiceMembers(service.Service, service.Key, new EquatableArray<string>(members)));
			}
		}

		// The variance candidates' service types (registration order), for the emitter's runtime variance
		// fallback: an imperative Resolve of a differently-closed generic interface no consumer parameter ever
		// requested (so no compile-time alias exists) is matched against these at runtime instead of throwing.
		List<string> varianceCandidateTypes = new(variance.Candidates.Count);
		foreach ((string serviceType, INamedTypeSymbol _) in variance.Candidates)
		{
			varianceCandidateTypes.Add(serviceType);
		}

		return new GraphModel(instances, dependencies, constructionDependencies, serviceToImpl, implToIndex, instanceLocations, collections, varianceCandidateTypes);
	}

	/// <summary>
	///     Removes parameterized ([Arg]) implementations from every collection's membership: such a service is
	///     built fresh from its runtime arguments and is reachable only through its <c>Func&lt;TArg…, T&gt;</c>
	///     factory, so it is never a collection member.
	/// </summary>
	private static void PruneParameterizedMembers(List<InstanceModel> instances, Dictionary<ServiceKey, List<string>> serviceMembers)
	{
		HashSet<string> parameterized = new(StringComparer.Ordinal);
		foreach (InstanceModel instance in instances)
		{
			if (instance.IsParameterized)
			{
				parameterized.Add(instance.ImplementationType);
			}
		}

		if (parameterized.Count == 0)
		{
			return;
		}

		foreach (List<string> members in serviceMembers.Values)
		{
			members.RemoveAll(parameterized.Contains);
		}
	}

	/// <summary>
	///     The instance indices of every collection-resolvable service's members, keyed by the collection's
	///     (element service type, key), for the transitive-disposable walk. Composed from the graph's
	///     <paramref name="collections" /> and <paramref name="implToIndex" /> (a member absent from the latter
	///     failed to build and is skipped).
	/// </summary>
	internal static Dictionary<ServiceKey, List<int>> CollectionMemberIndices(
		IReadOnlyList<ServiceMembers> collections,
		Dictionary<string, int> implToIndex)
	{
		Dictionary<ServiceKey, List<int>> members = new();
		foreach (ServiceMembers collection in collections)
		{
			List<int> indices = new();
			foreach (string implementation in collection.Implementations.AsArray())
			{
				if (implToIndex.TryGetValue(implementation, out int index))
				{
					indices.Add(index);
				}
			}

			members[new ServiceKey(collection.Service, collection.Key)] = indices;
		}

		return members;
	}

	/// <summary>
	///     Whether building the service at <paramref name="start" /> on its owner tracks a fresh disposable
	///     there: the service itself is disposable, or its construction transitively rebuilds one. The walk
	///     follows only <em>transient</em> dependency edges, because a scoped or singleton dependency is
	///     cached/shared (built at most once on the owner) and so is bounded, whereas a transient dependency is
	///     rebuilt - and, if disposable, re-tracked - on every construction. A collection (Enumerable) edge is
	///     followed too: it materializes its members eagerly into the owner, so a transient disposable member is
	///     rebuilt on every construction just like a direct transient dependency. Used to decide whether a plain
	///     <c>Func&lt;…&gt;</c> over the service accumulates on the container root (AWT118 / strict withholding):
	///     a non-disposable transient that injects a disposable transient leaks just the same when built
	///     repeatedly through a root-bound factory.
	/// </summary>
	internal static bool BuildsFreshDisposable(
		IReadOnlyList<InstanceModel> instances,
		Dictionary<ServiceKey, int> serviceToIndex,
		IReadOnlyDictionary<ServiceKey, List<int>> collectionMembers,
		int start)
	{
		HashSet<int> visited = new();
		Stack<int> stack = new();
		stack.Push(start);

		while (stack.Count > 0)
		{
			int node = stack.Pop();
			if (!visited.Add(node))
			{
				continue;
			}

			InstanceModel instance = instances[node];
			if (instance.NeedsDisposal)
			{
				return true;
			}

			PushFreshTransientDependencies(instance, instances, serviceToIndex, collectionMembers, stack);
		}

		return false;
	}

	// Pushes the dependencies of <paramref name="instance" /> that are rebuilt as part of constructing it: a
	// direct (non-deferred) transient dependency, and each transient member of a collection (a collection -
	// including the awaited Task<C> form, whose task starts materializing at construction - builds its members
	// eagerly during construction). A relationship/Owned/Arg parameter defers, and a scoped/singleton dependency
	// is cached/shared, so neither is followed.
	private static void PushFreshTransientDependencies(
		InstanceModel instance,
		IReadOnlyList<InstanceModel> instances,
		Dictionary<ServiceKey, int> serviceToIndex,
		IReadOnlyDictionary<ServiceKey, List<int>> collectionMembers,
		Stack<int> stack)
	{
		foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
		{
			if (parameter.Kind is DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable)
			{
				PushTransientCollectionMembers(KeyOf(parameter), instances, collectionMembers, stack);
			}
			else if (parameter.Kind == DependencyKind.Direct
			         && serviceToIndex.TryGetValue(KeyOf(parameter), out int dependency)
			         && instances[dependency].Lifetime == Lifetime.Transient)
			{
				stack.Push(dependency);
			}
		}
	}

	// Pushes each transient member of the collection reached under <paramref name="collectionKey" /> (its
	// element service type plus resolution key; a non-transient member is cached/shared, so it is bounded).
	private static void PushTransientCollectionMembers(
		ServiceKey collectionKey,
		IReadOnlyList<InstanceModel> instances,
		IReadOnlyDictionary<ServiceKey, List<int>> collectionMembers,
		Stack<int> stack)
	{
		if (!collectionMembers.TryGetValue(collectionKey, out List<int>? members))
		{
			return;
		}

		foreach (int member in members)
		{
			if (instances[member].Lifetime == Lifetime.Transient)
			{
				stack.Push(member);
			}
		}
	}

	private static (List<ImplInfo> Order, Dictionary<ServiceKey, string> ServiceToImpl, Dictionary<ServiceKey, List<string>> Members, List<ServiceKey> MemberOrder, List<(string ServiceType, INamedTypeSymbol Symbol)> VarianceCandidates) CoalesceByImplementation(
		List<RawRegistration> raw,
		List<DiagnosticInfo> diagnostics)
	{
		List<ImplInfo> implOrder = new();
		Dictionary<string, ImplInfo> implInfos = new(StringComparer.Ordinal);
		Dictionary<ServiceKey, string> serviceToImpl = new();
		HashSet<string> reportedConflicts = new(StringComparer.Ordinal);
		HashSet<string> reportedProductionConflicts = new(StringComparer.Ordinal);

		// Collection membership: every registration of a service, keyed by (service type, resolution key) and
		// deduped by implementation, kept in registration order - so an unkeyed IEnumerable<T> resolves the
		// unkeyed registrations and a [FromKey("k")] IEnumerable<T> the registrations under "k".
		// serviceMemberOrder preserves the first-seen (type, key) order for deterministic emission.
		Dictionary<ServiceKey, List<string>> serviceMembers = new();
		List<ServiceKey> serviceMemberOrder = new();

		// Variance: every unkeyed registration of a closed generic interface whose definition declares variance
		// (in/out), keyed by its fully-qualified string, in registration order. When a consumer requests a closed
		// generic interface with no exact registration, the request is redirected to a variance-compatible
		// candidate here (a registered IHandler<DomainEvent> satisfying a requested IHandler<OrderPlaced> via
		// `in T`). Keyed registrations are reached only through their key, so they are never variance-redirect
		// targets; an invariant interface can never satisfy a different closure, so it is not a candidate.
		List<(string ServiceType, INamedTypeSymbol Symbol)> varianceCandidates = new();
		HashSet<string> varianceSeen = new(StringComparer.Ordinal);

		foreach (RawRegistration registration in raw)
		{
			// Record an unkeyed closed-generic-interface registration as a variance candidate, so a
			// differently-closed consumer request can be redirected to it. Recorded even when it loses the
			// single-resolution slot to an earlier registration, since it stays reachable through its resolver.
			if (registration.Key is null
			    && registration.ServiceSymbol is { IsGenericType: true, TypeKind: TypeKind.Interface, } variantService
			    && HasDeclaredVariance(variantService)
			    && varianceSeen.Add(registration.ServiceType))
			{
				varianceCandidates.Add((registration.ServiceType, variantService));
			}

			// Setting both Factory and Instance on one attribute is contradictory; the directives are
			// mutually exclusive, so report AWT110 against the offending registration. Like AWT108/109/112,
			// this is a fault in a single registration's directives, so it names the service type (the
			// AWT107/AWT111 coalescing conflicts name the implementation instead).
			if (registration.ConflictingDirectives)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ConflictingProductionDirectives,
					LocationInfo.From(registration.Location),
					new EquatableArray<string>([Display(registration.ServiceType),])));
			}

			// The implementation's already-recorded ImplInfo (null on first sight), which ReportCoalescingConflicts
			// compares the current registration against. EnsureImpl below takes implInfos as a parameter rather
			// than capturing it, so its Add crosses a call boundary and this lookup is not misread as reading an
			// always-empty dictionary.
			implInfos.TryGetValue(registration.ImplementationType, out ImplInfo? info);

			// A lifetime (AWT107) or production (AWT111) conflict is a property of the implementation, not of any
			// single service type, so it is checked before the per-service dedup below; otherwise re-registering
			// the same service type differently would be skipped and the contradiction silently dropped.
			// Coalescing keeps the first, so the conflicting one is reported rather than ignored.
			ReportCoalescingConflicts(info, registration, reportedConflicts, reportedProductionConflicts, diagnostics);

			ServiceKey serviceKey = new(registration.ServiceType, registration.Key);
			bool alreadyChosen = serviceToImpl.TryGetValue(serviceKey, out string? existingImpl);

			// Every registration is a member of the collection for its (service type, key): an unkeyed
			// IEnumerable<T> resolves the unkeyed registrations, a [FromKey("k")] IEnumerable<T> the ones under
			// "k". A member is built even when it loses the single-resolution slot to an earlier registration,
			// since it is reachable through the collection.
			AddCollectionMember(serviceMembers, serviceMemberOrder, serviceKey, registration.ImplementationType);
			EnsureImpl(implInfos, implOrder, registration);

			if (alreadyChosen)
			{
				ReportDuplicateKey(registration, existingImpl, diagnostics);
				continue;
			}

			ImplInfo chosen = EnsureImpl(implInfos, implOrder, registration);
			serviceToImpl[serviceKey] = registration.ImplementationType;
			chosen.Services.Add(serviceKey);
		}

		return (implOrder, serviceToImpl, serviceMembers, serviceMemberOrder, varianceCandidates);

		// Creates the single ImplInfo for a registration's implementation (idempotent): the first registration seen
		// for an implementation fixes its lifetime/production, and the same instance is shared by every registration
		// of that implementation (multi-service or collection member). A collection member that is not the
		// single-resolution winner is still built here - it is reached only through the collection. Takes implInfos
		// and implOrder as parameters (rather than being a capturing local function) so the caller's pre-loop lookup
		// sees the Add across a call boundary.
		static ImplInfo EnsureImpl(Dictionary<string, ImplInfo> implInfos, List<ImplInfo> implOrder, RawRegistration reg)
		{
			if (!implInfos.TryGetValue(reg.ImplementationType, out ImplInfo? info))
			{
				info = new ImplInfo(
					reg.ImplementationType, reg.Implementation, reg.Lifetime,
					LocationInfo.From(reg.Location), reg.Production, reg.ProductionMember);
				implInfos.Add(reg.ImplementationType, info);
				implOrder.Add(info);
			}

			return info;
		}
	}

	// Reports the coalescing conflicts a re-registration of an already-seen implementation raises: a different
	// lifetime (AWT107) or a different production strategy (AWT111). Each is reported at most once per
	// implementation (the reported sets guard that), since coalescing keeps the first registration.
	private static void ReportCoalescingConflicts(
		ImplInfo? info,
		RawRegistration registration,
		HashSet<string> reportedConflicts,
		HashSet<string> reportedProductionConflicts,
		List<DiagnosticInfo> diagnostics)
	{
		if (info is null)
		{
			return;
		}

		if (info.Lifetime != registration.Lifetime && reportedConflicts.Add(registration.ImplementationType))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ConflictingLifetime,
				LocationInfo.From(registration.Location),
				new EquatableArray<string>([
					Display(registration.ImplementationType),
					info.Lifetime.ToString(),
					registration.Lifetime.ToString(),
				])));
		}

		if (ConflictsWith(info, registration) && reportedProductionConflicts.Add(registration.ImplementationType))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ConflictingProduction,
				LocationInfo.From(registration.Location),
				new EquatableArray<string>([
					Display(registration.ImplementationType),
					DescribeProduction(info.Production, info.ProductionMember),
					DescribeProduction(registration.Production, registration.ProductionMember),
				])));
		}
	}

	// Records a registration's implementation as a member of the collection for its (service type, key), in
	// registration order and deduped by implementation. serviceMemberOrder preserves first-seen (type, key) order.
	private static void AddCollectionMember(
		Dictionary<ServiceKey, List<string>> serviceMembers,
		List<ServiceKey> serviceMemberOrder,
		ServiceKey serviceKey,
		string implementationType)
	{
		if (!serviceMembers.TryGetValue(serviceKey, out List<string>? members))
		{
			members = new List<string>();
			serviceMembers.Add(serviceKey, members);
			serviceMemberOrder.Add(serviceKey);
		}

		if (!members.Contains(implementationType))
		{
			members.Add(implementationType);
		}
	}

	// AWT117: two different implementations claim the same service type and key, so a keyed resolution of
	// that key would be ambiguous. The same implementation re-registered under one key is just a coalesce
	// (first wins), and an unkeyed duplicate keeps the existing first-wins behavior, so neither is reported.
	private static void ReportDuplicateKey(RawRegistration registration, string existingImpl, List<DiagnosticInfo> diagnostics)
	{
		if (registration.Key is null || existingImpl == registration.ImplementationType)
		{
			return;
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.DuplicateKey,
			LocationInfo.From(registration.Location),
			new EquatableArray<string>([Display(registration.ServiceType), registration.Key,])));
	}

	// Two registrations of the same implementation conflict when they produce it differently: a different
	// kind (constructor vs factory vs instance), or the same kind naming a different container member.
	// Coalescing keeps the first, so the second would otherwise be dropped without a trace.
	private static bool ConflictsWith(ImplInfo info, RawRegistration registration)
		=> info.Production != registration.Production
		   || !string.Equals(info.ProductionMember, registration.ProductionMember, StringComparison.Ordinal);

	private static string DescribeProduction(ProductionKind production, string? member)
		=> production switch
		{
			ProductionKind.Factory => $"factory '{member}'",
			ProductionKind.Instance => $"instance '{member}'",
			_ => "a constructor",
		};

	// The direct-dependency graph over instance indices (resolvable edges to built instances): the edge set
	// for async-taint and captive-dependency analysis. The relationship types (Func<T>/Lazy<T>/…) and the
	// bare eager relationships (Owned<T>/Task<T>) all defer or launder, so only direct dependencies
	// contribute edges here. Cycle detection uses the wider BuildConstructionGraph instead.
	private static Dictionary<int, List<int>> BuildDependencyGraph(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers)
		=> BuildEdges(instances, serviceToImpl, implToIndex, serviceMembers, includeEagerBare: false);

	// The construction graph over instance indices: a superset of BuildDependencyGraph that additionally
	// includes the bare eager relationships Owned<T> and Task<T> (the latter also covering Task<Owned<T>>).
	// Unlike their deferred Func/Lazy wrappers - which are stored as a closure and invoked later - these
	// resolve their target during the owner's construction: synchronously for a synchronous target, and in
	// the synchronous prefix (before the first await, and before the memoized task is published) of an async
	// resolver. A cycle closed through one of them therefore re-enters an as-yet-uncached resolver and
	// overflows the stack at runtime rather than being broken, so it is reported as a dependency cycle
	// (AWT102). The narrower direct-only graph stays the edge set for taint/captive analysis, which the
	// deferrals correctly launder.
	private static Dictionary<int, List<int>> BuildConstructionGraph(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers)
		=> BuildEdges(instances, serviceToImpl, implToIndex, serviceMembers, includeEagerBare: true);

	// Builds the edge set over instance indices, keeping only the parameters that contribute an edge and that
	// resolve to a built instance. A direct dependency always contributes; the bare eager relationships
	// Owned<T> and Task<T> contribute only when <paramref name="includeEagerBare" /> is set (the construction
	// graph), since they resolve eagerly and so close cycles even though they launder async taint. A collection
	// (Enumerable) contributes an edge to each of its members in both graphs: it materializes them eagerly into
	// an array, so it captures them (taint/captive) and closes cycles through them just like a direct dependency.
	private static Dictionary<int, List<int>> BuildEdges(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		bool includeEagerBare)
	{
		Dictionary<int, List<int>> edges = new();
		for (int i = 0; i < instances.Count; i++)
		{
			List<int> nodeEdges = new();
			foreach (ParameterModel parameter in instances[i].ConstructorParameters.AsArray())
			{
				AddParameterEdges(parameter, serviceToImpl, implToIndex, serviceMembers, includeEagerBare, nodeEdges);
			}

			// An injected [Inject] member is a full graph edge just like a constructor parameter: a Direct
			// member captures its target (and, resolved at construction, closes a cycle through it), a collection
			// member edges to each of its members, and a relationship member defers - so AddParameterEdges
			// classifies it identically to a constructor edge.
			foreach (MemberModel member in instances[i].InjectedMembers.AsArray())
			{
				AddParameterEdges(member.Dependency, serviceToImpl, implToIndex, serviceMembers, includeEagerBare, nodeEdges);
			}

			edges[i] = nodeEdges;
		}

		return edges;
	}

	// Appends the edge(s) a single parameter contributes to its node's edge list. A collection - synchronous
	// (Enumerable) or asynchronous (AsyncEnumerable) - edges to each of its members; a direct dependency (and, in
	// the construction graph, a bare eager Owned<T>/Task<T>) edges to its single resolved instance; everything else
	// defers and contributes nothing. Both collection kinds materialize their members eagerly, so both capture them
	// (taint/captive) and close cycles through them; they differ only in that AsyncEnumerable awaits its members.
	// An awaited collection (AwaitedEnumerable) is the collection form of the bare Task<T>: its members are awaited
	// behind the produced task, not at the consumer's construction, so it launders their taint - but the task starts
	// materializing them during construction, so its member edges still close cycles (the construction graph only).
	private static void AddParameterEdges(
		ParameterModel parameter,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		bool includeEagerBare,
		List<int> nodeEdges)
	{
		if (parameter.Kind is DependencyKind.Enumerable or DependencyKind.AsyncEnumerable
		    || (includeEagerBare && parameter.Kind == DependencyKind.AwaitedEnumerable))
		{
			AddCollectionMemberEdges(KeyOf(parameter), serviceMembers, implToIndex, nodeEdges);
			return;
		}

		bool contributes = parameter.Kind == DependencyKind.Direct
		                   || (includeEagerBare && parameter.Kind is DependencyKind.Owned or DependencyKind.Task);
		if (contributes
		    && serviceToImpl.TryGetValue(KeyOf(parameter), out string? depImpl)
		    && implToIndex.TryGetValue(depImpl, out int depIndex))
		{
			nodeEdges.Add(depIndex);
		}
	}

	// Appends an edge to each built member of the collection reached under <paramref name="collectionKey" />
	// (its element service type plus resolution key; a member absent from implToIndex failed to build and is skipped).
	private static void AddCollectionMemberEdges(
		ServiceKey collectionKey,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, int> implToIndex,
		List<int> nodeEdges)
	{
		if (!serviceMembers.TryGetValue(collectionKey, out List<string>? members))
		{
			return;
		}

		foreach (string member in members)
		{
			if (implToIndex.TryGetValue(member, out int memberIndex))
			{
				nodeEdges.Add(memberIndex);
			}
		}
	}

	// A decorator chain link's synthetic key and identity are built from this prefix; DisplayInstance also keys
	// off it to trim the synthetic suffix out of diagnostics. Kept on the enclosing type so both the nested
	// DecoratorChainBuilder and DisplayInstance can reach it (a nested type's private member is not visible here).
	private const string DecoratorKeyPrefix = "__dec:";

	/// <summary>
	///     Builds the decorator chains after coalescing. For each decorated service it locates the base
	///     implementation(s) — every collection member, or the single-dispatch winner — and over each base impl
	///     synthesizes a chain of synthetic-keyed links: the base implementation is moved onto a synthetic key
	///     (<c>__dec:S:k:0</c>), each decorator is registered as a fresh instance whose single inner parameter is
	///     redirected to the next-lower key, and the public unkeyed winner is rewritten to the outermost decorator.
	///     The existing keyed resolver then produces <c>D2(D1(Real))</c> for free. The collection membership of the
	///     service is rewritten so a collection view yields the decorated chain, never the bare base impl (the
	///     decorator is unbypassable). Reports AWT123 (nothing to decorate) and AWT124 (no single inner parameter).
	/// </summary>
	/// <remarks>
	///     Holds the mutable graph state (the coalesced <c>serviceToImpl</c>, <c>implOrder</c>, collection
	///     membership and the decorator-inner map) as fields so the chain-building steps read as small focused
	///     methods rather than one deeply nested loop.
	/// </remarks>
	private sealed class DecoratorChainBuilder
	{
		private readonly INamedTypeSymbol _containerSymbol;
		private readonly Compilation _compilation;
		private readonly Dictionary<ServiceKey, string> _serviceToImpl;
		private readonly List<ImplInfo> _implOrder;
		private readonly Dictionary<ServiceKey, List<string>> _serviceMembers;
		private readonly Dictionary<string, DecoratorInner> _decoratorInner;
		private readonly bool _importServices;
		private readonly List<DiagnosticInfo> _diagnostics;

		// The coalesced implementations by identity, so a base impl's ImplInfo can be moved onto a synthetic key
		// and each new chain link's ImplInfo can be appended for BuildInstance to build.
		private readonly Dictionary<string, ImplInfo> _byImpl;

		public DecoratorChainBuilder(
			INamedTypeSymbol containerSymbol,
			Compilation compilation,
			CoalescedGraph graph,
			Dictionary<string, DecoratorInner> decoratorInner,
			bool importServices,
			List<DiagnosticInfo> diagnostics)
		{
			_containerSymbol = containerSymbol;
			_compilation = compilation;
			_serviceToImpl = graph.ServiceToImpl;
			_implOrder = graph.ImplOrder;
			_serviceMembers = graph.ServiceMembers;
			_decoratorInner = decoratorInner;
			_importServices = importServices;
			_diagnostics = diagnostics;

			_byImpl = new Dictionary<string, ImplInfo>(StringComparer.Ordinal);
			foreach (ImplInfo info in _implOrder)
			{
				_byImpl[info.ImplementationType] = info;
			}
		}

		public void Build(List<DecorateRegistration> decorators)
		{
			foreach ((string service, List<DecorateRegistration> chain) in GroupByService(decorators))
			{
				ProcessService(service, chain);
			}
		}

		// Group decorators by decorated service, preserving first-seen service order for determinism.
		private static List<(string Service, List<DecorateRegistration> Chain)> GroupByService(List<DecorateRegistration> decorators)
		{
			List<(string, List<DecorateRegistration>)> ordered = new();
			Dictionary<string, List<DecorateRegistration>> byService = new(StringComparer.Ordinal);
			foreach (DecorateRegistration decorator in decorators)
			{
				if (!byService.TryGetValue(decorator.Service, out List<DecorateRegistration>? list))
				{
					list = new List<DecorateRegistration>();
					byService.Add(decorator.Service, list);
					ordered.Add((decorator.Service, list));
				}

				list.Add(decorator);
			}

			return ordered;
		}

		// The base implementations to wrap: every unkeyed collection member (so the collection view is also
		// decorated), or the single-dispatch winner when the service has no collection membership.
		private static List<string> CollectBaseImpls(List<string>? members, string? winner)
		{
			if (members is { Count: > 0, })
			{
				return new List<string>(members);
			}

			return winner is null ? new List<string>() : new List<string> { winner, };
		}

		private void ProcessService(string service, List<DecorateRegistration> chain)
		{
			ServiceKey publicKey = new(service, null);
			_serviceToImpl.TryGetValue(publicKey, out string? winner);
			List<string>? members = _serviceMembers.TryGetValue(publicKey, out List<string>? m) ? m : null;
			List<string> baseImpls = CollectBaseImpls(members, winner);

			// AWT123: the decorated service has no registration to wrap.
			if (baseImpls.Count == 0)
			{
				Report(Diagnostics.DecoratedServiceNotRegistered, chain[0].Location,
					service, chain[0].Decorator.ToDisplayString(FullyQualified));
				return;
			}

			// Order the chain by (Order, declaration) — innermost first, outermost last.
			List<DecorateRegistration> ordered = chain
				.OrderBy(d => d.Order)
				.ThenBy(d => d.DeclarationOrder)
				.ToList();

			// AWT124: each decorator must have exactly one constructor parameter assignable to the service.
			List<string>? innerParameterTypes = ResolveInnerParameterTypes(service, ordered);
			if (innerParameterTypes is null)
			{
				return;
			}

			ServiceChain sc = new(service, winner, members, ordered, innerParameterTypes);
			for (int k = 0; k < baseImpls.Count; k++)
			{
				WrapBaseImpl(sc, baseImpls[k], k);
			}
		}

		// Each decorator's inner-parameter type in chain order, or null (having reported AWT124, or AWT135 when
		// the would-be inner is marked [FromServices]) when any decorator has no single constructor parameter
		// that can receive the inner instance.
		private List<string>? ResolveInnerParameterTypes(string service, List<DecorateRegistration> ordered)
		{
			List<string> innerParameterTypes = new();
			bool valid = true;
			foreach (DecorateRegistration decorator in ordered)
			{
				string? innerType = SingleInnerParameterType(decorator.Decorator, decorator.ServiceSymbol, out IParameterSymbol? externalInner);
				if (innerType is null)
				{
					// The would-be inner is marked [FromServices]: point at the offending parameter (falling back
					// to the [Decorate] registration when its location is unavailable) rather than reporting the
					// generic missing-inner AWT124, whose "add a parameter" guidance would mislead here.
					if (externalInner is not null)
					{
						Report(Diagnostics.ExternalDecoratorInner,
							externalInner.Locations.FirstOrDefault() ?? decorator.Location,
							externalInner.Name, decorator.Decorator.ToDisplayString(FullyQualified));
					}
					else
					{
						Report(Diagnostics.DecoratorMissingInnerParameter, decorator.Location,
							decorator.Decorator.ToDisplayString(FullyQualified), service);
					}

					valid = false;
					continue;
				}

				innerParameterTypes.Add(innerType);
			}

			return valid ? innerParameterTypes : null;
		}

		private void WrapBaseImpl(ServiceChain chain, string baseImpl, int baseIndex)
		{
			string service = chain.Service;
			bool isWinnerChain = baseImpl == chain.Winner;

			// Move the base implementation off the public service onto the chain's lowest synthetic key, so the
			// first decorator reaches it by key and the public dispatch no longer hits it directly.
			string baseKey = DecoratorKey(service, baseIndex, 0);
			MoveBaseToSyntheticKey(service, baseImpl, baseKey);

			ImplInfo? baseInfo = _byImpl.TryGetValue(baseImpl, out ImplInfo? bi) ? bi : null;
			Lifetime lifetime = baseInfo?.Lifetime ?? Lifetime.Transient;
			LocationInfo? location = baseInfo?.Location;

			// Register each decorator as a fresh instance with a synthetic identity (so one decorator type can
			// wrap several base impls as distinct instances), inheriting the base registration's lifetime.
			string innerKey = baseKey;
			string outermostIdentity = baseImpl;
			for (int i = 0; i < chain.Ordered.Count; i++)
			{
				bool isPublic = isWinnerChain && i == chain.Ordered.Count - 1;
				outermostIdentity = AddChainLink(chain, baseIndex, i, isPublic, lifetime, location, innerKey);
				innerKey = DecoratorKey(service, baseIndex, i + 1);
			}

			// Collection membership is unbypassable: replace the base impl with the chain's outermost decorator
			// identity, so a collection view yields the full chain as a single element rather than the bare impl.
			RewriteCollectionMembership(chain.Members, baseImpl, outermostIdentity);
		}

		// Moves the base implementation off the public service onto the chain's lowest synthetic key.
		private void MoveBaseToSyntheticKey(string service, string baseImpl, string baseKey)
		{
			if (_byImpl.TryGetValue(baseImpl, out ImplInfo? baseInfo))
			{
				baseInfo.Services.Remove(new ServiceKey(service, null));
				ServiceKey synthetic = new(service, baseKey);
				if (!baseInfo.Services.Contains(synthetic))
				{
					baseInfo.Services.Add(synthetic);
				}
			}

			_serviceToImpl[new ServiceKey(service, baseKey)] = baseImpl;
		}

		// Registers one chain link (the decorator at <paramref name="linkIndex" />) and returns its synthetic
		// identity. The outermost link of the winner chain (<paramref name="isPublic" />) takes the public
		// service key; every other link is keyed so it is reached only as the inner of the link above it (or as
		// a rewritten collection member).
		private string AddChainLink(ServiceChain chain, int baseIndex, int linkIndex, bool isPublic, Lifetime lifetime, LocationInfo? location, string innerKey)
		{
			string service = chain.Service;
			int link = linkIndex + 1;
			INamedTypeSymbol decorator = chain.Ordered[linkIndex].Decorator;
			string identity = DecoratorIdentity(decorator.ToDisplayString(FullyQualified), service, baseIndex, link);

			ImplInfo info = new(identity, decorator, lifetime, location, ProductionKind.Constructor, null);
			_byImpl[identity] = info;
			_implOrder.Add(info);

			ServiceKey ownKey = isPublic
				? new ServiceKey(service, null)
				: new ServiceKey(service, DecoratorKey(service, baseIndex, link));
			info.Services.Add(ownKey);
			_serviceToImpl[ownKey] = identity;

			// Redirect this link's inner parameter to the link below it. The chain links are all registered under
			// the decorated service type, so the redirect keys to (service, innerKey) - not to the parameter's own
			// declared type, which may be a base of the service and is not a registration key.
			_decoratorInner[identity] = new DecoratorInner(chain.InnerParameterTypes[linkIndex], service, innerKey);

			return identity;
		}

		private static void RewriteCollectionMembership(List<string>? members, string baseImpl, string outermostIdentity)
		{
			int index = members?.IndexOf(baseImpl) ?? -1;
			if (index >= 0)
			{
				members![index] = outermostIdentity;
			}
		}

		private void Report(DiagnosticDescriptor descriptor, Location? location, params string[] messageArgs)
		{
			string[] displayed = new string[messageArgs.Length];
			for (int i = 0; i < messageArgs.Length; i++)
			{
				displayed[i] = Display(messageArgs[i]);
			}

			_diagnostics.Add(new DiagnosticInfo(descriptor, LocationInfo.From(location), new EquatableArray<string>(displayed)));
		}

		// The per-service state threaded through WrapBaseImpl / AddChainLink: the decorated service, its
		// single-dispatch winner (if any), its collection membership (if any), the decorators innermost-first,
		// and each decorator's inner-parameter type (positionally matching Ordered).
		private readonly record struct ServiceChain(
			string Service,
			string? Winner,
			List<string>? Members,
			List<DecorateRegistration> Ordered,
			List<string> InnerParameterTypes);

		/// <summary>
		///     The fully-qualified type of a decorator's single constructor parameter that receives the inner
		///     instance, or <see langword="null" /> when there is none or it is ambiguous (AWT124) - or when the
		///     would-be inner is marked <c>[FromServices]</c>, yielded through
		///     <paramref name="externalInner" /> so the caller reports the specific conflict (AWT135) instead. The
		///     constructor is chosen by the same <see cref="SelectConstructor" /> the container uses to build the
		///     decorator, so this validation can never inspect a different constructor than the one constructed - a
		///     divergence would leave the inner parameter un-redirected and the link resolving itself. The returned
		///     type string is what <see cref="ClassifyParameter" /> produces for that parameter, so the
		///     inner-parameter redirect in <see cref="ClassifyParameters" /> can match it.
		/// </summary>
		private string? SingleInnerParameterType(INamedTypeSymbol decorator, INamedTypeSymbol service, out IParameterSymbol? externalInner)
		{
			externalInner = null;
			IMethodSymbol? constructor = SelectConstructor(decorator, _containerSymbol, _serviceToImpl.Keys.Select(k => k.Service), importServices: _importServices);
			if (constructor is null)
			{
				return null;
			}

			// The inner is an instance of the decorated service, so its parameter must accept it: the service is
			// implicitly convertible to the parameter's type (the parameter is the service, or a base of it). A
			// [FromKey] parameter is excluded - it deliberately selects a specific keyed registration, so it is a
			// separate dependency, never the chain inner (which is redirected by key and would ignore the [FromKey]
			// anyway). A [FromServices] parameter is excluded for the same reason: it deliberately resolves from
			// the external provider, so it is a separate dependency, never the chain inner. More than one parameter
			// can be assignable at once - e.g. a plain `object` state parameter alongside the inner - so the inner
			// is the most-derived of them: the one every other assignable parameter is a base of. Exactly one such
			// maximum makes the inner unambiguous; a tie (two equally-derived assignable parameters, e.g. two
			// `IService`) is genuinely ambiguous and reported as AWT124, as is a decorator whose only
			// service-assignable parameter is [FromKey]-ed.
			List<IParameterSymbol> assignable = constructor.Parameters
				.Where(p => FromKey(p.GetAttributes()) is null && !HasFromServices(p) && _compilation.HasImplicitConversion(service, p.Type))
				.ToList();

			// AWT135: nothing is left to receive the inner instance, but a [FromServices] parameter of the
			// service is present - the would-be inner was marked external, which would silently bypass the
			// decorator chain. Yield it so the caller reports that specific conflict instead of a generic AWT124.
			if (assignable.Count == 0)
			{
				externalInner = constructor.Parameters
					.FirstOrDefault(p => HasFromServices(p) && _compilation.HasImplicitConversion(service, p.Type));
				return null;
			}

			IParameterSymbol? inner = null;
			foreach (IParameterSymbol candidate in assignable)
			{
				bool isMaximum = assignable.All(other =>
					ReferenceEquals(other, candidate) || _compilation.HasImplicitConversion(candidate.Type, other.Type));
				if (!isMaximum)
				{
					continue;
				}

				if (inner is not null)
				{
					return null;
				}

				inner = candidate;
			}

			return inner?.Type.ToDisplayString(FullyQualified);
		}

		private static string DecoratorKey(string service, int baseIndex, int link)
			=> $"{DecoratorKeyPrefix}{service}:{baseIndex}:{link}";

		private static string DecoratorIdentity(string decoratorType, string service, int baseIndex, int link)
			=> $"{decoratorType}@{DecoratorKeyPrefix}{service}:{baseIndex}:{link}";
	}

	/// <summary>
	///     Builds the composites after coalescing (and after decorator chains, so a composite fronts the
	///     decorated members). For each <c>[Composite&lt;TComposite, TService&gt;]</c> it registers the composite
	///     as an ordinary instance (constructed, cached and disposed by index) with the chosen lifetime and
	///     rewrites the public unkeyed winner of <c>TService</c> to the composite, so a plain <c>TService</c>
	///     parameter and <c>Resolve&lt;TService&gt;()</c> both get the composite. The composite is deliberately
	///     NOT added to <c>serviceMembers</c>: it is excluded from its own - and everyone else's - collection
	///     membership, so its own collection parameter (and any separate <c>IEnumerable&lt;TService&gt;</c>
	///     consumer) resolves to the OTHER registrations, never the composite. With no self-edge the cycle
	///     (AWT102) and captive (AWT105) analysis works unchanged over the composite's eager collection edges. An
	///     empty member set is legal (the composite fans out to an empty collection). Reports AWT130 when the
	///     composite has no collection parameter of the composed service, AWT133 when that collection is of a base
	///     type rather than the service itself, AWT132 for a second composite over an already-composed service, and
	///     AWT131 (a warning) when the composite type is also registered as a bare member of its own service - in
	///     which case that membership is dropped, keeping the no-self-edge invariant that would otherwise be
	///     violated (and surface as a confusing AWT102 cycle).
	/// </summary>
	private static void BuildComposites(
		List<CompositeRegistration> composites,
		Compilation compilation,
		INamedTypeSymbol containerSymbol,
		CoalescedGraph graph,
		bool importServices,
		List<DiagnosticInfo> diagnostics)
	{
		(Dictionary<ServiceKey, string> serviceToImpl, List<ImplInfo> implOrder, Dictionary<ServiceKey, List<string>> serviceMembers) = graph;

		// The coalesced implementations by identity (including decorator chain links), so the composite's
		// ImplInfo can be appended for BuildInstance to build and a former winner's public key removed.
		Dictionary<string, ImplInfo> byImpl = new(StringComparer.Ordinal);
		foreach (ImplInfo info in implOrder)
		{
			byImpl[info.ImplementationType] = info;
		}

		// The composite type chosen for each composed service, so a second composite for the same service is
		// caught (AWT132) rather than silently overwriting the first and leaving it as a dead built instance.
		Dictionary<string, string> compositeByService = new(StringComparer.Ordinal);

		foreach (CompositeRegistration composite in composites)
		{
			string compositeType = composite.Composite.ToDisplayString(FullyQualified);

			// AWT132: a service can have at most one composite façade; a later composite for it is skipped.
			if (IsDuplicateComposite(composite, compositeType, compositeByService, diagnostics))
			{
				continue;
			}

			// AWT130/AWT133: the composite must fan out over a collection of exactly the composed service.
			if (!ValidateCompositeCollection(composite, compositeType, containerSymbol, compilation, serviceToImpl, importServices, diagnostics))
			{
				continue;
			}

			ImplInfo compositeInfo = EnsureCompositeInstance(composite, compositeType, byImpl, implOrder);
			DropRedundantSelfMembership(composite, compositeType, serviceMembers, diagnostics);
			MakeCompositeThePublicWinner(composite, compositeType, compositeInfo, serviceToImpl, byImpl);
		}
	}

	// AWT132: whether this composite duplicates one already chosen for its service (so the caller skips it),
	// recording the first composite per service. A second composite of a DIFFERENT type is reported; the same
	// type declared twice is idempotent and left silent.
	private static bool IsDuplicateComposite(
		CompositeRegistration composite,
		string compositeType,
		Dictionary<string, string> compositeByService,
		List<DiagnosticInfo> diagnostics)
	{
		if (!compositeByService.TryGetValue(composite.Service, out string? firstComposite))
		{
			compositeByService.Add(composite.Service, compositeType);
			return false;
		}

		if (firstComposite != compositeType)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.MultipleCompositesForService,
				LocationInfo.From(composite.Location),
				new EquatableArray<string>([Display(composite.Service),])));
		}

		return true;
	}

	// Whether the composite's collection parameter is valid (the caller proceeds), reporting AWT130 for a missing
	// collection parameter and AWT133 for a collection of a base type of the service (which, since collections are
	// keyed by exact element type, would resolve a different collection than the composed service's registrations).
	private static bool ValidateCompositeCollection(
		CompositeRegistration composite,
		string compositeType,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		Dictionary<ServiceKey, string> serviceToImpl,
		bool importServices,
		List<DiagnosticInfo> diagnostics)
	{
		switch (ClassifyCompositeCollection(composite.Composite, composite.ServiceSymbol, containerSymbol, compilation, serviceToImpl, importServices, out string? relatedElement))
		{
			case CompositeCollectionKind.Missing:
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.CompositeMissingCollectionParameter,
					LocationInfo.From(composite.Location),
					new EquatableArray<string>([
						Display(compositeType),
						Display(composite.Service),
					])));
				return false;

			case CompositeCollectionKind.RelatedElement:
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.CompositeCollectionNotOfComposedService,
					LocationInfo.From(composite.Location),
					new EquatableArray<string>([
						Display(compositeType),
						Display(relatedElement!),
						Display(composite.Service),
					])));
				return false;

			default:
				return true;
		}
	}

	// Registers the composite as an ordinary instance (constructed, cached and disposed by index) and returns its
	// ImplInfo, reusing the existing one if the type was already registered (idempotent when named twice or when
	// the composite type is also a normal service).
	private static ImplInfo EnsureCompositeInstance(
		CompositeRegistration composite,
		string compositeType,
		Dictionary<string, ImplInfo> byImpl,
		List<ImplInfo> implOrder)
	{
		if (byImpl.TryGetValue(compositeType, out ImplInfo? compositeInfo))
		{
			return compositeInfo;
		}

		compositeInfo = new ImplInfo(
			compositeType, composite.Composite, composite.Lifetime,
			LocationInfo.From(composite.Location), ProductionKind.Constructor, null);
		byImpl.Add(compositeType, compositeInfo);
		implOrder.Add(compositeInfo);
		return compositeInfo;
	}

	// AWT131: the composite type is also registered as a bare member of the service it composes (e.g. a
	// [Transient<C, S>] alongside [Composite<C, S>]). A composite is excluded from its own fan-out, so drop it from
	// every collection of the composed service and warn - without the removal its own collection edge would include
	// itself and surface as a confusing AWT102 dependency cycle.
	private static void DropRedundantSelfMembership(
		CompositeRegistration composite,
		string compositeType,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		List<DiagnosticInfo> diagnostics)
	{
		bool wasMember = false;
		foreach (KeyValuePair<ServiceKey, List<string>> entry in serviceMembers)
		{
			if (entry.Key.Service == composite.Service && entry.Value.Remove(compositeType))
			{
				wasMember = true;
			}
		}

		if (!wasMember)
		{
			return;
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.CompositeAlsoRegisteredAsMember,
			LocationInfo.From(composite.Location),
			new EquatableArray<string>([
				Display(compositeType),
				Display(composite.Service),
			])));
	}

	// Makes the composite the public single-dispatch winner: takes the unkeyed service off whatever impl currently
	// holds it (a former winner stays a collection member, just no longer the façade) and hands it to the composite.
	// The composite is never a serviceMembers entry, so it stays excluded from its own - and every other consumer's -
	// IEnumerable<TService>.
	private static void MakeCompositeThePublicWinner(
		CompositeRegistration composite,
		string compositeType,
		ImplInfo compositeInfo,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, ImplInfo> byImpl)
	{
		ServiceKey publicKey = new(composite.Service, null);
		if (serviceToImpl.TryGetValue(publicKey, out string? previousWinner)
		    && previousWinner != compositeType
		    && byImpl.TryGetValue(previousWinner, out ImplInfo? previousInfo))
		{
			previousInfo.Services.Remove(publicKey);
		}

		serviceToImpl[publicKey] = compositeType;
		if (!compositeInfo.Services.Contains(publicKey))
		{
			compositeInfo.Services.Add(publicKey);
		}
	}

	/// <summary>Which collection-parameter shape a composite offers for its composed service.</summary>
	private enum CompositeCollectionKind
	{
		/// <summary>A collection parameter whose element type is exactly the composed service - valid.</summary>
		Exact,

		/// <summary>
		///     A collection parameter of a base (or otherwise related) type of the composed service. Collections
		///     resolve by exact element type, so it would fan out over a different collection - reported as AWT133.
		/// </summary>
		RelatedElement,

		/// <summary>No collection parameter of the composed service at all - reported as AWT130.</summary>
		Missing,
	}

	/// <summary>
	///     Classifies the collection constructor parameter <paramref name="composite" /> offers for its composed
	///     <paramref name="service" /> - what it fans out over. <see cref="CompositeCollectionKind.Exact" /> when a
	///     collection parameter (<c>IEnumerable&lt;TService&gt;</c>, <c>IReadOnlyList&lt;TService&gt;</c>,
	///     <c>TService[]</c>, …) has element type exactly the service; <see cref="CompositeCollectionKind.RelatedElement" />
	///     (yielding the offending element type in <paramref name="relatedElement" />) when a collection parameter's
	///     element is a base type the service is assignable to but is not the service itself - collections resolve
	///     by exact element type, so it would fan out over a different collection; otherwise
	///     <see cref="CompositeCollectionKind.Missing" />. An exact match wins over a related one. The constructor
	///     is chosen by the same <see cref="SelectConstructor" /> the container builds the composite through, so
	///     this validation can never inspect a different constructor than the one constructed.
	/// </summary>
	private static CompositeCollectionKind ClassifyCompositeCollection(
		INamedTypeSymbol composite,
		INamedTypeSymbol service,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		Dictionary<ServiceKey, string> serviceToImpl,
		bool importServices,
		out string? relatedElement)
	{
		relatedElement = null;
		IMethodSymbol? constructor = SelectConstructor(composite, containerSymbol, serviceToImpl.Keys.Select(k => k.Service), importServices: importServices);
		if (constructor is null)
		{
			return CompositeCollectionKind.Missing;
		}

		foreach (IParameterSymbol parameter in constructor.Parameters)
		{
			if (!TryGetCompositeCollectionElement(parameter.Type, out ITypeSymbol? element))
			{
				continue;
			}

			if (SymbolEqualityComparer.Default.Equals(element, service))
			{
				return CompositeCollectionKind.Exact;
			}

			if (relatedElement is null && compilation.HasImplicitConversion(service, element!))
			{
				relatedElement = element!.ToDisplayString(FullyQualified);
			}
		}

		return relatedElement is null ? CompositeCollectionKind.Missing : CompositeCollectionKind.RelatedElement;
	}

	/// <summary>
	///     The element type of a collection parameter (the rank-1 array element, or the single type argument of
	///     one of the standard generic collection interfaces), mirroring <see cref="TryGetCollectionElement" />
	///     but yielding the element symbol so the composed service's convertibility to it can be checked.
	/// </summary>
	private static bool TryGetCompositeCollectionElement(ITypeSymbol type, out ITypeSymbol? element)
	{
		if (type is IArrayTypeSymbol { Rank: 1, } array)
		{
			element = array.ElementType;
			return true;
		}

		if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
		    && named.Name is "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection")
		{
			element = named.TypeArguments[0];
			return true;
		}

		element = null;
		return false;
	}

	private static InstanceModel? BuildInstance(ImplInfo info, BuildContext context)
	{
		INamedTypeSymbol containerSymbol = context.ContainerSymbol;
		Compilation compilation = context.Compilation;
		Dictionary<ServiceKey, string> serviceToImpl = context.ServiceToImpl;
		WellKnownTypes wellKnown = context.WellKnown;
		List<DiagnosticInfo> diagnostics = context.Diagnostics;

		// A pre-built Instance is handed back from a container member, never constructed here. The
		// container does not own it, so it is not disposed; the registered type may legitimately be an
		// interface (so the not-instantiable check is skipped) and it contributes no graph edges.
		//
		// It is likewise never async-initialized: a pre-built Instance implementing IAsyncInitializable is
		// NOT awaited by the container and is NOT async-tainted, so it stays synchronously resolvable and is
		// handed out without InitializeAsync ever running. This mirrors the disposal contract above - a
		// pre-built instance is the caller's to construct, initialize and own; the container only hands back
		// what the member produced. A service that needs the container to drive its asynchronous
		// initialization must be registered for construction (or via a Factory whose concrete return type
		// implements IAsyncInitializable), not as a pre-built Instance.
		if (info.Production == ProductionKind.Instance)
		{
			ValidateInstanceMember(containerSymbol, info, compilation, diagnostics);
			return new InstanceModel(
				info.ImplementationType,
				info.Symbol.Name,
				info.Lifetime,
				new EquatableArray<ServiceKey>(info.Services.ToArray()),
				new EquatableArray<ParameterModel>([]),
				false,
				info.Symbol.IsReferenceType,
				ProductionKind.Instance,
				info.ProductionMember);
		}

		// Select the producer: a container method (Factory) or the implementation's constructor (the
		// default). A null result means the registration is unusable and a diagnostic was already reported.
		IMethodSymbol? producer = SelectProducer(info, containerSymbol, compilation, serviceToImpl, context.ImportServices, diagnostics);
		if (producer is null)
		{
			return null;
		}

		// An asynchronous factory returns Task<T> / ValueTask<T>: the container awaits it, so the type it
		// actually owns is the awaited result T, not the Task. A synchronous factory owns its return type
		// directly, and a constructed implementation owns info.Symbol.
		bool asyncFactory = info.Production == ProductionKind.Factory
		                    && ContainerRegistrations.IsAsyncFactoryReturn(producer.ReturnType, compilation, out _);

		// A factory's parameters resolve from the graph exactly like a constructor's. An async factory
		// additionally forwards the resolve-time CancellationToken (the async creator's) into a matching
		// parameter rather than resolving it from the graph.
		List<ParameterModel> parameters = ClassifyParameters(producer, info, asyncFactory, context);

		// Property injection: after the constructor, fill opt-in [Inject] properties through an object
		// initializer. Only a constructed instance is filled - a factory or pre-built instance is produced
		// whole by its source. Each member edge is classified exactly like a Direct constructor parameter, so
		// it participates fully in cycle, captive and async-taint analysis.
		List<MemberModel> members = new();
		if (info.Production == ProductionKind.Constructor)
		{
			DiscoverInjectedMembers(info, containerSymbol, serviceToImpl, context.ConstraintRejected, members, diagnostics);
		}

		// Disposability follows the type the container actually owns: a factory's produced type (which may
		// implement IDisposable behind a non-disposable service interface; for an async factory this is the
		// awaited T, not the Task), or the constructed implementation type. Using info.Symbol for a factory
		// would miss a DisposableX behind an IX and leak it.
		ITypeSymbol disposalType = info.Production == ProductionKind.Factory
			? ContainerRegistrations.ProducedType(producer.ReturnType, compilation)
			: info.Symbol;
		bool disposable = wellKnown.Disposable is not null && ImplementsInterface(disposalType, wellKnown.Disposable);

		// Async disposal mirrors synchronous disposal: the container owns an IAsyncDisposable instance for
		// teardown too, and the drain awaits its DisposeAsync, preferring it over IDisposable when a type is
		// both. This is recognized only when the runtime exposes async disposal at all - otherwise the generated
		// container stays synchronous-dispose only.
		bool asyncDisposable = wellKnown.AsyncDisposable is not null && ImplementsInterface(disposalType, wellKnown.AsyncDisposable);

		// A factory's declared return type can hide a concrete IDisposable (or IAsyncDisposable) behind a
		// non-disposable service interface (or base class), which the static flags above miss. When that is
		// possible - the declared type is itself neither yet a subtype could be (an interface or a non-sealed
		// class) - the emitter tracks the realized instance for disposal behind a runtime
		// `is IDisposable or IAsyncDisposable` test instead. A sealed declared type that is neither cannot hide
		// one, so it needs no check (and the runtime test would not even compile). Constructed and pre-built
		// Instance production never lie: info.Symbol is the concrete type, and an Instance is not owned.
		bool runtimeDisposalCheck = info.Production == ProductionKind.Factory
		                            && !disposable
		                            && !asyncDisposable
		                            && CouldHideDisposable(disposalType);

		// Async initialization follows the type the container actually owns - a factory's concrete return type
		// (which may implement IAsyncInitializable behind a non-async service interface) or the constructed
		// implementation type - mirroring the disposal-type choice above. A pre-built Instance is returned
		// early above and is never initialized here (the caller owns it). An async factory is async-tainted
		// regardless of whether its produced type implements IAsyncInitializable: its result is reached only by
		// awaiting the Task (see the IsAsyncFactory seed in PropagateAsyncTaint). When the produced type IS
		// IAsyncInitializable, the container additionally awaits its InitializeAsync after the factory completes.
		bool asyncInit = wellKnown.AsyncInitializable is not null && ImplementsInterface(disposalType, wellKnown.AsyncInitializable);

		// Best-effort lint (AWT106): a synchronous factory whose declared return type hides the asynchronous
		// initialization its body provably produces. The container reads async-init taint off producer.ReturnType
		// (above), so a concrete IAsyncInitializable returned behind a plainer interface is never initialized.
		// An async Task<T>/ValueTask<T> factory owns its own initialization (the container awaits the factory),
		// and a hidden IDisposable is disposed at runtime via RuntimeDisposalCheck - neither is reported.
		if (info.Production == ProductionKind.Factory && !asyncFactory)
		{
			ReportFactoryHidingAsyncInitialization(producer, compilation, wellKnown.AsyncInitializable, diagnostics);
		}

		// A decorator chain link carries a synthetic ImplementationType identity (so one decorator type can be
		// several distinct instances); the real type to construct is then its symbol, kept apart in EmitType.
		string realType = info.Symbol.ToDisplayString(FullyQualified);
		string? emitType = info.ImplementationType == realType ? null : realType;

		return new InstanceModel(
			info.ImplementationType,
			info.Symbol.Name,
			info.Lifetime,
			new EquatableArray<ServiceKey>(info.Services.ToArray()),
			new EquatableArray<ParameterModel>(parameters.ToArray()),
			disposable,
			info.Symbol.IsReferenceType,
			info.Production,
			info.ProductionMember,
			asyncInit,
			IsAsyncFactory: asyncFactory,
			RuntimeDisposalCheck: runtimeDisposalCheck,
			IsAsyncDisposable: asyncDisposable,
			EmitType: emitType,
			InjectedMembers: new EquatableArray<MemberModel>(members.ToArray()));

		static bool ImplementsInterface(ITypeSymbol type, INamedTypeSymbol @interface)
		{
			return SymbolEqualityComparer.Default.Equals(type, @interface)
			       || type.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, @interface));
		}

		// Whether a value of this declared type could be IDisposable at runtime through a subtype the
		// declaration does not reveal: an interface or type parameter (any implementer qualifies) or a
		// non-sealed class (a derived type may implement it). A sealed class or a struct that does not
		// itself implement IDisposable cannot, so a runtime `is IDisposable` test against it is pointless
		// (and, for a sealed class, a compile error - CS8121/CS0184).
		static bool CouldHideDisposable(ITypeSymbol type)
			=> type.TypeKind is TypeKind.Interface or TypeKind.TypeParameter
			   || (type.TypeKind == TypeKind.Class && !type.IsSealed);
	}

	/// <summary>
	///     Selects the method that produces an implementation: a container method for a <c>Factory</c>
	///     registration, or the implementation's own constructor otherwise. Returns <see langword="null" />
	///     when the registration is unusable - an unresolved factory (AWT108), a non-instantiable abstract or
	///     interface type (AWT103), or a type with no accessible constructor (AWT104) - having already appended
	///     the corresponding diagnostic. A factory produces the instance, so the registered type may be an
	///     interface and is not subject to the not-instantiable check a constructed type is.
	/// </summary>
	private static IMethodSymbol? SelectProducer(
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		Dictionary<ServiceKey, string> serviceToImpl,
		bool importServices,
		List<DiagnosticInfo> diagnostics)
	{
		if (info.Production == ProductionKind.Factory)
		{
			return ResolveFactory(containerSymbol, info, compilation, diagnostics);
		}

		// An abstract type or interface cannot be constructed; reject it instead of emitting a 'new'
		// against it (which would fail to compile in the generated source).
		if (info.Symbol.IsAbstract || info.Symbol.TypeKind == TypeKind.Interface)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NotInstantiable,
				info.Location,
				new EquatableArray<string>([Display(info.ImplementationType),])));
			return null;
		}

		IMethodSymbol? constructor = SelectConstructor(info.Symbol, containerSymbol, serviceToImpl.Keys.Select(k => k.Service), importServices: importServices);
		if (constructor is null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NoAccessibleConstructor,
				info.Location,
				new EquatableArray<string>([Display(info.ImplementationType),])));
		}

		return constructor;
	}

	/// <summary>
	///     Reports <see cref="Diagnostics.FactoryHidesAsyncInitialization">AWT106</see> when a synchronous
	///     factory method's body provably returns a concrete type that implements <c>IAsyncInitializable</c>
	///     while the method's declared return type does not - so the initialization is invisible to the
	///     container and never runs.
	/// </summary>
	/// <remarks>
	///     Conservative by design: it inspects only the producer's own <c>return</c> expressions (both
	///     expression-bodied and block-bodied), never descending into nested lambdas or local functions, and
	///     fires only when the statically determined type of the returned expression is a non-abstract,
	///     non-interface named type that is async-initializable. A metadata-only factory (no syntax) or an
	///     unresolved/unanalyzable return type yields no diagnostic. False negatives (helper-returned or
	///     runtime-selected implementations) are accepted; false positives are not. A hidden <c>IDisposable</c>
	///     is not reported (the container disposes factory outputs behind a runtime check); an asynchronous
	///     factory is excluded by the caller (it owns its own initialization).
	/// </remarks>
	private static void ReportFactoryHidingAsyncInitialization(
		IMethodSymbol producer,
		Compilation compilation,
		INamedTypeSymbol? asyncInitializableSymbol,
		List<DiagnosticInfo> diagnostics)
	{
		if (asyncInitializableSymbol is null)
		{
			return;
		}

		ITypeSymbol declaredReturnType = producer.ReturnType;

		// The container already sees the initialization when the declared return type is itself
		// async-initializable, so nothing it hides could be missed - there is no diagnostic to report.
		if (Implements(declaredReturnType, asyncInitializableSymbol))
		{
			return;
		}

		// Already-reported concrete types: a factory with several returns of the same hidden type should
		// surface a single diagnostic, not one per return.
		HashSet<ITypeSymbol> reported = new(SymbolEqualityComparer.Default);

		foreach (SyntaxReference reference in producer.DeclaringSyntaxReferences)
		{
			// A factory must be a method on the container; anything else (or metadata-only, no syntax) is
			// not analyzable here and is left silent.
			if (reference.GetSyntax() is not MethodDeclarationSyntax method)
			{
				continue;
			}

			SemanticModel model = compilation.GetSemanticModel(method.SyntaxTree);

			foreach (ExpressionSyntax returnExpression in CollectReturnExpressions(method))
			{
				ITypeSymbol? returnedType = model.GetTypeInfo(returnExpression).Type;
				if (returnedType is not INamedTypeSymbol concrete
				    || concrete.TypeKind == TypeKind.Interface
				    || concrete.IsAbstract
				    || concrete.TypeKind == TypeKind.Error
				    || !reported.Add(concrete)
				    || !Implements(concrete, asyncInitializableSymbol))
				{
					continue;
				}

				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.FactoryHidesAsyncInitialization,
					LocationInfo.From(returnExpression.GetLocation()),
					new EquatableArray<string>([
						producer.Name,
						Display(concrete.ToDisplayString(FullyQualified)),
						Display(declaredReturnType.ToDisplayString(FullyQualified)),
					])));
			}
		}

		static bool Implements(ITypeSymbol type, INamedTypeSymbol @interface)
		{
			return SymbolEqualityComparer.Default.Equals(type, @interface)
			       || type.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, @interface));
		}
	}

	/// <summary>
	///     The expressions a method directly returns: the arrow expression of an expression-bodied method, or
	///     every <c>return x;</c> in a block body. Nested lambdas and local functions are not descended into,
	///     so their returns are never attributed to the enclosing factory.
	/// </summary>
	private static IEnumerable<ExpressionSyntax> CollectReturnExpressions(MethodDeclarationSyntax method)
	{
		if (method.ExpressionBody?.Expression is { } arrow)
		{
			yield return arrow;
			yield break;
		}

		if (method.Body is null)
		{
			yield break;
		}

		Stack<SyntaxNode> pending = new();
		pending.Push(method.Body);
		while (pending.Count > 0)
		{
			SyntaxNode node = pending.Pop();
			foreach (SyntaxNode child in node.ChildNodes())
			{
				// Do not cross into a nested function: its returns belong to it, not to the factory.
				if (child is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
				{
					continue;
				}

				if (child is ReturnStatementSyntax { Expression: { } returned })
				{
					yield return returned;
				}

				pending.Push(child);
			}
		}
	}

	/// <summary>
	///     Redirects a decorator chain link's single inner parameter (a direct, unkeyed dependency assignable to
	///     the decorated service type) to the synthetic key of the next-lower link - so the keyed resolver
	///     produces <c>D2(D1(Real))</c> rather than the link resolving its own public service (itself) and
	///     recursing. Both the service type and key are rewritten: the links are registered under the decorated
	///     service type, so a parameter declared as a base of the service still resolves the link (its own
	///     declared type is not a registration key). A non-link instance, or any other parameter, is returned
	///     unchanged and resolves from the graph.
	/// </summary>
	private static ParameterModel RedirectDecoratorInner(
		ParameterModel parameterModel,
		ImplInfo info,
		Dictionary<string, DecoratorInner> decoratorInner)
	{
		if (parameterModel is { Kind: DependencyKind.Direct, Key: null, }
		    && decoratorInner.TryGetValue(info.ImplementationType, out DecoratorInner inner)
		    && parameterModel.ServiceType == inner.ParameterServiceType)
		{
			return parameterModel with { ServiceType = inner.ServiceType, Key = inner.InnerKey, };
		}

		return parameterModel;
	}

	/// <summary>
	///     Rewrites a collection parameter to an ordinary direct dependency when its collection shape is claimed by
	///     an explicit registration. A collection type (<c>IEnumerable&lt;T&gt;</c> and friends, <c>T[]</c>, or
	///     <c>IAsyncEnumerable&lt;T&gt;</c>) is normally synthesized from the registrations of its element type under
	///     the parameter's key. But if that collection shape is itself registered as a service under the same key - a
	///     legitimate opaque value such as a <c>string[]</c> of command-line arguments, an
	///     <c>IReadOnlyList&lt;T&gt;</c> of config or an <c>IAsyncEnumerable&lt;T&gt;</c> channel - synthesis steps
	///     aside entirely (all-or-nothing): the registered shape resolves to that opaque value as an ordinary direct
	///     dependency, and an unregistered sibling shape is a plain missing dependency (AWT101) rather than a
	///     silently synthesized second collection that could disagree with the registered one. A registered
	///     synchronous shape claims the whole collection - including the <c>IAsyncEnumerable&lt;T&gt;</c> and awaited
	///     <c>Task&lt;C&gt;</c> views, so injecting either is AWT101 rather than a second collection synthesized
	///     behind the opaque one - mirroring the by-type SynthesisSuppressed gate; a registered
	///     <c>IAsyncEnumerable&lt;T&gt;</c> or <c>Task&lt;C&gt;</c> claims only its own exact shape.
	/// </summary>
	private static ParameterModel SuppressRegisteredCollectionSynthesis(
		ParameterModel parameterModel,
		IParameterSymbol parameter,
		Dictionary<ServiceKey, string> serviceToImpl)
	{
		bool syncShapeRegistered = parameterModel.Kind is (DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable)
		                           && CollectionShapeTypes(parameterModel.ServiceType).Any(shape => serviceToImpl.ContainsKey(new ServiceKey(shape, parameterModel.Key)));
		bool asyncShapeRegistered = parameterModel.Kind == DependencyKind.AsyncEnumerable
		                            && serviceToImpl.ContainsKey(new ServiceKey(AsyncEnumerableShapeType(parameterModel.ServiceType), parameterModel.Key));
		bool awaitedShapeRegistered = parameterModel.Kind == DependencyKind.AwaitedEnumerable
		                              && serviceToImpl.ContainsKey(new ServiceKey(parameter.Type.ToDisplayString(FullyQualified), parameterModel.Key));
		if (syncShapeRegistered || asyncShapeRegistered || awaitedShapeRegistered)
		{
			string collectionType = parameter.Type.ToDisplayString(FullyQualified);
			return parameterModel with { ServiceType = collectionType, Kind = DependencyKind.Direct, AwaitedCollectionType = null, };
		}

		return parameterModel;
	}

	/// <summary>
	///     Redirects a single-service consumer parameter requesting a closed generic interface with no exact
	///     registration to a variance-compatible registration (Part A): a registered <c>IHandler&lt;DomainEvent&gt;</c>
	///     (<c>in T</c>) satisfying a request for <c>IHandler&lt;OrderPlaced&gt;</c>, or an
	///     <c>IFactory&lt;OrderPlaced&gt;</c> (<c>out T</c>) satisfying <c>IFactory&lt;DomainEvent&gt;</c>. It runs
	///     only when the normal lookup misses, so an exact registration always wins; keyed parameters (reached
	///     through their key) and collections (a set, handled by <see cref="RecordRequestedCollectionElement" />)
	///     are out of scope. The redirect reuses the target registration's resolver by rewriting the parameter's
	///     service type - no new instance is synthesized, exactly as decorator/collection redirection does - and
	///     records the requested closed type as a top-level dispatch alias (Part B) so an imperative
	///     <c>Resolve&lt;T&gt;()</c> / <c>Resolve(T)</c> routes to the same target. Only the delivery shapes whose
	///     emitted expression still converts after the rewrite are redirected: a direct dependency (the resolved
	///     instance converts by the very variance that matched) and a <c>Func&lt;…, T&gt;</c> (covariant in its
	///     result). The <c>Lazy&lt;T&gt;</c> / <c>Task&lt;T&gt;</c> wrappers are invariant in <c>T</c>, so a
	///     differently-closed wrapped request has no conversion to the parameter's declared type and stays a
	///     missing dependency (AWT101).
	/// </summary>
	private static ParameterModel RedirectVariance(
		ParameterModel parameterModel,
		IParameterSymbol parameter,
		Dictionary<ServiceKey, string> serviceToImpl,
		VarianceState variance)
	{
		if (variance.Candidates.Count == 0
		    || parameterModel.Key is not null
		    || parameterModel.Kind is not (DependencyKind.Direct or DependencyKind.Func)
		    || serviceToImpl.ContainsKey(KeyOf(parameterModel))
		    || UnderlyingServiceType(parameter.Type) is not { } requested
		    // The unwrapped symbol must denote the classified service type: a shape the classification treats as
		    // an opaque direct dependency (ValueTask<T>, a nested relationship like Func<Lazy<T>>) unwraps to a
		    // different type here, and redirecting it would emit an argument the declared parameter type cannot
		    // accept. Likewise Func<…, Owned<T>>, whose classified service is the inner T, not the Owned<T> handle.
		    || requested.ToDisplayString(FullyQualified) != parameterModel.ServiceType
		    || FindVarianceMatch(requested, parameterModel.ServiceType, variance) is not { } variantMatch)
		{
			return parameterModel;
		}

		// Record the requested closed type as a top-level dispatch alias (Part B) before the redirect rewrites
		// ServiceType. The same requested type always picks the same nearest target, so first-seen wins keeps the
		// alias stable across consumers.
		string requestedType = parameterModel.ServiceType;
		if (serviceToImpl.TryGetValue(new ServiceKey(variantMatch, null), out string? variantImpl)
		    && !variance.Aliases.ContainsKey(requestedType))
		{
			variance.Aliases.Add(requestedType, variantImpl);
			variance.AliasOrder.Add(requestedType);
		}

		return parameterModel with { ServiceType = variantMatch, };
	}

	/// <summary>
	///     Records an unkeyed collection parameter whose element is a closed generic interface (Part C), so after
	///     the instance loop its members can be unioned with every variance-compatible registration - a collection
	///     of <c>IHandler&lt;OrderPlaced&gt;</c> then includes a registered <c>IHandler&lt;DomainEvent&gt;</c>
	///     (<c>in T</c>). The synchronous (<c>IEnumerable&lt;T&gt;</c> / <c>T[]</c>), asynchronous
	///     (<c>IAsyncEnumerable&lt;T&gt;</c>) and awaited (<c>Task&lt;C&gt;</c>) shapes are all captured - they
	///     share one membership per (element type, key), so the union reaches every shape alike. A keyed collection
	///     ([FromKey]) resolves only its keyed registrations, and every variance candidate is unkeyed, so a keyed
	///     collection is left untouched.
	/// </summary>
	private static void RecordRequestedCollectionElement(
		ParameterModel parameterModel,
		IParameterSymbol parameter,
		VarianceState variance)
	{
		if (variance.Candidates.Count == 0
		    || parameterModel.Key is not null
		    || parameterModel.Kind is not (DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable)
		    || CollectionElementSymbol(parameter.Type) is not { } element
		    || !variance.CollectionSeen.Add(parameterModel.ServiceType))
		{
			return;
		}

		variance.CollectionElements.Add((parameterModel.ServiceType, element));
	}

	/// <summary>
	///     Unions every variance-compatible registration's members into each requested closed-generic collection's
	///     membership (Part C): the exact and open-expanded members keep their order and lead, and variance members
	///     follow in candidate registration order, deduped by implementation. A collection with only a variance
	///     match (no exact member) gains a fresh membership entry. Run after the instance loop and before the
	///     parameterized prune and edge building, so the unioned members drive analysis and emission.
	/// </summary>
	private static void UnionVarianceCollectionMembers(
		VarianceState variance,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		List<ServiceKey> serviceMemberOrder)
	{
		foreach ((string ServiceType, INamedTypeSymbol Symbol) requested in variance.CollectionElements)
		{
			List<(string ServiceType, INamedTypeSymbol Symbol)> matches =
				VarianceMatches(requested.Symbol, requested.ServiceType, variance);
			if (matches.Count == 0)
			{
				continue;
			}

			ServiceKey requestedKey = new(requested.ServiceType, null);
			if (!serviceMembers.TryGetValue(requestedKey, out List<string>? members))
			{
				members = new List<string>();
				serviceMembers.Add(requestedKey, members);
				serviceMemberOrder.Add(requestedKey);
			}

			UnionMatchedMembers(members, matches, serviceMembers);
		}
	}

	/// <summary>
	///     Appends each matched candidate's members to <paramref name="members" /> in candidate order, deduped by
	///     implementation (a candidate whose members were all pruned, or that failed to build, contributes none).
	/// </summary>
	private static void UnionMatchedMembers(
		List<string> members,
		List<(string ServiceType, INamedTypeSymbol Symbol)> matches,
		Dictionary<ServiceKey, List<string>> serviceMembers)
	{
		foreach ((string ServiceType, INamedTypeSymbol Symbol) match in matches)
		{
			if (!serviceMembers.TryGetValue(new ServiceKey(match.ServiceType, null), out List<string>? candidateMembers))
			{
				continue;
			}

			foreach (string member in candidateMembers.Where(member => !members.Contains(member)))
			{
				members.Add(member);
			}
		}
	}

	/// <summary>
	///     Exposes each closed generic type that was variance-redirected as a single service under its chosen
	///     target instance (Part B), so <c>Resolve&lt;T&gt;()</c> / <c>Resolve(T)</c> - and the Func/Lazy/Owned
	///     variants and registration metadata, all driven by an instance's <c>Services</c> - route it to the same
	///     resolver the consumer parameter uses. Skipped when an exact registration already owns the type (it never
	///     does when the alias was recorded, since a redirect fires only on a miss) or the target failed to build.
	/// </summary>
	private static void ApplyVarianceDispatchAliases(
		VarianceState variance,
		List<InstanceModel> instances,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, string> serviceToImpl)
	{
		foreach (string requestedType in variance.AliasOrder)
		{
			if (serviceToImpl.ContainsKey(new ServiceKey(requestedType, null))
			    || !implToIndex.TryGetValue(variance.Aliases[requestedType], out int index))
			{
				continue;
			}

			ServiceKey aliasKey = new(requestedType, null);
			ServiceKey[] existing = instances[index].Services.AsArray();
			if (System.Array.IndexOf(existing, aliasKey) >= 0)
			{
				continue;
			}

			ServiceKey[] augmented = new ServiceKey[existing.Length + 1];
			System.Array.Copy(existing, augmented, existing.Length);
			augmented[existing.Length] = aliasKey;
			instances[index] = instances[index] with { Services = new EquatableArray<ServiceKey>(augmented), };
		}
	}

	/// <summary>
	///     The single service type a parameter resolves, as a symbol - the result of a <c>Func&lt;T&gt;</c> /
	///     <c>Func&lt;TArg…, T&gt;</c> relationship, or the parameter type itself for a direct dependency - so the
	///     variance redirect can compare it against the registered service symbols. Returns <see langword="null" />
	///     for a non-named type. The caller cross-checks the display string against the classified
	///     <c>ServiceType</c>, so a shape the classification treats differently never redirects.
	/// </summary>
	private static INamedTypeSymbol? UnderlyingServiceType(ITypeSymbol type)
	{
		if (type is INamedTypeSymbol { IsGenericType: true, Name: "Func", TypeArguments.Length: >= 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System")
		{
			return named.TypeArguments[named.TypeArguments.Length - 1] as INamedTypeSymbol;
		}

		return type as INamedTypeSymbol;
	}

	/// <summary>
	///     The element type symbol of a collection dependency - a synchronous shape (<c>T[]</c>,
	///     <c>IEnumerable&lt;T&gt;</c> and friends), an asynchronous one (<c>IAsyncEnumerable&lt;T&gt;</c>) or an
	///     awaited one (<c>Task&lt;C&gt;</c> over a synchronous shape, e.g. <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c>) -
	///     when that element is a named type, or <see langword="null" /> otherwise. Exactly the shapes
	///     classification maps to <see cref="DependencyKind.Enumerable" /> / <see cref="DependencyKind.AsyncEnumerable" /> /
	///     <see cref="DependencyKind.AwaitedEnumerable" /> (the caller's kind gate), so no other wrapper reaches
	///     here - in particular <c>ValueTask&lt;C&gt;</c> never classifies as a collection, matching
	///     <see cref="TryGetAwaitedCollection" />. Used to variance-match a requested collection element against
	///     the registered service symbols (the string-only element in the parameter model is enough for
	///     membership, but variance needs the symbol).
	/// </summary>
	private static INamedTypeSymbol? CollectionElementSymbol(ITypeSymbol type)
	{
		if (type is IArrayTypeSymbol array)
		{
			return array.ElementType as INamedTypeSymbol;
		}

		// An awaited collection wraps a synchronous shape in Task<C>; recurse into the inner collection type.
		if (IsTask(type, out ITypeSymbol awaitedCollection))
		{
			return CollectionElementSymbol(awaitedCollection);
		}

		if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
		    && named.Name is "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection" or "IAsyncEnumerable")
		{
			return named.TypeArguments[0] as INamedTypeSymbol;
		}

		return null;
	}

	/// <summary>
	///     Every registered service that satisfies a requested closed generic interface through declared C#
	///     variance, in registration order: a registered <c>IHandler&lt;DomainEvent&gt;</c> (<c>in T</c>) satisfies
	///     a request for <c>IHandler&lt;OrderPlaced&gt;</c>; a registered <c>IFactory&lt;OrderPlaced&gt;</c>
	///     (<c>out T</c>) satisfies <c>IFactory&lt;DomainEvent&gt;</c>. Only candidates of the same generic
	///     interface definition are considered, and the exact request is skipped (handled by the normal lookup). A
	///     single-service consumer (<see cref="FindVarianceMatch" />) picks the nearest of these; a collection
	///     unions them all. An invariant interface (no <c>in</c>/<c>out</c>) and any non-interface request yield
	///     nothing, so both still report AWT101 when unregistered.
	/// </summary>
	private static List<(string ServiceType, INamedTypeSymbol Symbol)> VarianceMatches(
		INamedTypeSymbol requested,
		string requestedServiceType,
		VarianceState variance)
	{
		List<(string ServiceType, INamedTypeSymbol Symbol)> matches = new();

		// Variance is defined for constructed generic interfaces only. A non-generic or non-interface request
		// cannot be variance-matched (and classes never carry variance).
		if (!requested.IsGenericType || requested.TypeKind != TypeKind.Interface)
		{
			return matches;
		}

		INamedTypeSymbol requestedDefinition = requested.OriginalDefinition;

		// The interface must declare at least one variant (in/out) type parameter; an invariant interface
		// (IStore<T> with no in/out) never matches a differently-closed registration.
		if (!HasDeclaredVariance(requested))
		{
			return matches;
		}

		foreach ((string ServiceType, INamedTypeSymbol Symbol) candidate in variance.Candidates)
		{
			// Skip the exact request (handled by the normal lookup) and any candidate of a different interface.
			if (candidate.ServiceType == requestedServiceType
			    || !SymbolEqualityComparer.Default.Equals(candidate.Symbol.OriginalDefinition, requestedDefinition))
			{
				continue;
			}

			// The candidate satisfies the request when an instance of the candidate's service IS-A the requested
			// service - exactly the implicit reference conversion C# variance defines.
			if (VarianceCompatible(candidate.Symbol, requested, variance.Compilation))
			{
				matches.Add(candidate);
			}
		}

		return matches;
	}

	/// <summary>
	///     Whether the generic interface's definition declares at least one variant (<c>in</c>/<c>out</c>) type
	///     parameter - the precondition for any differently-closed construction of it to be convertible.
	/// </summary>
	private static bool HasDeclaredVariance(INamedTypeSymbol service)
		=> service.OriginalDefinition.TypeParameters.Any(parameter => parameter.Variance is VarianceKind.In or VarianceKind.Out);

	/// <summary>
	///     The single registered service that best satisfies a requested closed generic interface through declared
	///     C# variance (Part A). When several candidates match, the nearest one wins - the one whose service every
	///     other matching candidate's service is itself assignable to (the most-derived argument under
	///     contravariance, the most-general under covariance): a registered <c>IHandler&lt;DomainEvent&gt;</c>
	///     beats a registered <c>IHandler&lt;object&gt;</c> for a requested <c>IHandler&lt;OrderPlaced&gt;</c> -
	///     falling back to registration order for unordered candidates, so the result is deterministic. Returns
	///     the matching candidate's service-type string (the existing resolver to reuse), or
	///     <see langword="null" /> when there is no variance match.
	/// </summary>
	private static string? FindVarianceMatch(
		INamedTypeSymbol requested,
		string requestedServiceType,
		VarianceState variance)
	{
		(string ServiceType, INamedTypeSymbol Symbol)? best = null;
		foreach ((string ServiceType, INamedTypeSymbol Symbol) candidate in VarianceMatches(requested, requestedServiceType, variance))
		{
			// The candidate is nearer the request than the current best when the best's service converts to it:
			// under contravariance the more-derived closure sits between the request and the more-general one
			// (IHandler<object> IS-A IHandler<DomainEvent> IS-A IHandler<OrderPlaced>), and under covariance the
			// more-general closure does - in both cases the conversion target is the better pick.
			if (best is not { } current || VarianceCompatible(current.Symbol, candidate.Symbol, variance.Compilation))
			{
				best = candidate;
			}
		}

		return best?.ServiceType;
	}

	/// <summary>
	///     True when an instance of the constructed generic interface <paramref name="from" /> IS-A
	///     <paramref name="to" /> through declared C# variance: both must be the same generic interface
	///     definition, and at each type-argument position the declared variance must hold - covariant (<c>out</c>)
	///     requires the <c>from</c> argument assignable to the <c>to</c> argument, contravariant (<c>in</c>)
	///     requires the reverse, and an invariant position requires identical arguments. Reference conversions
	///     only (a value-type argument at a variant position is never variance-convertible in C#).
	/// </summary>
	private static bool VarianceCompatible(INamedTypeSymbol from, INamedTypeSymbol to, Compilation compilation)
	{
		if (!SymbolEqualityComparer.Default.Equals(from.OriginalDefinition, to.OriginalDefinition))
		{
			return false;
		}

		ImmutableArray<ITypeParameterSymbol> parameters = to.OriginalDefinition.TypeParameters;
		if (from.TypeArguments.Length != parameters.Length || to.TypeArguments.Length != parameters.Length)
		{
			return false;
		}

		for (int i = 0; i < parameters.Length; i++)
		{
			ITypeSymbol fromArg = from.TypeArguments[i];
			ITypeSymbol toArg = to.TypeArguments[i];
			if (SymbolEqualityComparer.Default.Equals(fromArg, toArg))
			{
				continue;
			}

			switch (parameters[i].Variance)
			{
				case VarianceKind.Out when fromArg.IsReferenceType && toArg.IsReferenceType && IsReferenceAssignable(fromArg, toArg, compilation):
				case VarianceKind.In when fromArg.IsReferenceType && toArg.IsReferenceType && IsReferenceAssignable(toArg, fromArg, compilation):
					continue;
				default:
					return false;
			}
		}

		return true;
	}

	/// <summary>
	///     True when an identity or implicit reference conversion exists from <paramref name="from" /> to
	///     <paramref name="to" /> - exactly what a variant type-argument position requires. Classified by the
	///     compiler rather than re-derived, so it covers every reference conversion, including an interface to
	///     <c>object</c>, array covariance, and the variance conversions a nested variant position needs
	///     (<c>IEnumerable&lt;OrderPlaced&gt;</c> to <c>IEnumerable&lt;DomainEvent&gt;</c>).
	/// </summary>
	private static bool IsReferenceAssignable(ITypeSymbol from, ITypeSymbol to, Compilation compilation)
	{
		Microsoft.CodeAnalysis.Operations.CommonConversion conversion = compilation.ClassifyCommonConversion(from, to);
		return conversion.IsIdentity || (conversion.IsImplicit && conversion.IsReference);
	}

	/// <summary>
	///     The mutable variance state threaded through instance building: the candidate registrations (every
	///     unkeyed closed-generic-interface registration, from coalescing), the compilation (whose conversion
	///     classification decides variance compatibility), and the accumulators the redirect fills - the top-level
	///     dispatch aliases (Part B) and the requested collection elements (Part C) - drained after the instance
	///     loop. Empty <see cref="Candidates" /> short-circuits every variance step.
	/// </summary>
	private sealed class VarianceState
	{
		public VarianceState(List<(string ServiceType, INamedTypeSymbol Symbol)> candidates, Compilation compilation)
		{
			Candidates = candidates;
			Compilation = compilation;
		}

		public List<(string ServiceType, INamedTypeSymbol Symbol)> Candidates { get; }
		public Compilation Compilation { get; }
		public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);
		public List<string> AliasOrder { get; } = new();
		public List<(string ServiceType, INamedTypeSymbol Symbol)> CollectionElements { get; } = new();
		public HashSet<string> CollectionSeen { get; } = new(StringComparer.Ordinal);
	}

	/// <summary>
	///     Classifies the producer's parameters (a constructor's or a factory method's) and reports
	///     <see cref="Diagnostics.MissingDependency">AWT101</see> for any non-<c>[Arg]</c> parameter whose
	///     service type is not registered. A runtime argument (<c>[Arg]</c>) is supplied at resolve time, so
	///     it is never a missing dependency. A service in the context's <c>ConstraintRejected</c> set - an open
	///     generic that could not be closed at the required type argument (AWT126) - is not reported again as
	///     AWT101: the constraint violation is the one root cause.
	/// </summary>
	private static List<ParameterModel> ClassifyParameters(
		IMethodSymbol producer,
		ImplInfo info,
		bool asyncFactory,
		BuildContext context)
	{
		List<ParameterModel> parameters = new();
		foreach (IParameterSymbol parameter in producer.Parameters)
		{
			ParameterModel parameterModel = ClassifyParameter(parameter, asyncFactory);

			// AWT134: a [FromServices] parameter (External) cannot also be an [Arg] runtime argument - it
			// cannot be both an externally-resolved dependency and a caller-supplied value. Point the diagnostic
			// at the offending parameter, falling back to the registration when its location is unavailable.
			if (parameterModel.Kind == DependencyKind.External && HasArgAttribute(parameter.GetAttributes()))
			{
				context.Diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ConflictingExternalParameter,
					parameterModel.Location ?? info.Location,
					new EquatableArray<string>([parameter.Name, DisplayInstance(info.ImplementationType),])));
			}

			parameterModel = RedirectDecoratorInner(parameterModel, info, context.DecoratorInner);

			parameterModel = SuppressRegisteredCollectionSynthesis(parameterModel, parameter, context.ServiceToImpl);

			// Variance: a closed-generic-interface request with no exact registration is redirected to a
			// variance-compatible registration before the [ImportServices] fall-through below - a match makes
			// the dependency container-resolved (its rewritten service type has a registration), so it is no
			// longer "otherwise-unresolved" and never falls through to the external provider.
			parameterModel = RedirectVariance(parameterModel, parameter, context.ServiceToImpl, context.Variance);
			RecordRequestedCollectionElement(parameterModel, parameter, context.Variance);

			// [ImportServices]: an otherwise-unresolved direct dependency (unkeyed) is satisfied from the
			// external provider rather than reported as missing. Only direct dependencies fall through; an
			// unregistered relationship type still surfaces as AWT101 below.
			if (context.ImportServices
			    && parameterModel is { Kind: DependencyKind.Direct, Key: null, }
			    && !context.ServiceToImpl.ContainsKey(KeyOf(parameterModel)))
			{
				parameterModel = parameterModel with { Kind = DependencyKind.External, };
			}

			parameters.Add(parameterModel);
			ReportWhenUnregistered(parameterModel, info, context);
		}

		return parameters;
	}

	/// <summary>
	///     Reports <see cref="Diagnostics.MissingDependency">AWT101</see> (or
	///     <see cref="Diagnostics.OwnedThroughLazy">AWT121</see> for an <c>Owned&lt;T&gt;</c> reached through a
	///     <c>Lazy</c>) for a classified parameter whose service type has no registration. A CancellationToken is
	///     forwarded from the resolve-time token, not resolved from the graph (like <c>[Arg]</c>), so it is never
	///     a missing dependency. A collection (Enumerable, AsyncEnumerable or the awaited AwaitedEnumerable) resolves
	///     to every registration of its element type under the parameter's key and an empty collection is legal, so
	///     an element type with no such registration is not a missing dependency either - it just yields an empty
	///     collection. (An unregistered collection type whose synthesis was suppressed was rewritten to Direct and so
	///     is no longer a collection kind here, and does surface as AWT101.) A closed generic that expansion refused to
	///     synthesize because its type arguments violate the open implementation's constraints (AWT126) is
	///     deliberately absent from <c>ServiceToImpl</c>; reporting AWT101 on top would name the same root cause
	///     twice, so it is suppressed here. An External dependency (a <c>[FromServices]</c> parameter, or an
	///     <c>[ImportServices]</c> fall-through) is resolved from the external provider, not the Awaiten graph, so
	///     it is never a missing dependency.
	/// </summary>
	private static void ReportWhenUnregistered(ParameterModel parameterModel, ImplInfo info, BuildContext context)
	{
		if (parameterModel.Kind is DependencyKind.Arg or DependencyKind.CancellationToken or DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable or DependencyKind.External
		    || context.ServiceToImpl.ContainsKey(KeyOf(parameterModel))
		    || context.ConstraintRejected.Contains(parameterModel.ServiceType))
		{
			return;
		}

		// Lazy does not unwrap Owned<T> (memoizing a disposal handle is a footgun), so a Lazy<Owned<T>> /
		// Lazy<Task<Owned<T>>> leaves the handle's Owned<T> type as the service - which is not registered.
		// Report that with the supported owned forms rather than a bare "missing Owned<T>" (AWT101).
		bool ownedThroughLazy = parameterModel.Kind is DependencyKind.Lazy or DependencyKind.LazyTask
		                        && parameterModel.ServiceType.StartsWith("global::Awaiten.Owned<", StringComparison.Ordinal);

		context.Diagnostics.Add(new DiagnosticInfo(
			ownedThroughLazy ? Diagnostics.OwnedThroughLazy : Diagnostics.MissingDependency,
			info.Location,
			new EquatableArray<string>([
				Display(info.OwningServiceOrImpl),
				DisplayInstance(info.ImplementationType),
				DisplayKeyed(parameterModel.ServiceType, parameterModel.Key),
			])));
	}

	/// <summary>
	///     Discovers the injected properties of a constructed implementation: every property marked
	///     <c>[Inject]</c> (opt-in only - a plain <c>required</c> property is left to the caller and is not
	///     auto-injected). Each is classified exactly like a Direct constructor parameter and resolved against
	///     the graph, so it produces a graph edge for cycle, captive and async-taint analysis and is filled
	///     through an object initializer after construction. Walks the implementation and its base types
	///     (most-derived first), so an overriding or shadowing declaration wins. Reports AWT136 (<c>[Inject]</c>
	///     on a property with no set/init accessor the container can assign through), AWT137 (an injected
	///     property marked <c>[Arg]</c>) and AWT101 (a member with no registration to satisfy it), each at the
	///     property's own location.
	/// </summary>
	private static void DiscoverInjectedMembers(
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Dictionary<ServiceKey, string> serviceToImpl,
		HashSet<string> constraintRejected,
		List<MemberModel> members,
		List<DiagnosticInfo> diagnostics)
	{
		HashSet<string> seen = new(StringComparer.Ordinal);
		for (INamedTypeSymbol? type = info.Symbol; type is not null; type = type.BaseType)
		{
			foreach (IPropertySymbol property in type.GetMembers().OfType<IPropertySymbol>())
			{
				// Walk most-derived first, recording every instance property (seen) so a base declaration is
				// shadowed by an overriding or `new` one. Property injection is opt-in: only a property marked
				// [Inject] is filled; a plain required property is left to the caller (never auto-injected).
				if (property.IsStatic || property.IsIndexer || !seen.Add(property.Name) || !HasInject(property.GetAttributes()))
				{
					continue;
				}

				if (ClassifyInjectedMember(property, info, containerSymbol, serviceToImpl, constraintRejected, diagnostics) is { } member)
				{
					members.Add(member);
				}
			}
		}
	}

	/// <summary>
	///     Classifies one <c>[Inject]</c> property into the member edge to fill after construction, or reports
	///     why it cannot be injected and returns <c>null</c>: AWT136 (no set/init accessor the container can
	///     reach), AWT137 (<c>[Arg]</c> on an injected property) or - when the resolved edge has no registration -
	///     AWT101 (with AWT121 substituted for an <c>Owned&lt;T&gt;</c> requested through <c>Lazy</c>). Each is
	///     reported at the property's own location. A missing registration only diagnoses; it still yields a
	///     member so the edge participates in analysis, exactly like a constructor parameter.
	/// </summary>
	private static MemberModel? ClassifyInjectedMember(
		IPropertySymbol property,
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Dictionary<ServiceKey, string> serviceToImpl,
		HashSet<string> constraintRejected,
		List<DiagnosticInfo> diagnostics)
	{
		LocationInfo? location = LocationInfo.From(property.Locations.FirstOrDefault());

		// AWT136: an [Inject] property must have a set/init accessor the container can assign through the object
		// initializer. The container is not a derived type, so a protected/private-protected setter (and a
		// cross-assembly internal one) is out of reach even though it is not private - apply the same accessibility
		// test the constructor path uses rather than a bare not-private check, so an unreachable setter surfaces as
		// AWT136 instead of an inaccessible-setter error in generated code.
		if (property.SetMethod is not { } setter || !IsAccessibleSetter(setter, containerSymbol))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.InjectedPropertyNotSettable,
				location,
				new EquatableArray<string>([property.Name, DisplayInstance(info.ImplementationType),])));
			return null;
		}

		ParameterModel dependency = ClassifyDependency(
			property.Type, property.GetAttributes(), asyncFactory: false, location);

		// AWT137: runtime arguments flow only through a Func<…> factory into [Arg] constructor parameters, never
		// through property injection (the member resolves entirely from the graph).
		if (dependency.Kind == DependencyKind.Arg)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.InjectedPropertyIsArg,
				location,
				new EquatableArray<string>([property.Name, DisplayInstance(info.ImplementationType),])));
			return null;
		}

		// AWT101: a direct/relationship member edge needs a registration to satisfy it (a collection member is
		// satisfied elsewhere, like a constructor parameter, and yields an empty collection when unregistered).
		// Mirror ClassifyParameters: a constraint-rejected open generic (AWT126) is not re-reported here, and an
		// Owned<T> requested through Lazy surfaces the targeted AWT121 instead.
		if (dependency.Kind is not (DependencyKind.Enumerable or DependencyKind.AsyncEnumerable)
		    && !serviceToImpl.ContainsKey(KeyOf(dependency))
		    && !constraintRejected.Contains(dependency.ServiceType))
		{
			bool ownedThroughLazy = dependency.Kind is DependencyKind.Lazy or DependencyKind.LazyTask
			                        && dependency.ServiceType.StartsWith("global::Awaiten.Owned<", StringComparison.Ordinal);

			diagnostics.Add(new DiagnosticInfo(
				ownedThroughLazy ? Diagnostics.OwnedThroughLazy : Diagnostics.MissingDependency,
				location,
				new EquatableArray<string>([
					Display(info.OwningServiceOrImpl),
					DisplayInstance(info.ImplementationType),
					DisplayKeyed(dependency.ServiceType, dependency.Key),
				])));
		}

		return new MemberModel(property.Name, dependency);

		// The setter must be reachable from the container's object initializer, which is not a derived context:
		// mirrors IsAccessibleConstructor - public always, internal/protected-internal only within the container's
		// own assembly, and protected/private-protected/private never (the container cannot reach them).
		static bool IsAccessibleSetter(IMethodSymbol setter, INamedTypeSymbol containerSymbol)
			=> setter.DeclaredAccessibility switch
			{
				Accessibility.Public => true,
				Accessibility.Internal or Accessibility.ProtectedOrInternal =>
					SymbolEqualityComparer.Default.Equals(setter.ContainingAssembly, containerSymbol.ContainingAssembly),
				_ => false,
			};
	}

	/// <summary>
	///     Resolves a <c>Factory</c> registration to the container method that produces it. No accessible
	///     method of that name returns the registered type → <see cref="Diagnostics.InvalidFactory">AWT108</see>;
	///     more than one (an overload) → <see cref="Diagnostics.AmbiguousFactory">AWT112</see>.
	/// </summary>
	private static IMethodSymbol? ResolveFactory(
		INamedTypeSymbol containerSymbol,
		ImplInfo info,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		List<IMethodSymbol> candidates = ContainerRegistrations.FindFactoryCandidates(
			containerSymbol, info.ProductionMember!, info.Symbol, compilation);
		if (candidates.Count == 1)
		{
			return candidates[0];
		}

		diagnostics.Add(new DiagnosticInfo(
			candidates.Count == 0 ? Diagnostics.InvalidFactory : Diagnostics.AmbiguousFactory,
			info.Location,
			new EquatableArray<string>([Display(info.OwningServiceOrImpl), info.ProductionMember!,])));
		return null;
	}

	/// <summary>
	///     Validates an <c>Instance</c> registration against the named container member, reporting
	///     <see cref="Diagnostics.InvalidInstance">AWT109</see> when no accessible field or property of
	///     that name (on the container or an accessible base type) holds the registered type.
	/// </summary>
	private static void ValidateInstanceMember(
		INamedTypeSymbol containerSymbol,
		ImplInfo info,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		foreach (ISymbol member in ContainerRegistrations.AccessibleMembers(containerSymbol, info.ProductionMember!))
		{
			ITypeSymbol? memberType = member switch
			{
				IFieldSymbol field => field.Type,
				IPropertySymbol property => property.Type,
				_ => null,
			};
			if (memberType is not null && compilation.HasImplicitConversion(memberType, info.Symbol))
			{
				return;
			}
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.InvalidInstance,
			info.Location,
			new EquatableArray<string>([Display(info.OwningServiceOrImpl), info.ProductionMember!,])));
	}

	/// <summary>
	///     Chooses the constructor the container builds <paramref name="implementation" /> through: its single
	///     accessible constructor, or the greediest whose parameters are all satisfiable (falling back to the
	///     greediest so unresolved parameters surface as AWT101). <paramref name="additionallySatisfiable" />, when
	///     supplied, marks parameters the caller can satisfy beyond the registered set - open generic expansion
	///     passes it so a parameter whose closed generic is expanded on demand does not disqualify a constructor,
	///     letting the seed scan the same constructor the emitted container resolves.
	/// </summary>
	internal static IMethodSymbol? SelectConstructor(
		INamedTypeSymbol implementation,
		INamedTypeSymbol containerSymbol,
		IEnumerable<string> registeredServices,
		Func<IParameterSymbol, bool>? additionallySatisfiable = null,
		bool importServices = false)
	{
		List<IMethodSymbol> constructors = implementation.InstanceConstructors
			.Where(c => IsAccessibleConstructor(c, containerSymbol))
			.ToList();
		if (constructors.Count <= 1)
		{
			return constructors.FirstOrDefault();
		}

		HashSet<string> registered = new(registeredServices, StringComparer.Ordinal);
		IMethodSymbol? resolvable = constructors
			.Where(c => c.Parameters.All(p =>
			{
				// Selecting a constructor, never an async factory, so no CancellationToken forwarding applies.
				// A collection - synchronous (Enumerable), asynchronous (AsyncEnumerable) or awaited (AwaitedEnumerable)
				// - is always satisfiable: an unregistered element type just yields an empty collection, so it never
				// disqualifies a constructor. A [FromServices] (External) parameter is always satisfiable too; with
				// [ImportServices] any direct dependency can fall through to the external provider, so it does not
				// disqualify a constructor either.
				ParameterModel parameter = ClassifyParameter(p, asyncFactory: false);
				return parameter.Kind is DependencyKind.Arg or DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable or DependencyKind.External
				       || (importServices && parameter.Kind == DependencyKind.Direct)
				       || registered.Contains(parameter.ServiceType)
				       || (additionallySatisfiable?.Invoke(p) ?? false);
			}))
			.OrderByDescending(c => c.Parameters.Length)
			.FirstOrDefault();

		// Fall back to the greediest constructor so its unresolved parameters surface as AWT101.
		return resolvable ?? constructors.OrderByDescending(c => c.Parameters.Length).First();

		static bool IsAccessibleConstructor(IMethodSymbol constructor, INamedTypeSymbol containerSymbol)
		{
			return constructor.DeclaredAccessibility switch
			{
				Accessibility.Public => true,
				Accessibility.Internal or Accessibility.ProtectedOrInternal =>
					SymbolEqualityComparer.Default.Equals(
						constructor.ContainingAssembly, containerSymbol.ContainingAssembly),
				_ => false,
			};
		}
	}

	/// <summary>
	///     Classifies a constructor parameter as a runtime argument (<c>[Arg]</c>), a deferred relationship
	///     type (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c> or <c>Func&lt;TArg…, T&gt;</c>) or a direct
	///     dependency, returning the underlying service type it resolves. A <c>Func&lt;TArg…, T&gt;</c> also
	///     carries the leading runtime-argument types it supplies to the produced service's <c>[Arg]</c>
	///     parameters. Only one level of nesting is supported: a relationship over another relationship
	///     (e.g. <c>Func&lt;Func&lt;T&gt;&gt;</c>) is classified as a direct dependency so it surfaces as an
	///     unregistered service type rather than a misleading diagnostic about the inner relationship.
	/// </summary>
	private static ParameterModel ClassifyParameter(IParameterSymbol parameter, bool asyncFactory)
	{
		LocationInfo? location = LocationInfo.From(parameter.Locations.FirstOrDefault());

		// An explicit [FromServices] parameter is resolved from the external provider; its own type is the
		// external service type, and a [FromKey] on it selects the keyed external service (the key is forwarded
		// to the resolver). It takes precedence so the parameter is never treated as an Awaiten graph edge (a
		// [FromServices] together with [Arg] is reported as AWT134 in ClassifyParameters). [FromServices] is a
		// constructor-parameter concern only, so it lives here rather than in the shared ClassifyDependency core
		// (an injected property never resolves from the external provider).
		if (HasFromServices(parameter))
		{
			return new ParameterModel(
				parameter.Type.ToDisplayString(FullyQualified), DependencyKind.External, Key: FromKey(parameter.GetAttributes()), Location: location);
		}

		return ClassifyDependency(parameter.Type, parameter.GetAttributes(), asyncFactory, location);
	}

	/// <summary>
	///     Classifies a dependency by its declared type and attributes, shared by constructor parameters and
	///     injected properties: a runtime argument (<c>[Arg]</c>), an asynchronous factory's forwarded
	///     <c>CancellationToken</c>, a deferred relationship type (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>,
	///     their async siblings or a bare <c>Owned&lt;T&gt;</c> / <c>Task&lt;T&gt;</c>), a collection, or a
	///     direct dependency - returning the underlying service type it resolves and an optional
	///     <c>[FromKey]</c> selection. The property path reuses this verbatim, so a member resolves exactly
	///     like a constructor parameter.
	/// </summary>
	private static ParameterModel ClassifyDependency(ITypeSymbol type, ImmutableArray<AttributeData> attributes, bool asyncFactory, LocationInfo? location)
	{
		if (HasArgAttribute(attributes))
		{
			return new ParameterModel(type.ToDisplayString(FullyQualified), DependencyKind.Arg, Location: location);
		}

		// An asynchronous factory's CancellationToken parameter is not resolved from the graph: the container
		// forwards the resolve-time token (the async creator's cancellationToken). Limited to async factories -
		// only they are constructed on the async path where that token exists; a synchronous factory (or a
		// constructor) has no ambient token to forward, so its CancellationToken stays an ordinary dependency
		// and is reported as AWT101 when unregistered rather than silently receiving default. An [Arg]
		// CancellationToken is handled above as a caller-supplied runtime argument and is left untouched.
		if (asyncFactory
		    && type is INamedTypeSymbol { Name: "CancellationToken", } token
		    && token.ContainingNamespace?.ToDisplayString() == "System.Threading")
		{
			return new ParameterModel(
				type.ToDisplayString(FullyQualified), DependencyKind.CancellationToken, Location: location);
		}

		// A [FromKey] selects the keyed registration of the dependency's service type, whether it is required
		// directly, deferred behind a Func<T>/Lazy<T>, wrapped in an Owned<T> handle, or a collection - the
		// service type is the same, only the delivery differs.
		string? key = FromKey(attributes);

		// An asynchronous collection (IAsyncEnumerable<T>) resolves to every registration of its element type, like
		// the synchronous collection shapes below, but awaits each member's initialization - so it is the one shape
		// through which an async-tainted member is legal. Recognized before the synchronous shapes (both live in
		// System.Collections.Generic) and before the relationship gate.
		if (IsAsyncEnumerable(type, out string? asyncElementType))
		{
			return new ParameterModel(asyncElementType!, DependencyKind.AsyncEnumerable, Key: key, Location: location);
		}

		// A collection dependency resolves to every registration of its element type under the parameter's
		// [FromKey] key (unkeyed by default). Recognized before the relationship types so IEnumerable<T> and T[]
		// are not mistaken for a plain generic service or an array-typed direct dependency.
		if (TryGetCollectionElement(type, out string? elementType))
		{
			return new ParameterModel(elementType!, DependencyKind.Enumerable, Key: key, Location: location);
		}

		// An awaited collection (Task<C> over the synchronous collection shapes, e.g. Task<IReadOnlyList<T>>)
		// resolves to every registration of its element type under the parameter's key, awaiting each
		// async-initialized member behind the returned task - the eager awaited sibling of the synchronous
		// collection, and the second shape (besides IAsyncEnumerable<T>) through which an async-tainted member is
		// legal. Recognized before the bare Task<T> relationship below, so Task<IReadOnlyList<T>> is the awaited
		// collection of T rather than a Task relationship over the (unregistered) collection type itself.
		if (TryGetAwaitedCollection(type, out string? awaitedElement, out string? awaitedCollection))
		{
			return new ParameterModel(
				awaitedElement!, DependencyKind.AwaitedEnumerable, Key: key, Location: location,
				AwaitedCollectionType: awaitedCollection);
		}

		// A bare Owned<T> dependency: resolve T into a throwaway scope and hand the caller the disposal handle.
		if (IsOwned(type, out ITypeSymbol ownedInner))
		{
			return new ParameterModel(ownedInner.ToDisplayString(FullyQualified), DependencyKind.Owned, Key: key, Location: location);
		}

		// A bare Task<T> dependency: an awaitable that resolves (and initializes) T. Task lives in
		// System.Threading.Tasks, not System, so it is recognized here rather than through the System-generic
		// relationship gate below (which handles the Func/Lazy wrappers, including Func<…, Task<T>>).
		if (IsTask(type, out ITypeSymbol taskResult))
		{
			// Task<Owned<T>> is the async counterpart of a bare Owned<T>: async-resolve (and initialize) T into a
			// throwaway child scope and hand back the disposal handle.
			return IsOwned(taskResult, out ITypeSymbol taskOwnedInner)
				? new ParameterModel(taskOwnedInner.ToDisplayString(FullyQualified), DependencyKind.Task, Key: key, Location: location, ProducesOwned: true)
				: new ParameterModel(taskResult.ToDisplayString(FullyQualified), DependencyKind.Task, Key: key, Location: location);
		}

		if (type is INamedTypeSymbol { IsGenericType: true, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System"
		    && ClassifyRelationship(named, key, location) is { } relationship)
		{
			return relationship;
		}

		// A direct dependency, optionally selecting a keyed registration with [FromKey].
		return new ParameterModel(
			type.ToDisplayString(FullyQualified), DependencyKind.Direct, Key: key, Location: location);
	}

	/// <summary>
	///     Classifies a <c>System</c> generic as the single-level relationship it defers - <c>Lazy&lt;T&gt;</c>,
	///     <c>Func&lt;T&gt;</c> or <c>Func&lt;TArg…, T&gt;</c> (the latter optionally producing an
	///     <c>Owned&lt;T&gt;</c> disposal handle) - returning the underlying service type. A type that is not a
	///     recognized relationship, or whose produced type is itself a relationship (nesting beyond one level),
	///     returns <see langword="null" /> so the caller treats it as a direct dependency on the whole type.
	/// </summary>
	private static ParameterModel? ClassifyRelationship(INamedTypeSymbol named, string? key, LocationInfo? location)
	{
		if (named is { Name: "Lazy", TypeArguments.Length: 1, } && !IsRelationshipType(named.TypeArguments[0]))
		{
			// Lazy<Task<T>> is the async counterpart of Lazy<T>: a memoized awaitable dependency.
			return IsTask(named.TypeArguments[0], out ITypeSymbol lazyTaskResult)
				? new ParameterModel(lazyTaskResult.ToDisplayString(FullyQualified), DependencyKind.LazyTask, Key: key, Location: location)
				: new ParameterModel(named.TypeArguments[0].ToDisplayString(FullyQualified), DependencyKind.Lazy, Key: key, Location: location);
		}

		if (named is not { Name: "Func", TypeArguments.Length: >= 1, })
		{
			return null;
		}

		// Func<T> defers resolution; Func<TArg…, T> additionally supplies runtime arguments (the leading type
		// arguments) to the produced service's [Arg]-marked parameters.
		ITypeSymbol[] typeArgs = named.TypeArguments.ToArray();
		ITypeSymbol service = typeArgs[typeArgs.Length - 1];
		string[] argTypes = typeArgs.Take(typeArgs.Length - 1)
			.Select(t => t.ToDisplayString(FullyQualified))
			.ToArray();

		// Func<…, Task<T>> is the async counterpart of Func<…, T>: an async factory that resolves (and
		// initializes) T, awaiting it. It forwards any leading runtime arguments to T's [Arg] parameters.
		// Func<…, Task<Owned<T>>> is its leak-free form: each call async-resolves T into a throwaway child scope
		// and hands back the Owned<T> disposal handle (the async counterpart of Func<…, Owned<T>>).
		if (IsTask(service, out ITypeSymbol funcTaskResult))
		{
			return IsOwned(funcTaskResult, out ITypeSymbol funcTaskOwnedInner)
				? new ParameterModel(
					funcTaskOwnedInner.ToDisplayString(FullyQualified), DependencyKind.FuncTask,
					new EquatableArray<string>(argTypes), Key: key, Location: location, ProducesOwned: true)
				: new ParameterModel(
					funcTaskResult.ToDisplayString(FullyQualified), DependencyKind.FuncTask,
					new EquatableArray<string>(argTypes), Key: key, Location: location);
		}

		// Func<…, Owned<T>> is the leak-free factory: its produced value is an Owned<T> disposal handle.
		if (IsOwned(service, out ITypeSymbol funcOwnedInner))
		{
			return new ParameterModel(
				funcOwnedInner.ToDisplayString(FullyQualified), DependencyKind.Func,
				new EquatableArray<string>(argTypes), Key: key, Location: location, ProducesOwned: true);
		}

		// Func<…, T> over a relationship type (nesting beyond one level) falls through to a direct dependency.
		return IsRelationshipType(service)
			? null
			: new ParameterModel(
				service.ToDisplayString(FullyQualified), DependencyKind.Func, new EquatableArray<string>(argTypes), Key: key, Location: location);
	}

	private static ServiceKey KeyOf(ParameterModel parameter) => new(parameter.ServiceType, parameter.Key);

	private static string DisplayKeyed(string serviceType, string? key)
		=> key is null ? Display(serviceType) : $"{Display(serviceType)} (key: {key})";

	private static string? FromKey(ImmutableArray<AttributeData> attributes)
	{
		foreach (AttributeData attribute in attributes)
		{
			if (attribute.AttributeClass is { Name: "FromKeyAttribute", } attributeClass
			    && attributeClass.ContainingNamespace?.ToDisplayString() == ContainerRegistrations.AttributeNamespace
			    && attribute.ConstructorArguments.Length == 1
			    && attribute.ConstructorArguments[0].Value is string key)
			{
				return key;
			}
		}

		return null;
	}

	/// <summary>
	///     Reads the container's <c>LifetimeSafety</c> from its <c>[Container]</c> attribute. Strict (the
	///     default, enum value 0) unless the attribute explicitly sets <c>Loose</c>.
	/// </summary>
	internal static bool ReadStrict(INamedTypeSymbol containerSymbol)
	{
		foreach (AttributeData attribute in containerSymbol.GetAttributes())
		{
			if (attribute.AttributeClass?.ToDisplayString() != ContainerAttributeName)
			{
				continue;
			}

			foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
			{
				// LifetimeSafety is an enum; its TypedConstant value is the underlying int (Strict = 0, Loose = 1).
				if (argument.Key == "LifetimeSafety" && argument.Value.Value is int value)
				{
					return value == 0;
				}
			}
		}

		return true;
	}

	/// <summary>
	///     Reads the container's <c>SyncResolveAfterInit</c> flag from its <c>[Container]</c> attribute
	///     (default <see langword="false" />: strict async resolution, where an async-tainted service is
	///     reachable only through <c>ResolveAsync</c>).
	/// </summary>
	internal static bool ReadSyncResolveAfterInit(INamedTypeSymbol containerSymbol)
	{
		foreach (AttributeData attribute in containerSymbol.GetAttributes())
		{
			if (attribute.AttributeClass?.ToDisplayString() != ContainerAttributeName)
			{
				continue;
			}

			foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
			{
				if (argument.Key == "SyncResolveAfterInit" && argument.Value.Value is bool value)
				{
					return value;
				}
			}
		}

		return false;
	}

	/// <summary>
	///     Marks every instance that is an async-taint source - its implementation is async-initialized, or it
	///     is produced by an asynchronous factory (Task&lt;T&gt; / ValueTask&lt;T&gt;), which the container can
	///     only reach by awaiting - or that reaches one through non-deferred (Direct) edges, by fixpoint over
	///     the dependency graph. The edges already exclude relationship/Owned/Arg parameters, so the taint is
	///     laundered by exactly the deferrals that break cycles.
	/// </summary>
	private static bool[] PropagateAsyncTaint(List<InstanceModel> instances, Dictionary<int, List<int>> dependencies)
	{
		bool[] tainted = new bool[instances.Count];
		for (int i = 0; i < instances.Count; i++)
		{
			tainted[i] = instances[i].IsAsyncInitializable || instances[i].IsAsyncFactory;
		}

		bool changed = true;
		while (changed)
		{
			changed = false;
			for (int i = 0; i < instances.Count; i++)
			{
				if (tainted[i])
				{
					continue;
				}

				foreach (int dependency in dependencies[i])
				{
					if (tainted[dependency])
					{
						tainted[i] = true;
						changed = true;
						break;
					}
				}
			}
		}

		return tainted;
	}

	/// <summary>
	///     AWT119 / AWT120 (strict mode): a synchronous <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c> /
	///     <c>Owned&lt;T&gt;</c> relationship resolves its target on demand without awaiting initialization,
	///     so it must not target an async-tainted service. AWT119 fires when the target is itself
	///     async-initialized; AWT120 fires when it only reaches one transitively, and reports the dependency
	///     path. (The prototype checked only <c>Func</c>/<c>Lazy</c>; <c>Owned</c> is included here because
	///     it is the same synchronous deferral and an async-tainted service emits no synchronous resolver
	///     for the <c>Owned</c> handle to build into.)
	/// </summary>
	private static void DetectSynchronousAsyncResolution(
		List<InstanceModel> instances,
		Dictionary<int, List<int>> dependencies,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		for (int i = 0; i < instances.Count; i++)
		{
			foreach (ParameterModel parameter in instances[i].ConstructorParameters.AsArray())
			{
				// Guard the implToIndex lookup: serviceToImpl can name an implementation whose BuildInstance
				// failed (so it is absent from implToIndex), and an unguarded indexer would crash the generator
				// (KeyNotFoundException) instead of surfacing the real registration error. Mirrors the guard in
				// BuildDependencyGraph / ValidateRuntimeArguments.
				if (parameter.Kind is not (DependencyKind.Func or DependencyKind.Lazy or DependencyKind.Owned)
				    || !serviceToImpl.TryGetValue(KeyOf(parameter), out string? targetImpl)
				    || !implToIndex.TryGetValue(targetImpl, out int target))
				{
					continue;
				}

				if (!instances[target].IsAsyncTainted)
				{
					continue;
				}

				// Point the diagnostic at the offending parameter; fall back to the consumer's registration.
				LocationInfo? location = parameter.Location ?? instanceLocations[i];
				if (instances[target].IsAsyncSource)
				{
					diagnostics.Add(new DiagnosticInfo(
						Diagnostics.SynchronousAsyncResolution,
						location,
						new EquatableArray<string>([
							DisplayInstance(instances[i].ImplementationType),
							parameter.Kind.ToString(),
							DisplayInstance(instances[target].ImplementationType),
						])));
				}
				else
				{
					string path = AsyncTaintPath(instances, dependencies, target);
					diagnostics.Add(new DiagnosticInfo(
						Diagnostics.AsyncDependencyOnSyncPath,
						location,
						new EquatableArray<string>([
							DisplayInstance(instances[i].ImplementationType),
							path,
						])));
				}
			}
		}
	}

	/// <summary>
	///     AWT122: a collection dependency is materialized synchronously, so a member whose implementation is
	///     async-tainted would be resolved without awaiting its initialization. Collections are synchronous-only,
	///     so this is reported rather than silently emitting a synchronous resolver for an async-tainted member
	///     (which the synchronous path does not even generate for such a member). Like AWT119/AWT120 this is a
	///     synchronous-resolution-of-async concern independent of lifetime safety, so it is reported under both
	///     strict and loose safety; only the pragmatic SyncResolveAfterInit mode suppresses it (the caller gates
	///     on that, as it does for AWT119/AWT120).
	/// </summary>
	private static void DetectSynchronousAsyncCollection(
		List<InstanceModel> instances,
		List<ServiceMembers> collections,
		Dictionary<string, int> implToIndex,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		if (collections.Count == 0)
		{
			return;
		}

		Dictionary<ServiceKey, ServiceMembers> byService = new();
		foreach (ServiceMembers collection in collections)
		{
			byService[new ServiceKey(collection.Service, collection.Key)] = collection;
		}

		for (int i = 0; i < instances.Count; i++)
		{
			foreach (ParameterModel parameter in instances[i].ConstructorParameters.AsArray())
			{
				if (parameter.Kind == DependencyKind.Enumerable
				    && byService.TryGetValue(KeyOf(parameter), out ServiceMembers members))
				{
					ReportAsyncTaintedMembers(i, parameter, members, instances, implToIndex, instanceLocations, diagnostics);
				}
			}
		}
	}

	// Reports AWT122 for each async-tainted member of the collection <paramref name="consumer" /> injects
	// through <paramref name="parameter" /> (a member absent from implToIndex failed to build and is skipped).
	private static void ReportAsyncTaintedMembers(
		int consumer,
		ParameterModel parameter,
		ServiceMembers members,
		List<InstanceModel> instances,
		Dictionary<string, int> implToIndex,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		foreach (string member in members.Implementations.AsArray())
		{
			if (!implToIndex.TryGetValue(member, out int memberIndex) || !instances[memberIndex].IsAsyncTainted)
			{
				continue;
			}

			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.AsyncCollectionResolution,
				instanceLocations[consumer],
				new EquatableArray<string>([
					DisplayInstance(instances[consumer].ImplementationType),
					Display(parameter.ServiceType),
					DisplayInstance(instances[memberIndex].ImplementationType),
				])));
		}
	}

	/// <summary>
	///     The shortest chain of Direct edges from <paramref name="start" /> to an async-initialized
	///     instance, rendered for the AWT120 message.
	/// </summary>
	private static string AsyncTaintPath(List<InstanceModel> instances, Dictionary<int, List<int>> dependencies, int start)
	{
		Queue<int> queue = new();
		Dictionary<int, int> previous = new();
		HashSet<int> visited = new() { start, };
		queue.Enqueue(start);
		int end = start;
		while (queue.Count > 0)
		{
			int node = queue.Dequeue();
			if (instances[node].IsAsyncSource)
			{
				end = node;
				break;
			}

			foreach (int next in dependencies[node].Where(visited.Add))
			{
				previous[next] = node;
				queue.Enqueue(next);
			}
		}

		List<int> chain = new();
		for (int node = end; ; node = previous[node])
		{
			chain.Insert(0, node);
			if (node == start)
			{
				break;
			}
		}

		return string.Join(" -> ", chain.Select(index => DisplayInstance(instances[index].ImplementationType)));
	}

	// Whether a type is a System.Threading.Tasks.Task<T>, yielding its result type T. Used to recognize the
	// async relationship types (Task<T>, Func<…, Task<T>>, Lazy<Task<T>>); ValueTask<T> is deliberately not a
	// relationship type (a stored ValueTask may only be awaited once) - it is supported solely as an async
	// factory's return type, on the producer side.
	private static bool IsTask(ITypeSymbol type, out ITypeSymbol result)
	{
		if (type is INamedTypeSymbol { IsGenericType: true, Name: "Task", TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
		{
			result = named.TypeArguments[0];
			return true;
		}

		result = type;
		return false;
	}

	/// <summary>
	///     Recognizes a collection dependency - an array <c>T[]</c> or one of the standard generic collection
	///     interfaces (<c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>,
	///     <c>IReadOnlyCollection&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>) - yielding the
	///     fully-qualified element type. An array satisfies every one of these, so the emitter materializes all
	///     of them as an array.
	/// </summary>
	private static bool TryGetCollectionElement(ITypeSymbol type, out string? elementType)
	{
		// Only a single-dimensional (rank-1) array is a collection shape: it is what the emitter materializes
		// (new T[] { … }). A multidimensional array (T[,]) is a distinct type that a rank-1 literal cannot fill,
		// so it stays an ordinary direct dependency (an unregistered one surfaces as AWT101, not broken codegen).
		if (type is IArrayTypeSymbol { Rank: 1, } array)
		{
			elementType = array.ElementType.ToDisplayString(FullyQualified);
			return true;
		}

		if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
		    && named.Name is "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection")
		{
			elementType = named.TypeArguments[0].ToDisplayString(FullyQualified);
			return true;
		}

		elementType = null;
		return false;
	}

	/// <summary>
	///     Recognizes an awaited collection - a <c>Task&lt;C&gt;</c> whose result <c>C</c> is one of the collection
	///     shapes recognized by <see cref="TryGetCollectionElement" /> (e.g. <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c>
	///     or <c>Task&lt;T[]&gt;</c>) - yielding the fully-qualified element type and inner collection type.
	///     <c>ValueTask&lt;C&gt;</c> is deliberately not recognized, for the same reason a bare
	///     <c>ValueTask&lt;T&gt;</c> is not a relationship type (see <see cref="IsTask" />): a stored ValueTask may
	///     only be awaited once, and an injected collection is held for the consumer's lifetime.
	/// </summary>
	private static bool TryGetAwaitedCollection(ITypeSymbol type, out string? elementType, out string? collectionType)
	{
		if (IsTask(type, out ITypeSymbol result) && TryGetCollectionElement(result, out elementType))
		{
			collectionType = result.ToDisplayString(FullyQualified);
			return true;
		}

		elementType = null;
		collectionType = null;
		return false;
	}

	// Whether a type is a System.Collections.Generic.IAsyncEnumerable<T> asynchronous collection, yielding its
	// fully-qualified element type T. The one collection shape that awaits its members, so it is classified apart
	// from the synchronous shapes in TryGetCollectionElement (which materialize eagerly into an array).
	private static bool IsAsyncEnumerable(ITypeSymbol type, out string? elementType)
	{
		if (type is INamedTypeSymbol { IsGenericType: true, Name: "IAsyncEnumerable", TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
		{
			elementType = named.TypeArguments[0].ToDisplayString(FullyQualified);
			return true;
		}

		elementType = null;
		return false;
	}

	// The fully-qualified IAsyncEnumerable<T> shape of <paramref name="elementType" />, in the exact form
	// registrations are stored under, so a membership check against serviceToImpl recognizes an explicitly
	// registered async-collection type (the async analogue of CollectionShapeTypes). Its own shape, so a single
	// string rather than a set.
	internal static string AsyncEnumerableShapeType(string elementType)
		=> $"global::System.Collections.Generic.IAsyncEnumerable<{elementType}>";

	// The fully-qualified type strings of every collection shape of <paramref name="elementType" /> - the five
	// generic collection interfaces and the rank-1 array - in the exact form registrations are stored under, so a
	// membership check against serviceToImpl recognizes an explicitly registered collection type. Shared by the
	// generator (injection classification) and the emitter (public dispatch) so the two never drift.
	internal static IEnumerable<string> CollectionShapeTypes(string elementType)
	{
		yield return $"global::System.Collections.Generic.IEnumerable<{elementType}>";
		yield return $"global::System.Collections.Generic.IReadOnlyList<{elementType}>";
		yield return $"global::System.Collections.Generic.IReadOnlyCollection<{elementType}>";
		yield return $"global::System.Collections.Generic.IList<{elementType}>";
		yield return $"global::System.Collections.Generic.ICollection<{elementType}>";
		yield return $"{elementType}[]";
	}

	// Whether a type is an Awaiten.Owned<T> disposal handle, yielding the owned service type T.
	private static bool IsOwned(ITypeSymbol type, out ITypeSymbol inner)
	{
		if (type is INamedTypeSymbol { Name: "Owned", TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == ContainerRegistrations.AttributeNamespace)
		{
			inner = named.TypeArguments[0];
			return true;
		}

		inner = type;
		return false;
	}

	private static bool HasArgAttribute(ImmutableArray<AttributeData> attributes)
		=> HasAwaitenAttribute(attributes, "ArgAttribute");

	private static bool HasInject(ImmutableArray<AttributeData> attributes)
		=> HasAwaitenAttribute(attributes, "InjectAttribute");

	private static bool HasAwaitenAttribute(ImmutableArray<AttributeData> attributes, string attributeName)
	{
		foreach (AttributeData attribute in attributes)
		{
			if (attribute.AttributeClass is { } attributeClass
			    && attributeClass.Name == attributeName
			    && attributeClass.ContainingNamespace?.ToDisplayString() == ContainerRegistrations.AttributeNamespace)
			{
				return true;
			}
		}

		return false;
	}

	// Whether a parameter is marked [FromServices], so it is resolved from the container's external provider
	// rather than the Awaiten graph.
	private static bool HasFromServices(IParameterSymbol parameter)
	{
		foreach (AttributeData attribute in parameter.GetAttributes())
		{
			if (attribute.AttributeClass is { Name: "FromServicesAttribute", } attributeClass
			    && attributeClass.ContainingNamespace?.ToDisplayString() == ContainerRegistrations.AttributeNamespace)
			{
				return true;
			}
		}

		return false;
	}

	// Whether the container is marked [ImportServices], so every otherwise-unresolved direct dependency is
	// satisfied from the external provider rather than reported as missing.
	private static bool ContainerImportsServices(INamedTypeSymbol containerSymbol)
	{
		foreach (AttributeData attribute in containerSymbol.GetAttributes())
		{
			if (attribute.AttributeClass is { Name: "ImportServicesAttribute", } attributeClass
			    && attributeClass.ContainingNamespace?.ToDisplayString() == ContainerRegistrations.AttributeNamespace)
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsRelationshipType(ITypeSymbol type)
		=> type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, Name: "Func" or "Lazy", } named
		   && named.ContainingNamespace?.ToDisplayString() == "System";

	/// <summary>
	///     Validates how parameterized services (those with <c>[Arg]</c>-marked parameters) are registered
	///     and consumed:
	///     <list type="bullet">
	///         <item>
	///             AWT114: a parameterized service is built fresh from its runtime arguments on every
	///             request, so a non-<c>Transient</c> lifetime cannot be honored.
	///         </item>
	///         <item>
	///             AWT113: a <c>Func&lt;TArg…, T&gt;</c> relationship must request exactly the runtime
	///             arguments that <c>T</c>'s <c>[Arg]</c> parameters expect, in order (a plain
	///             <c>Func&lt;T&gt;</c> over a parameterized service requests none, so it mismatches).
	///         </item>
	///         <item>
	///             AWT115: a parameterized service requested as a plain dependency or a <c>Lazy&lt;T&gt;</c>
	///             cannot be supplied its runtime arguments, so it is reachable only through a
	///             <c>Func&lt;TArg…, T&gt;</c>.
	///         </item>
	///     </list>
	/// </summary>
	private static void ValidateRuntimeArguments(
		List<InstanceModel> instances,
		List<LocationInfo?> instanceLocations,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		List<DiagnosticInfo> diagnostics)
	{
		for (int i = 0; i < instances.Count; i++)
		{
			InstanceModel instance = instances[i];
			LocationInfo? location = instanceLocations[i];

			if (instance.IsParameterized && instance.Lifetime != Lifetime.Transient)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ParameterizedLifetime,
					location,
					new EquatableArray<string>([Display(instance.ImplementationType), instance.Lifetime.ToString(),])));
			}

			// A parameterized async service (an [Arg] service that is IAsyncInitializable, is produced by an
			// asynchronous Task<T> / ValueTask<T> factory, or transitively reaches one) is built fresh per call
			// from its runtime arguments AND must await initialization, so its correct resolution path is the
			// async parameterized factory relationship Func<TArg…, Task<T>>, which forwards the arguments to the
			// async resolver. Misuse is caught at the consumption site rather than the registration: a synchronous
			// Func<TArg…, T> over it is AWT119 (cannot await), and a plain / Lazy<T> / Task<T> dependency that
			// supplies no arguments is AWT115 (parameterized requires a Func). There is therefore no
			// registration-time diagnostic for the [Arg]-plus-async combination itself.
			foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
			{
				// A collection (Enumerable, AsyncEnumerable or AwaitedEnumerable) resolves to a set of members, not
				// a single registration whose [Arg] parameters could be supplied, so runtime-argument matching does
				// not apply to it (its element type may coincidentally be singly registered, so it is excluded
				// explicitly rather than by the serviceToImpl lookup below). Guard the implToIndex lookup the same
				// way BuildDependencyGraph does: serviceToImpl can name an implementation whose BuildInstance failed
				// (so it is absent from implToIndex), and an unguarded indexer would crash the generator
				// (KeyNotFoundException) instead of surfacing the real registration error (e.g. AWT103).
				if (parameter.Kind is DependencyKind.Arg or DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable
				    || !serviceToImpl.TryGetValue(KeyOf(parameter), out string? targetImpl)
				    || !implToIndex.TryGetValue(targetImpl, out int targetIndex))
				{
					continue;
				}

				ValidateDependency(
					instance, parameter, instances[targetIndex].ArgTypes(), location, diagnostics);
			}
		}
	}

	/// <summary>
	///     Validates a single (non-<c>[Arg]</c>) dependency against its target's runtime arguments
	///     (<paramref name="expected" />): a <c>Func&lt;TArg…, T&gt;</c> or its async form
	///     <c>Func&lt;TArg…, Task&lt;T&gt;&gt;</c> must request exactly them (AWT113); a plain, <c>Lazy&lt;T&gt;</c>
	///     or <c>Task&lt;T&gt;</c> dependency cannot supply them at all, so a parameterized target must instead be
	///     reached through a <c>Func</c> (AWT115).
	/// </summary>
	private static void ValidateDependency(
		InstanceModel consumer,
		ParameterModel parameter,
		string[] expected,
		LocationInfo? consumerLocation,
		List<DiagnosticInfo> diagnostics)
	{
		// Point the diagnostic at the offending parameter; fall back to the consumer's registration when the
		// parameter has no usable location.
		LocationInfo? location = parameter.Location ?? consumerLocation;

		if (parameter.Kind is DependencyKind.Func or DependencyKind.FuncTask)
		{
			string[] requested = parameter.FuncArgTypes.AsArray();
			if (!requested.SequenceEqual(expected))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.RuntimeArgumentMismatch,
					location,
					new EquatableArray<string>([
						Display(parameter.ServiceType),
						FormatTypeList(requested),
						FormatTypeList(expected),
					])));
			}
		}
		else if (expected.Length > 0)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ParameterizedRequiresFunc,
				location,
				new EquatableArray<string>([Display(parameter.ServiceType), DisplayInstance(consumer.ImplementationType),])));
		}
	}

	// Renders a runtime-argument type list for a diagnostic message, reading as "none" when empty so a
	// mismatch against a service with no [Arg] parameters (or a Func that supplies none) is not an empty "()".
	private static string FormatTypeList(string[] types)
		=> types.Length == 0 ? "none" : string.Join(", ", types.Select(Display));

	private static void DetectCaptiveDependencies(
		List<InstanceModel> instances,
		Dictionary<int, List<int>> dependencies,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		for (int i = 0; i < instances.Count; i++)
		{
			if (instances[i].Lifetime == Lifetime.Singleton)
			{
				ReportCapturedScoped(i, instances, dependencies, instanceLocations, diagnostics);
			}
		}
	}

	private static void ReportCapturedScoped(
		int singleton,
		List<InstanceModel> instances,
		Dictionary<int, List<int>> dependencies,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		// Walk the singleton's graph through its transient dependencies (which are baked into it).
		// Reaching a scoped service means the singleton would capture it for the container's life. Each
		// node carries the index of the dependency that referenced it, so the diagnostic can name the
		// service alias the developer actually wrote rather than an arbitrary one of its service types.
		HashSet<int> visited = new();
		Stack<(int Node, int Parent)> stack = new();
		foreach (int dependency in dependencies[singleton])
		{
			stack.Push((dependency, singleton));
		}

		while (stack.Count > 0)
		{
			(int node, int parent) = stack.Pop();
			if (!visited.Add(node))
			{
				continue;
			}

			switch (instances[node].Lifetime)
			{
				case Lifetime.Scoped:
					ServiceKey referenced = ReferencedService(instances[parent], instances[node]);
					diagnostics.Add(new DiagnosticInfo(
						Diagnostics.CaptiveDependency,
						instanceLocations[singleton],
						new EquatableArray<string>([
							DisplayInstance(instances[singleton].ImplementationType),
							DisplayKeyed(referenced.Service, referenced.Key),
						])));
					break;
				case Lifetime.Transient:
					foreach (int next in dependencies[node])
					{
						stack.Push((next, node));
					}

					break;
			}
		}

		// The service key the parent's constructor used to reach this dependency - the alias the developer
		// wrote, including any [FromKey] - which is the one of the dependency's service keys that a parent
		// parameter selects. Falls back to the first service key if no parameter matches, or - when the
		// dependency is a collection member reached only through the collection and so exposes no service of
		// its own - to its implementation type, so the diagnostic still names it.
		static ServiceKey ReferencedService(InstanceModel parent, InstanceModel dependency)
		{
			ServiceKey[] dependencyServices = dependency.Services.AsArray();
			foreach (ParameterModel parameter in parent.ConstructorParameters.AsArray())
			{
				ServiceKey key = KeyOf(parameter);
				if (dependencyServices.Contains(key))
				{
					return key;
				}
			}

			return dependencyServices.Length > 0 ? dependencyServices[0] : new ServiceKey(dependency.ImplementationType, null);
		}
	}

	private static void DetectCycles(
		List<InstanceModel> instances,
		Dictionary<int, List<int>> dependencies,
		LocationInfo? containerLocation,
		List<DiagnosticInfo> diagnostics)
	{
		HashSet<int> visited = new();
		HashSet<int> onStack = new();
		List<int> path = new();
		HashSet<string> reportedCycles = new(StringComparer.Ordinal);

		for (int i = 0; i < instances.Count; i++)
		{
			Visit(i);
		}

		void Visit(int node)
		{
			visited.Add(node);
			onStack.Add(node);
			path.Add(node);

			foreach (int next in dependencies[node])
			{
				if (onStack.Contains(next))
				{
					ReportCycle(next);
				}
				else if (!visited.Contains(next))
				{
					Visit(next);
				}
			}

			onStack.Remove(node);
			path.RemoveAt(path.Count - 1);
		}

		void ReportCycle(int cycleStart)
		{
			int startIndex = path.LastIndexOf(cycleStart);
			List<int> cycle = path.GetRange(startIndex, path.Count - startIndex);
			cycle.Add(cycleStart);

			// Dedupe on the set of nodes so the same cycle is not reported once per back-edge.
			string signature = string.Join("|", cycle.Take(cycle.Count - 1).OrderBy(x => x));
			if (!reportedCycles.Add(signature))
			{
				return;
			}

			string rendered = string.Join(" -> ", cycle.Select(index => DisplayInstance(instances[index].ImplementationType)));
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.DependencyCycle,
				containerLocation,
				new EquatableArray<string>([rendered,])));
		}
	}

	private static string KeywordOf(INamedTypeSymbol symbol)
	{
		if (symbol.IsRecord)
		{
			return symbol.TypeKind == TypeKind.Struct ? "record struct" : "record";
		}

		return symbol.TypeKind == TypeKind.Struct ? "struct" : "class";
	}

	// Strip every 'global::' alias (the leading one and any nested in generic type arguments) so
	// diagnostics read 'System.Func<MyCode.Leaf>' rather than 'System.Func<global::MyCode.Leaf>'.
	internal static string Display(string fullyQualified)
		=> fullyQualified.Replace("global::", string.Empty);

	// Renders an instance identity for diagnostics. A decorator chain link carries a synthetic
	// '<type>@__dec:…' identity (see DecoratorIdentity); trim the synthetic suffix so an error names the real
	// decorator type ('MyCode.Deco') rather than the internal key ('MyCode.Deco@__dec:MyCode.IService:0:1').
	internal static string DisplayInstance(string implementationType)
	{
		int marker = implementationType.IndexOf("@" + DecoratorKeyPrefix, StringComparison.Ordinal);
		return Display(marker >= 0 ? implementationType.Substring(0, marker) : implementationType);
	}

	/// <summary>
	///     Redirects a decorator instance's inner parameter to the next-lower chain link. <see cref="ParameterServiceType" />
	///     is the parameter's own declared type (as <see cref="ClassifyParameter" /> classifies it), used to pick the single
	///     inner parameter out of the constructor. <see cref="ServiceType" /> is the decorated service type - the type under
	///     which every chain link is registered - so the redirect keys the parameter to <c>(ServiceType, InnerKey)</c> even
	///     when the parameter is declared as a base type of the service (its declared type is not a registration key).
	///     Consumed by <see cref="ClassifyParameters" /> when it classifies the decorator link.
	/// </summary>
	private readonly record struct DecoratorInner(string ParameterServiceType, string ServiceType, string InnerKey);

	private sealed class ImplInfo
	{
		public ImplInfo(
			string implementationType,
			INamedTypeSymbol symbol,
			Lifetime lifetime,
			LocationInfo? location,
			ProductionKind production,
			string? productionMember)
		{
			ImplementationType = implementationType;
			Symbol = symbol;
			Lifetime = lifetime;
			Location = location;
			Production = production;
			ProductionMember = productionMember;
			Services = new List<ServiceKey>();
		}

		public string ImplementationType { get; }
		public INamedTypeSymbol Symbol { get; }
		public Lifetime Lifetime { get; }
		public LocationInfo? Location { get; }
		public ProductionKind Production { get; }
		public string? ProductionMember { get; }
		public List<ServiceKey> Services { get; }

		/// <summary>
		///     The service type to name this implementation by in a diagnostic: its first winning service, or -
		///     when it won none (a collection member reached only through the collection) - the implementation
		///     type itself, so the message still identifies it rather than crashing on an empty service list.
		/// </summary>
		public string OwningServiceOrImpl => Services.Count > 0 ? Services[0].Service : ImplementationType;
	}
}
