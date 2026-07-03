using System.Text;
using Awaiten.SourceGenerators.Entities;
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
public sealed partial class AwaitenGenerator : IIncrementalGenerator
{
	private const string ContainerAttributeName = "Awaiten.ContainerAttribute";
	private const string AttributeNamespace = "Awaiten";

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

			spc.AddSource(model.HintName, SourceText.From(Sources.Emit(model), Encoding.UTF8));
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
		DetectNonTerminatingDeferredCycles(graph.Instances, graph.ConstructionDependencies, graph.CombinedDependencies, containerLocation, diagnostics);

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

		// Collect also expands the container's [Scan]s into overridable registrations (IsScan), ordered after
		// the explicit ones so coalescing lets an explicit registration win single resolution while every match
		// still joins its service's collection.
		(List<RawRegistration> raw, HashSet<string> constraintRejected) = Collect(containerSymbol, compilation, importServices, diagnostics);

		// A [Scan(SkipUnconstructable = true)] trades the AWT101 error for a skip-with-warning (AWT141) on
		// matches the container cannot construct: such a scan sweeps every assignable concrete class, so an
		// incidental helper type with an unsatisfiable constructor must not break the build. Scans without the
		// opt-in - and explicit registrations - keep the error.
		PruneUnconstructableScanMatches(raw, containerSymbol, compilation, importServices, constraintRejected, diagnostics);

		List<DecorateRegistration> decorators = CollectDecorators(containerSymbol);
		List<CompositeRegistration> composites = CollectComposites(containerSymbol);

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

		// The combined construction-plus-deferred graph vets deferred cycles (AWT145-147). Deferred members are
		// its only addition over the construction graph, so when none exists it IS the construction graph and the
		// extra pass is skipped (DetectNonTerminatingDeferredCycles early-exits on the same condition).
		Dictionary<int, List<int>> combinedDependencies = AnyDeferredMember(instances)
			? BuildCombinedGraph(instances, serviceToImpl, implToIndex, serviceMembers)
			: constructionDependencies;

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

		return new GraphModel(instances, dependencies, constructionDependencies, combinedDependencies, serviceToImpl, implToIndex, instanceLocations, collections, varianceCandidateTypes);
	}
}
