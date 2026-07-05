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
///     Assumptions: a container is a non-generic <c>partial class</c> (nested is allowed if every
///     enclosing type is partial and non-generic); each constructed type has a single accessible
///     constructor (else the one with the most resolvable parameters). Registrations of the same
///     implementation are coalesced into a single shared instance.
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
		// resolution; Loose relaxes that for MS.DI interop. AWT118 is reported by AwaitenAnalyzer, not here.
		bool strict = ReadStrict(containerSymbol);

		// When set, async-tainted services may also be resolved synchronously after InitializeAsync warms
		// them, and the AWT119/AWT120 sync-resolution diagnostics are not reported.
		bool syncResolveAfterInit = ReadSyncResolveAfterInit(containerSymbol);

		// IAsyncDisposable support is emitted only when the referenced Awaiten runtime exposes it (read off
		// Owned<T>, not System.IAsyncDisposable). When absent the container is sync-dispose only.
		bool hasAsyncDisposable = AsyncDisposableSupport(compilation) is not null;

		List<DiagnosticInfo> diagnostics = new();

		// The container must be a static class (a pure definition). A non-static class is AWT116; the emitter
		// still emits a throwing Root so consumers fail on that rather than a cascade of missing-member errors.
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

		// AWT119/AWT120 (strict only): a synchronous Func<T>/Lazy<T>/Owned<T> relationship resolves its target
		// without awaiting initialization, so it may not target an async-tainted service. SyncResolveAfterInit
		// allows it and so is not reported.
		if (!syncResolveAfterInit)
		{
			DetectSynchronousAsyncResolution(
				graph.Instances, graph.Dependencies, graph.ServiceToImpl, graph.ImplToIndex, graph.InstanceLocations, diagnostics);
			DetectSynchronousAsyncCollection(
				graph.Instances, graph.Collections, graph.KeyedCollections, graph.ImplToIndex, graph.InstanceLocations, diagnostics);

			// AWT161 (strict only): an async-tainted Eager singleton has no synchronous construction path, so it
			// cannot be built in the root's synchronous constructor. SyncResolveAfterInit allows it (blocking resolver).
			DetectEagerAsyncSingletons(graph.Instances, graph.InstanceLocations, diagnostics);
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

		// Qualify the hint name with namespace and enclosing types so containers sharing a simple name do not
		// collide. Nested types use '+' so 'Outer+Inner' cannot collide with a namespaced 'Outer.Inner'.
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
			new EquatableArray<string>(graph.VarianceCandidates.ToArray()),
			new EquatableArray<KeyedServiceMembers>(graph.KeyedCollections.ToArray()));
	}

	/// <summary>
	///     The framework/Awaiten interface symbols an instance is matched against, looked up once:
	///     <c>System.IDisposable</c>, the async-disposal symbol (non-null only when the runtime exposes it)
	///     and <c>Awaiten.IAsyncInitializable</c>. Any may be <see langword="null" />.
	/// </summary>
	private sealed record WellKnownTypes(
		INamedTypeSymbol? Disposable,
		INamedTypeSymbol? AsyncDisposable,
		INamedTypeSymbol? AsyncInitializable);

	/// <summary>
	///     The container-wide inputs threaded to each per-implementation <see cref="BuildInstance" />, passed
	///     together to avoid a long parameter list.
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
		HashSet<ServiceKey> ConsumedConditionals,
		List<DiagnosticInfo> Diagnostics);

	/// <summary>
	///     The mutable coalesced graph state the decorator-chain and composite builders rewrite in place: the
	///     single-dispatch winner per key, the implementations in declaration order, and collection membership.
	/// </summary>
	private sealed record CoalescedGraph(
		Dictionary<ServiceKey, string> ServiceToImpl,
		List<ImplInfo> ImplOrder,
		Dictionary<ServiceKey, List<string>> ServiceMembers);

	/// <summary>
	///     Resolves the container's object graph: coalesces registrations, builds an <see cref="InstanceModel" />
	///     per implementation and computes the dependency edges. Registration faults (AWT101/103/104/107-112) go
	///     to <paramref name="diagnostics" />. Shared by the generator and <see cref="AwaitenAnalyzer" /> (AWT118).
	/// </summary>
	internal static GraphModel BuildGraph(
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics,
		CancellationToken cancellationToken)
	{
		// Detect async disposal off the runtime's Owned<T>, so an instance is marked IsAsyncDisposable only
		// when the emitter will emit the async-disposal surface (see AsyncDisposableSupport).
		WellKnownTypes wellKnown = new(
			compilation.GetTypeByMetadataName("System.IDisposable"),
			AsyncDisposableSupport(compilation),
			compilation.GetTypeByMetadataName("Awaiten.IAsyncInitializable"));

		// The imported modules, resolved once. Import validation (AWT149-152, AWT154) is reported while collecting.
		List<ImportedModule> modules = CollectImportedModules(containerSymbol, diagnostics);

		// [ImportServices]: any otherwise-unresolved direct dependency falls through to the external provider
		// instead of AWT101. Computed up front because it widens constructor selection everywhere. A module can
		// contribute it too.
		bool importServices = ContainerImportsServices(containerSymbol)
		                      || modules.Any(module => HasAwaitenAttribute(module.Symbol.GetAttributes(), "ImportServicesAttribute"));

		// Collect also expands [Scan]s into overridable registrations (IsScan), ordered after the explicit ones
		// so an explicit registration wins single resolution while every match still joins its collection.
		(List<RawRegistration> raw, HashSet<string> constraintRejected) = Collect(containerSymbol, modules, compilation, importServices, diagnostics);

		// [Scan(SkipUnconstructable = true)] trades the AWT101 error for a skip-with-warning (AWT141) on matches
		// the container cannot construct. Scans without the opt-in, and explicit registrations, keep the error.
		PruneUnconstructableScanMatches(raw, containerSymbol, compilation, importServices, constraintRejected, diagnostics);

		(List<DecorateRegistration> decorators, List<OpenDecorateRegistration> openDecorators) =
			CollectDecorators(containerSymbol, modules, diagnostics);
		(List<CompositeRegistration> composites, List<OpenCompositeRegistration> openComposites) =
			CollectComposites(containerSymbol, modules, diagnostics);

		// Coalesce registrations by (service type, key): first per key wins, and registrations of the same
		// implementation share one instance. Conflicting lifetimes are AWT107; a duplicate key is AWT117.
		(List<ImplInfo> implOrder, Dictionary<ServiceKey, string> serviceToImpl, Dictionary<ServiceKey, List<string>> serviceMembers, List<ServiceKey> serviceMemberOrder, List<(string ServiceType, INamedTypeSymbol Symbol)> varianceCandidates, Dictionary<string, List<KeyedMember>> keyedMembers, List<string> keyedMemberOrder) =
			CoalesceByImplementation(raw, diagnostics);
		CoalescedGraph graph = new(serviceToImpl, implOrder, serviceMembers);

		// The closed generic service symbols the coalesced graph holds (recovered from the raw registrations,
		// which carry each closing's symbol), so an open decorator/composite can find every closing to expand onto.
		List<(string Display, INamedTypeSymbol Symbol)> closedServices = openDecorators.Count > 0 || openComposites.Count > 0
			? ClosedGenericServices(raw)
			: new List<(string, INamedTypeSymbol)>();

		// Open generic decorators: synthesize a closed [Decorate] per matching closing before building the chains,
		// so they flow through the same DecoratorChainBuilder as the closed form and interleave with it.
		if (openDecorators.Count > 0)
		{
			ExpandOpenDecorators(decorators, openDecorators, closedServices, graph, diagnostics);
		}

		// Decorator chains: for each [Decorate]d service, move the base impl(s) onto a synthetic key and register
		// each decorator as a chain link whose inner parameter redirects to the next-lower key. decoratorInner
		// keys each link's inner parameter to the link it wraps (consumed by ClassifyParameters).
		Dictionary<string, DecoratorInner> decoratorInner = new(StringComparer.Ordinal);
		if (decorators.Count > 0)
		{
			new DecoratorChainBuilder(containerSymbol, compilation, graph, decoratorInner, importServices, diagnostics)
				.Build(decorators);
		}

		// Open generic composites: synthesize a closed [Composite] per matching closing before building the
		// composites, so they flow through the same BuildComposites as the closed form (fronting the decorated members).
		if (openComposites.Count > 0)
		{
			ExpandOpenComposites(composites, openComposites, closedServices, graph, diagnostics);
		}

		// Composites: each [Composite<TComposite, TService>] registers the composite and makes it the public
		// winner for TService, excluded from its own collection so its collection parameter fans out to the
		// others. Runs after decorator chains so a composite fronts the decorated members.
		if (composites.Count > 0)
		{
			BuildComposites(composites, compilation, containerSymbol, graph, importServices, diagnostics);
		}

		List<InstanceModel> instances = new();
		List<LocationInfo?> instanceLocations = new();
		Dictionary<string, int> implToIndex = new(StringComparer.Ordinal);

		// Variance state: the candidate registrations plus the accumulators BuildInstance fills as it redirects
		// consumer parameters, drained after the instance loop below.
		VarianceState variance = new(varianceCandidates, compilation);

		// Contextual (WhenInjectedInto) bindings recorded up front, plus the set of context keys a consumer
		// dependency actually redirects to. A binding whose key stays absent is reported (AWT167) below.
		List<ConditionalRegistration> conditionals = CollectConditionalRegistrations(raw);
		HashSet<ServiceKey> consumedConditionals = new();

		BuildContext buildContext = new(containerSymbol, compilation, serviceToImpl, decoratorInner, wellKnown, constraintRejected, importServices, variance, consumedConditionals, diagnostics);

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

		// AWT167: a contextual registration whose named consumer never redirected to its context key (no unkeyed
		// direct dependency on the service) is never reached, so report it now that every instance is built.
		ReportUnappliedContextualBindings(conditionals, consumedConditionals, diagnostics);

		// Variance for collections (Part C): union every variance-compatible registration's members into each
		// requested closed-generic collection. Before the parameterized prune and edge building so the unioned
		// members drive analysis.
		UnionVarianceCollectionMembers(variance, serviceMembers, serviceMemberOrder);

		// Variance for top-level dispatch (Part B): expose each variance-redirected closed generic type as a
		// dispatch alias on its target instance, so an imperative Resolve routes it like the consumer parameter.
		ApplyVarianceDispatchAliases(variance, instances, implToIndex, serviceToImpl);

		// A parameterized ([Arg]) service is reachable only through its Func<TArg…, T> factory, never as a
		// collection member. Prune such implementations before they drive collection edges, AWT122 and emission.
		PruneParameterizedMembers(instances, serviceMembers, keyedMembers);

		Dictionary<int, List<int>> dependencies = BuildDependencyGraph(instances, serviceToImpl, implToIndex, serviceMembers, keyedMembers);
		Dictionary<int, List<int>> constructionDependencies = BuildConstructionGraph(instances, serviceToImpl, implToIndex, serviceMembers, keyedMembers);

		// The combined construction-plus-deferred graph vets deferred cycles (AWT145-147). With no deferred member
		// it IS the construction graph, so the extra pass is skipped.
		Dictionary<int, List<int>> combinedDependencies = AnyDeferredMember(instances)
			? BuildCombinedGraph(instances, serviceToImpl, implToIndex, serviceMembers, keyedMembers)
			: constructionDependencies;

		// Async taint: an instance is tainted if async-initialized, or if it reaches one through non-deferred
		// (Direct) edges. Relationship types launder the taint, so they contribute no edges to the dependency graph.
		bool[] tainted = PropagateAsyncTaint(instances, dependencies);
		for (int i = 0; i < instances.Count; i++)
		{
			if (tainted[i])
			{
				instances[i] = instances[i] with { IsAsyncTainted = true, };
			}
		}

		// The collection-resolvable membership, in first-seen service order, keeping only members that built
		// (a failed member is absent from implToIndex and dropped rather than emitted as a dangling call).
		List<ServiceMembers> collections = new();
		foreach (ServiceKey service in serviceMemberOrder)
		{
			string[] members = serviceMembers[service].Where(implToIndex.ContainsKey).ToArray();
			if (members.Length > 0)
			{
				collections.Add(new ServiceMembers(service.Service, service.Key, new EquatableArray<string>(members)));
			}
		}

		List<KeyedServiceMembers> keyedCollections = BuiltKeyedCollections(keyedMemberOrder, keyedMembers, implToIndex);

		// The variance candidates' service types (registration order), for the emitter's runtime variance
		// fallback: an imperative Resolve of a differently-closed generic no consumer requested is matched here.
		List<string> varianceCandidateTypes = new(variance.Candidates.Count);
		foreach ((string serviceType, INamedTypeSymbol _) in variance.Candidates)
		{
			varianceCandidateTypes.Add(serviceType);
		}

		return new GraphModel(instances, dependencies, constructionDependencies, combinedDependencies, serviceToImpl, implToIndex, instanceLocations, collections, varianceCandidateTypes, keyedCollections);
	}

	/// <summary>
	///     The keyed-collection membership, in first-seen service order, keeping only members that built (a
	///     failed member is absent from <paramref name="implToIndex" />). A service with none is absent, and
	///     injecting its dictionary yields an empty one.
	/// </summary>
	private static List<KeyedServiceMembers> BuiltKeyedCollections(
		List<string> keyedMemberOrder,
		Dictionary<string, List<KeyedMember>> keyedMembers,
		Dictionary<string, int> implToIndex)
	{
		List<KeyedServiceMembers> keyedCollections = new();
		foreach (string service in keyedMemberOrder)
		{
			KeyedMember[] members = keyedMembers[service].Where(member => implToIndex.ContainsKey(member.Implementation)).ToArray();
			if (members.Length > 0)
			{
				keyedCollections.Add(new KeyedServiceMembers(service, new EquatableArray<KeyedMember>(members)));
			}
		}

		return keyedCollections;
	}
}
