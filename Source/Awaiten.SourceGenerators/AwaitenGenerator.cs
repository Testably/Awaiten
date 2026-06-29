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
		DetectCycles(graph.Instances, graph.Dependencies, containerLocation, diagnostics);
		DetectCaptiveDependencies(graph.Instances, graph.Dependencies, graph.InstanceLocations, diagnostics);

		// AWT119/AWT120 (strict only): a synchronous Func<T>/Lazy<T>/Owned<T> relationship resolves its
		// target without awaiting initialization, so it may not target an async-tainted service. The
		// pragmatic SyncResolveAfterInit mode allows such resolution (after warm-up) and so is not reported.
		if (!syncResolveAfterInit)
		{
			DetectSynchronousAsyncResolution(
				graph.Instances, graph.Dependencies, graph.ServiceToImpl, graph.ImplToIndex, graph.InstanceLocations, diagnostics);
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
			syncResolveAfterInit);
	}

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
		INamedTypeSymbol? disposableSymbol = compilation.GetTypeByMetadataName("System.IDisposable");
		INamedTypeSymbol? asyncInitializableSymbol = compilation.GetTypeByMetadataName("Awaiten.IAsyncInitializable");

		List<RawRegistration> raw = ContainerRegistrations.Collect(containerSymbol);

		// Coalesce registrations by (service type, key): the first registration per key wins, and
		// registrations of the same implementation share one instance. Declaring one implementation with
		// two different lifetimes is reported as AWT107; two implementations under the same service type and
		// key as AWT117.
		(List<ImplInfo> implOrder, Dictionary<ServiceKey, string> serviceToImpl) = CoalesceByImplementation(raw, diagnostics);

		List<InstanceModel> instances = new();
		List<LocationInfo?> instanceLocations = new();
		Dictionary<string, int> implToIndex = new(StringComparer.Ordinal);

		// Validate each implementation, select its constructor and build the instance.
		foreach (ImplInfo info in implOrder)
		{
			cancellationToken.ThrowIfCancellationRequested();
			InstanceModel? instance = BuildInstance(info, containerSymbol, compilation, serviceToImpl, disposableSymbol, asyncInitializableSymbol, diagnostics);
			if (instance is not null)
			{
				implToIndex[info.ImplementationType] = instances.Count;
				instances.Add(instance);
				instanceLocations.Add(info.Location);
			}
		}

		Dictionary<int, List<int>> dependencies = BuildDependencyGraph(instances, serviceToImpl, implToIndex);

		// Async taint: an instance is tainted if its implementation is async-initialized, or if it reaches
		// one through non-deferred (Direct) edges. Relationship types (Func/Lazy/Owned/Arg) launder the
		// taint the same way they break cycles, so they contribute no edges to BuildDependencyGraph above.
		bool[] tainted = PropagateAsyncTaint(instances, dependencies);
		for (int i = 0; i < instances.Count; i++)
		{
			if (tainted[i])
			{
				instances[i] = instances[i] with { IsAsyncTainted = true, };
			}
		}

		return new GraphModel(instances, dependencies, serviceToImpl, implToIndex, instanceLocations);
	}

	/// <summary>
	///     Whether building the service at <paramref name="start" /> on its owner tracks a fresh disposable
	///     there: the service itself is disposable, or its construction transitively rebuilds one. The walk
	///     follows only direct <em>transient</em> dependency edges, because a scoped or singleton dependency is
	///     cached/shared (built at most once on the owner) and so is bounded, whereas a transient dependency is
	///     rebuilt - and, if disposable, re-tracked - on every construction. Used to decide whether a plain
	///     <c>Func&lt;…&gt;</c> over the service accumulates on the container root (AWT118 / strict withholding):
	///     a non-disposable transient that injects a disposable transient leaks just the same when built
	///     repeatedly through a root-bound factory.
	/// </summary>
	internal static bool BuildsFreshDisposable(
		IReadOnlyList<InstanceModel> instances,
		Dictionary<ServiceKey, int> serviceToIndex,
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
			if (instance.IsDisposable)
			{
				return true;
			}

			foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
			{
				// Only a direct (non-deferred) transient dependency is rebuilt as part of constructing this node;
				// a relationship/Owned/Arg parameter defers, and a scoped/singleton dependency is cached/shared.
				if (parameter.Kind == DependencyKind.Direct
				    && serviceToIndex.TryGetValue(KeyOf(parameter), out int dependency)
				    && instances[dependency].Lifetime == Lifetime.Transient)
				{
					stack.Push(dependency);
				}
			}
		}

		return false;
	}

	private static (List<ImplInfo> Order, Dictionary<ServiceKey, string> ServiceToImpl) CoalesceByImplementation(
		List<RawRegistration> raw,
		List<DiagnosticInfo> diagnostics)
	{
		List<ImplInfo> implOrder = new();
		Dictionary<string, ImplInfo> implInfos = new(StringComparer.Ordinal);
		Dictionary<ServiceKey, string> serviceToImpl = new();
		HashSet<string> reportedConflicts = new(StringComparer.Ordinal);
		HashSet<string> reportedProductionConflicts = new(StringComparer.Ordinal);

		foreach (RawRegistration registration in raw)
		{
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

			implInfos.TryGetValue(registration.ImplementationType, out ImplInfo? info);

			// A lifetime conflict is a property of the implementation, not of any single service type, so it
			// is checked before the per-service dedup below; otherwise re-registering the same service type
			// with a different lifetime would be skipped and the contradiction silently dropped. Coalescing
			// keeps the first lifetime, so the conflicting one is reported as AWT107 rather than ignored.
			if (info is not null && info.Lifetime != registration.Lifetime &&
			    reportedConflicts.Add(registration.ImplementationType))
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

			// Likewise the production: coalescing keeps the first, so a same-implementation re-registration
			// that constructs, factories or exposes it differently - or names a different factory/instance
			// member of the same kind - is reported as AWT111 rather than silently dropped. Checked before
			// the dedup for the same reason as the lifetime.
			if (info is not null && ConflictsWith(info, registration) &&
			    reportedProductionConflicts.Add(registration.ImplementationType))
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

			ServiceKey serviceKey = new(registration.ServiceType, registration.Key);
			if (serviceToImpl.TryGetValue(serviceKey, out string? existingImpl))
			{
				ReportDuplicateKey(registration, existingImpl, diagnostics);
				continue;
			}

			if (info is null)
			{
				info = new ImplInfo(
					registration.ImplementationType, registration.Implementation, registration.Lifetime,
					LocationInfo.From(registration.Location), registration.Production, registration.ProductionMember);
				implInfos.Add(registration.ImplementationType, info);
				implOrder.Add(info);
			}

			serviceToImpl[serviceKey] = registration.ImplementationType;
			info.Services.Add(serviceKey);
		}

		return (implOrder, serviceToImpl);
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

	// Dependency graph over instance indices, keeping only resolvable edges to built instances.
	private static Dictionary<int, List<int>> BuildDependencyGraph(
		List<InstanceModel> instances,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, int> implToIndex)
	{
		Dictionary<int, List<int>> dependencies = new();
		for (int i = 0; i < instances.Count; i++)
		{
			List<int> edges = new();
			foreach (ParameterModel parameter in instances[i].ConstructorParameters.AsArray())
			{
				// Relationship types (Func<T>/Lazy<T>) defer resolution, so they break cycles and do not
				// capture their target; only direct dependencies contribute graph edges.
				if (parameter.Kind != DependencyKind.Direct)
				{
					continue;
				}

				if (serviceToImpl.TryGetValue(KeyOf(parameter), out string? depImpl) &&
				    implToIndex.TryGetValue(depImpl, out int depIndex))
				{
					edges.Add(depIndex);
				}
			}

			dependencies[i] = edges;
		}

		return dependencies;
	}

	private static InstanceModel? BuildInstance(
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		Dictionary<ServiceKey, string> serviceToImpl,
		INamedTypeSymbol? disposableSymbol,
		INamedTypeSymbol? asyncInitializableSymbol,
		List<DiagnosticInfo> diagnostics)
	{
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
		IMethodSymbol? producer = SelectProducer(info, containerSymbol, compilation, serviceToImpl, diagnostics);
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
		List<ParameterModel> parameters = ClassifyParameters(producer, info, asyncFactory, serviceToImpl, diagnostics);

		// Disposability follows the type the container actually owns: a factory's produced type (which may
		// implement IDisposable behind a non-disposable service interface; for an async factory this is the
		// awaited T, not the Task), or the constructed implementation type. Using info.Symbol for a factory
		// would miss a DisposableX behind an IX and leak it.
		ITypeSymbol disposalType = info.Production == ProductionKind.Factory
			? ContainerRegistrations.ProducedType(producer.ReturnType, compilation)
			: info.Symbol;
		bool disposable = disposableSymbol is not null && ImplementsInterface(disposalType, disposableSymbol);

		// A factory's declared return type can hide a concrete IDisposable behind a non-disposable service
		// interface (or base class), which the static `disposable` flag above misses. When that is possible -
		// the declared type is not itself disposable yet a subtype could be (an interface or a non-sealed
		// class) - the emitter tracks the realized instance for disposal behind a runtime `is IDisposable`
		// test instead. A sealed declared type that is not IDisposable cannot hide one, so it needs no check
		// (and a runtime `is IDisposable` against it would not even compile). Constructed and pre-built
		// Instance production never lie: info.Symbol is the concrete type, and an Instance is not owned.
		bool runtimeDisposalCheck = info.Production == ProductionKind.Factory
		                            && !disposable
		                            && CouldHideDisposable(disposalType);

		// Async initialization follows the type the container actually owns - a factory's concrete return type
		// (which may implement IAsyncInitializable behind a non-async service interface) or the constructed
		// implementation type - mirroring the disposal-type choice above. A pre-built Instance is returned
		// early above and is never initialized here (the caller owns it). An async factory is async-tainted
		// regardless of whether its produced type implements IAsyncInitializable: its result is reached only by
		// awaiting the Task (see the IsAsyncFactory seed in PropagateAsyncTaint). When the produced type IS
		// IAsyncInitializable, the container additionally awaits its InitializeAsync after the factory completes.
		bool asyncInit = asyncInitializableSymbol is not null && ImplementsInterface(disposalType, asyncInitializableSymbol);

		// Best-effort lint (AWT106): a synchronous factory whose declared return type hides the asynchronous
		// initialization its body provably produces. The container reads async-init taint off producer.ReturnType
		// (above), so a concrete IAsyncInitializable returned behind a plainer interface is never initialized.
		// An async Task<T>/ValueTask<T> factory owns its own initialization (the container awaits the factory),
		// and a hidden IDisposable is disposed at runtime via RuntimeDisposalCheck - neither is reported.
		if (info.Production == ProductionKind.Factory && !asyncFactory)
		{
			ReportFactoryHidingAsyncInitialization(producer, compilation, asyncInitializableSymbol, diagnostics);
		}

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
			RuntimeDisposalCheck: runtimeDisposalCheck);

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

		IMethodSymbol? constructor = SelectConstructor(info.Symbol, containerSymbol, serviceToImpl.Keys.Select(k => k.Service));
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
	///     Classifies the producer's parameters (a constructor's or a factory method's) and reports
	///     <see cref="Diagnostics.MissingDependency">AWT101</see> for any non-<c>[Arg]</c> parameter whose
	///     service type is not registered. A runtime argument (<c>[Arg]</c>) is supplied at resolve time, so
	///     it is never a missing dependency.
	/// </summary>
	private static List<ParameterModel> ClassifyParameters(
		IMethodSymbol producer,
		ImplInfo info,
		bool asyncFactory,
		Dictionary<ServiceKey, string> serviceToImpl,
		List<DiagnosticInfo> diagnostics)
	{
		List<ParameterModel> parameters = new();
		foreach (IParameterSymbol parameter in producer.Parameters)
		{
			ParameterModel parameterModel = ClassifyParameter(parameter, asyncFactory);
			parameters.Add(parameterModel);

			// A CancellationToken is forwarded from the resolve-time token, not resolved from the graph (like
			// [Arg]), so it is never a missing dependency.
			if (parameterModel.Kind is not (DependencyKind.Arg or DependencyKind.CancellationToken)
			    && !serviceToImpl.ContainsKey(KeyOf(parameterModel)))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.MissingDependency,
					info.Location,
					new EquatableArray<string>([
						Display(info.Services[0].Service),
						Display(info.ImplementationType),
						DisplayKeyed(parameterModel.ServiceType, parameterModel.Key),
					])));
			}
		}

		return parameters;
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
			new EquatableArray<string>([Display(info.Services[0].Service), info.ProductionMember!,])));
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
			new EquatableArray<string>([Display(info.Services[0].Service), info.ProductionMember!,])));
	}

	private static IMethodSymbol? SelectConstructor(
		INamedTypeSymbol implementation,
		INamedTypeSymbol containerSymbol,
		IEnumerable<string> registeredServices)
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
				ParameterModel parameter = ClassifyParameter(p, asyncFactory: false);
				return parameter.Kind == DependencyKind.Arg || registered.Contains(parameter.ServiceType);
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

		if (HasArgAttribute(parameter))
		{
			return new ParameterModel(parameter.Type.ToDisplayString(FullyQualified), DependencyKind.Arg, Location: location);
		}

		// An asynchronous factory's CancellationToken parameter is not resolved from the graph: the container
		// forwards the resolve-time token (the async creator's cancellationToken). Limited to async factories -
		// only they are constructed on the async path where that token exists; a synchronous factory (or a
		// constructor) has no ambient token to forward, so its CancellationToken stays an ordinary dependency
		// and is reported as AWT101 when unregistered rather than silently receiving default. An [Arg]
		// CancellationToken is handled above as a caller-supplied runtime argument and is left untouched.
		if (asyncFactory
		    && parameter.Type is INamedTypeSymbol { Name: "CancellationToken", } token
		    && token.ContainingNamespace?.ToDisplayString() == "System.Threading")
		{
			return new ParameterModel(
				parameter.Type.ToDisplayString(FullyQualified), DependencyKind.CancellationToken, Location: location);
		}

		// A [FromKey] selects the keyed registration of the dependency's service type, whether it is required
		// directly, deferred behind a Func<T>/Lazy<T>, or wrapped in an Owned<T> handle - the service type is
		// the same, only the delivery differs.
		string? key = FromKey(parameter);

		// A bare Owned<T> dependency: resolve T into a throwaway scope and hand the caller the disposal handle.
		if (IsOwned(parameter.Type, out ITypeSymbol ownedInner))
		{
			return new ParameterModel(ownedInner.ToDisplayString(FullyQualified), DependencyKind.Owned, Key: key, Location: location);
		}

		if (parameter.Type is INamedTypeSymbol { IsGenericType: true, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System"
		    && ClassifyRelationship(named, key, location) is { } relationship)
		{
			return relationship;
		}

		// A direct dependency, optionally selecting a keyed registration with [FromKey].
		return new ParameterModel(
			parameter.Type.ToDisplayString(FullyQualified), DependencyKind.Direct, Key: key, Location: location);
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
			return new ParameterModel(
				named.TypeArguments[0].ToDisplayString(FullyQualified), DependencyKind.Lazy, Key: key, Location: location);
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

	private static string? FromKey(IParameterSymbol parameter)
	{
		foreach (AttributeData attribute in parameter.GetAttributes())
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
							Display(instances[i].ImplementationType),
							parameter.Kind.ToString(),
							Display(instances[target].ImplementationType),
						])));
				}
				else
				{
					string path = AsyncTaintPath(instances, dependencies, target);
					diagnostics.Add(new DiagnosticInfo(
						Diagnostics.AsyncDependencyOnSyncPath,
						location,
						new EquatableArray<string>([
							Display(instances[i].ImplementationType),
							path,
						])));
				}
			}
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

		return string.Join(" -> ", chain.Select(index => Display(instances[index].ImplementationType)));
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

	private static bool HasArgAttribute(IParameterSymbol parameter)
	{
		foreach (AttributeData attribute in parameter.GetAttributes())
		{
			if (attribute.AttributeClass is { Name: "ArgAttribute", } attributeClass
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

			// A parameterized service is reachable only through a synchronous Func<TArg…, T>, which returns the
			// service directly and so cannot await it. Combining [Arg] with an async-taint source (an
			// IAsyncInitializable implementation, or an asynchronous Task<T> / ValueTask<T> factory) would
			// therefore either hand back an uninitialized/unawaited instance (SyncResolveAfterInit) or be
			// silently unreachable (strict) - neither has a correct resolution path until an async parameterized
			// factory relationship (Func<TArg…, Task<T>>) exists. Reported in both modes (it is not a
			// sync-vs-async resolution choice but an unsupported combination).
			if (instance.IsParameterized && instance.IsAsyncSource)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ParameterizedAsyncInitialization,
					location,
					new EquatableArray<string>([Display(instance.ImplementationType),])));
			}

			foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
			{
				// Guard the implToIndex lookup the same way BuildDependencyGraph does: serviceToImpl can name an
				// implementation whose BuildInstance failed (so it is absent from implToIndex), and an unguarded
				// indexer would crash the generator (KeyNotFoundException) instead of surfacing the real
				// registration error (e.g. AWT103).
				if (parameter.Kind == DependencyKind.Arg
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
	///     (<paramref name="expected" />): a <c>Func&lt;TArg…, T&gt;</c> must request exactly them (AWT113);
	///     a plain or <c>Lazy&lt;T&gt;</c> dependency cannot supply them at all, so it must instead be a
	///     <c>Func</c> when the target is parameterized (AWT115).
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

		if (parameter.Kind == DependencyKind.Func)
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
				new EquatableArray<string>([Display(parameter.ServiceType), Display(consumer.ImplementationType),])));
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
							Display(instances[singleton].ImplementationType),
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
		// parameter selects. Falls back to the first service key if no parameter matches (it always should).
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

			return dependencyServices[0];
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

			string rendered = string.Join(" -> ", cycle.Select(index => Display(instances[index].ImplementationType)));
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
	}
}
