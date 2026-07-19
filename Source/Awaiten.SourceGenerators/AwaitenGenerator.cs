using System.Text;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
	private const string ModuleAttributeName = "Awaiten.ModuleAttribute";
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

		// A second root: a [Module] that declares a [Scan] self-compiles it, emitting a factory + registration per
		// match into its own partial (BuildModuleModel returns null for a plain module, which emits nothing).
		IncrementalValuesProvider<ModuleScanModel> moduleModels = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				ModuleAttributeName,
				static (node, _) => node is ClassDeclarationSyntax,
				static (ctx, ct) => BuildModuleModel(ctx, ct))
			.Where(static model => model is not null)
			.Select(static (model, _) => model!);

		context.RegisterSourceOutput(moduleModels, static (spc, model) =>
		{
			foreach (DiagnosticInfo diagnostic in model.Diagnostics.AsArray())
			{
				spc.ReportDiagnostic(diagnostic.ToDiagnostic());
			}

			// An expanded module always emits, even with zero factories: the partial then carries just the
			// [GeneratedScanExpansion] marker, which a consuming container needs to tell "the scan matched
			// nothing" from "the scan was never expanded" (AWT154). A module rejected before expansion
			// (AWT152/AWT194/AWT201) emits nothing; its error already fails the build.
			if (model.Expanded)
			{
				spc.AddSource(model.HintName, SourceText.From(Sources.EmitModule(model), Encoding.UTF8));
			}
		});
	}

	/// <summary>
	///     Builds the <see cref="ModuleScanModel" /> for a <c>[Module]</c> that declares a <c>[Scan]</c> (returning
	///     <see langword="null" /> for a module without one, which self-compiles nothing). The module must be
	///     non-generic (AWT201) and <c>partial</c> to receive the generated factories and registration attributes
	///     (AWT194); when it is, its scans are expanded into factories in its own build (see
	///     <see cref="CollectModuleScanFactories" />).
	/// </summary>
	private static ModuleScanModel? BuildModuleModel(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
	{
		if (context.TargetSymbol is not INamedTypeSymbol moduleSymbol)
		{
			return null;
		}

		// Only a module that declares a [Scan] self-compiles; a plain [Module] carries no generated code (and pays
		// no analysis cost beyond this check).
		if (!HasAwaitenAttribute(moduleSymbol.GetAttributes(), "ScanAttribute"))
		{
			return null;
		}

		Compilation compilation = context.SemanticModel.Compilation;
		List<DiagnosticInfo> diagnostics = new();

		// The module must be partial for the generator to add the factories and registration attributes. Every part
		// of a partial type carries the partial modifier, so the declaration bearing the [Module] attribute suffices.
		bool isPartial = context.TargetNode is ClassDeclarationSyntax declaration
		                 && declaration.Modifiers.Any(SyntaxKind.PartialKeyword);

		List<ModuleFactory> factories = new();
		bool expanded = false;
		if (HasOpenTypeParameters(moduleSymbol))
		{
			// AWT201: a generic module (or one nested in a generic type) has no single closed type a consumer could
			// import, and re-opening it as a bare-named partial would emit an unrelated non-generic class instead.
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.GenericModuleScan,
				LocationInfo.From(moduleSymbol.Locations.FirstOrDefault()),
				new EquatableArray<string>([Display(moduleSymbol.ToDisplayString(FullyQualified)),])));
		}
		else if (!moduleSymbol.IsStatic)
		{
			// AWT152, reported here in the module's own build (the import-side check only reaches a module some
			// container in the same solution imports): the generated partial re-opens the module as static, so
			// emitting into a non-static class would surface as a raw partial-modifier compiler error instead.
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NonStaticModule,
				LocationInfo.From(moduleSymbol.Locations.FirstOrDefault()),
				new EquatableArray<string>([Display(moduleSymbol.ToDisplayString(FullyQualified)),])));
		}
		else if (!isPartial)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NonPartialModuleScan,
				LocationInfo.From(moduleSymbol.Locations.FirstOrDefault()),
				new EquatableArray<string>([Display(moduleSymbol.ToDisplayString(FullyQualified)),])));
		}
		else
		{
			factories = CollectModuleScanFactories(moduleSymbol, compilation, diagnostics);
			expanded = true;
		}

		string? moduleNamespace = moduleSymbol.ContainingNamespace is { IsGlobalNamespace: false, } ns
			? ns.ToDisplayString()
			: null;

		List<TypeDeclaration> containingTypes = new();
		for (INamedTypeSymbol? outer = moduleSymbol.ContainingType; outer is not null; outer = outer.ContainingType)
		{
			containingTypes.Insert(0, new TypeDeclaration(KeywordOf(outer), outer.Name));
		}

		string typePath = containingTypes.Count > 0
			? $"{string.Join("+", containingTypes.Select(t => t.Name))}+{moduleSymbol.Name}"
			: moduleSymbol.Name;
		string hintName = moduleNamespace is null
			? $"Awaiten.ModuleScan.{typePath}.g.cs"
			: $"Awaiten.ModuleScan.{moduleNamespace}.{typePath}.g.cs";

		return new ModuleScanModel(
			moduleNamespace,
			new EquatableArray<TypeDeclaration>(containingTypes.ToArray()),
			moduleSymbol.Name,
			hintName,
			expanded,
			new EquatableArray<ModuleFactory>(factories.ToArray()),
			new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
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

		// AWT186: an Owned<T> relationship over a requesting-type factory has no owner scope to build into, so the
		// combination is unsupported. Reported regardless of lifetime safety - it is a structural incompatibility,
		// not an async concern - so it is outside the SyncResolveAfterInit gate below.
		DetectOwnedOverRequestingTypeFactory(
			graph.Instances, graph.ServiceToImpl, graph.ImplToIndex, graph.InstanceLocations, diagnostics);

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
	///     The container's external-resolution surface, consulted during constructor selection and open-generic/scan
	///     seeding: the blanket <c>[ImportServices]</c> fall-through (<see cref="ImportServices" />) plus the per-type
	///     <c>[ImportService&lt;T&gt;]</c> declarations (<see cref="ServiceTypes" />). The two are computed together
	///     and bundled so they travel as one through the coalescing-phase helpers that decide whether a constructor
	///     parameter is satisfiable, keeping their parameter lists short.
	/// </summary>
	internal readonly record struct ExternalSurface(bool ImportServices, HashSet<string> ServiceTypes);

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
		ExternalSurface External,
		VarianceState Variance,
		HashSet<ServiceKey> ConsumedConditionals,
		Dictionary<string, List<InjectPropertyEntry>> InjectProperties,
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

		// The imported modules, resolved once. Import validation (AWT149-152) is reported while collecting.
		List<ImportedModule> modules = CollectImportedModules(containerSymbol, diagnostics);

		// [ImportServices]: any otherwise-unresolved direct dependency falls through to the external provider
		// instead of AWT101. Computed up front because it widens constructor selection everywhere. A module can
		// contribute it too.
		bool importServices = ContainerImportsServices(containerSymbol)
		                      || modules.Any(module => HasAwaitenAttribute(module.Symbol.GetAttributes(), "ImportServicesAttribute"));

		// [ImportService<T>]: the typed counterpart of the blanket flag above, honored on the container and any
		// imported module. Each declared type routes every unregistered dependency of that type - keyed or not,
		// whether a constructor parameter, factory parameter or [Inject] property - to the external provider, while
		// every other unresolved dependency keeps the AWT101 check. Bundled with the blanket flag as the container's
		// external-resolution surface, threaded through the coalescing phase that decides constructor satisfiability.
		HashSet<string> externalServiceTypes = CollectExternalServiceTypes(containerSymbol, modules);
		ExternalSurface external = new(importServices, externalServiceTypes);

		// Collect also expands [Scan]s into overridable registrations (IsScan), ordered after the explicit ones
		// so an explicit registration wins single resolution while every match still joins its collection.
		(List<RawRegistration> raw, HashSet<string> constraintRejected) = Collect(containerSymbol, modules, compilation, external, diagnostics);

		// [Scan(SkipUnconstructable = true)] trades the AWT101 error for a skip-with-warning (AWT141) on matches
		// the container cannot construct. Scans without the opt-in, and explicit registrations, keep the error.
		PruneUnconstructableScanMatches(raw, containerSymbol, compilation, external, constraintRejected, diagnostics);

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
		// so they flow through the same DecoratorChainBuilder as the closed form and interleave with it. A no-op
		// when there are none (closedServices is then empty and the expansion has nothing to iterate).
		ExpandOpenDecorators(decorators, openDecorators, closedServices, graph, diagnostics);

		// Decorator chains: for each [Decorate]d service, move the base impl(s) onto a synthetic key and register
		// each decorator as a chain link whose inner parameter redirects to the next-lower key. decoratorInner
		// keys each link's inner parameter to the link it wraps (consumed by ClassifyParameters).
		Dictionary<string, DecoratorInner> decoratorInner = new(StringComparer.Ordinal);
		if (decorators.Count > 0)
		{
			new DecoratorChainBuilder(containerSymbol, compilation, graph, decoratorInner, external, diagnostics)
				.Build(decorators);
		}

		// Open generic composites: synthesize a closed [Composite] per matching closing before building the
		// composites, so they flow through the same BuildComposites as the closed form (fronting the decorated
		// members). A no-op when there are none, as with the open decorators above.
		ExpandOpenComposites(composites, openComposites, closedServices, graph, diagnostics);

		// Composites: each [Composite<TComposite, TService>] registers the composite and makes it the public
		// winner for TService, excluded from its own collection so its collection parameter fans out to the
		// others. Runs after decorator chains so a composite fronts the decorated members.
		if (composites.Count > 0)
		{
			BuildComposites(composites, compilation, containerSymbol, graph, external, diagnostics);
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

		// AWT175: a type declared [ImportService<T>] must not also be registered on the container - it is either
		// host-owned or Awaiten-owned, not both. Reported now that the coalesced service->impl map is known.
		ReportContradictingExternalServices(externalServiceTypes, serviceToImpl, containerSymbol, diagnostics);

		// Container-side property injection: [InjectProperty<TImpl>] entries keyed by implementation type, so
		// BuildInstance fills them wherever that implementation is constructed (including [Scan]-registered types).
		Dictionary<string, List<InjectPropertyEntry>> injectProperties = CollectInjectProperties(containerSymbol, diagnostics);

		BuildContext buildContext = new(containerSymbol, compilation, serviceToImpl, decoratorInner, wellKnown, constraintRejected, external, variance, consumedConditionals, injectProperties, diagnostics);

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

		// AWT176: a declared [ImportService<T>] whose type no graph edge ever routed externally is dead - a mistyped
		// or stale declaration. Reported once every instance is built, so every dependency has had the chance to
		// route. A type flagged AWT175 (registered, so resolved from the graph rather than externally) is excluded.
		ReportUnconsumedExternalServices(externalServiceTypes, serviceToImpl, instances, containerSymbol, diagnostics);

		// AWT180: an [InjectProperty<TImpl>] entry whose implementation type matches no registration is never
		// applied, reported now that every registered implementation has been seen.
		ReportUnmatchedInjectProperties(injectProperties, implOrder, diagnostics);

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
				keyedCollections.Add(new KeyedServiceMembers(service, new EquatableArray<KeyedMember>(members), DictionaryKeyType(members)));
			}
		}

		return keyedCollections;
	}
}
