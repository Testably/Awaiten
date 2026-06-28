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
		INamedTypeSymbol? disposableSymbol = compilation.GetTypeByMetadataName("System.IDisposable");

		List<RawRegistration> raw = ContainerRegistrations.Collect(containerSymbol);

		List<DiagnosticInfo> diagnostics = new();

		// Coalesce registrations by implementation: the first registration per service type wins, and
		// registrations of the same implementation share one instance. Declaring one implementation with
		// two different lifetimes is reported as AWT107.
		(List<ImplInfo> implOrder, Dictionary<string, string> serviceToImpl) = CoalesceByImplementation(raw, diagnostics);

		List<InstanceModel> instances = new();
		List<LocationInfo?> instanceLocations = new();
		Dictionary<string, int> implToIndex = new(StringComparer.Ordinal);

		// Validate each implementation, select its constructor and build the instance.
		foreach (ImplInfo info in implOrder)
		{
			cancellationToken.ThrowIfCancellationRequested();
			InstanceModel? instance = BuildInstance(info, containerSymbol, compilation, serviceToImpl, disposableSymbol, diagnostics);
			if (instance is not null)
			{
				implToIndex[info.ImplementationType] = instances.Count;
				instances.Add(instance);
				instanceLocations.Add(info.Location);
			}
		}

		ValidateRuntimeArguments(instances, instanceLocations, serviceToImpl, implToIndex, diagnostics);

		Dictionary<int, List<int>> dependencies = BuildDependencyGraph(instances, serviceToImpl, implToIndex);

		LocationInfo? containerLocation = LocationInfo.From(containerSymbol.Locations.FirstOrDefault());
		DetectCycles(instances, dependencies, containerLocation, diagnostics);
		DetectCaptiveDependencies(instances, dependencies, instanceLocations, diagnostics);

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
			new EquatableArray<InstanceModel>(instances.ToArray()),
			new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
	}

	private static (List<ImplInfo> Order, Dictionary<string, string> ServiceToImpl) CoalesceByImplementation(
		List<RawRegistration> raw,
		List<DiagnosticInfo> diagnostics)
	{
		List<ImplInfo> implOrder = new();
		Dictionary<string, ImplInfo> implInfos = new(StringComparer.Ordinal);
		Dictionary<string, string> serviceToImpl = new(StringComparer.Ordinal);
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

			if (serviceToImpl.ContainsKey(registration.ServiceType))
			{
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

			serviceToImpl[registration.ServiceType] = registration.ImplementationType;
			info.ServiceTypes.Add(registration.ServiceType);
		}

		return (implOrder, serviceToImpl);
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
		Dictionary<string, string> serviceToImpl,
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

				if (serviceToImpl.TryGetValue(parameter.ServiceType, out string? depImpl) &&
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
		Dictionary<string, string> serviceToImpl,
		INamedTypeSymbol? disposableSymbol,
		List<DiagnosticInfo> diagnostics)
	{
		// A pre-built Instance is handed back from a container member, never constructed here. The
		// container does not own it, so it is not disposed; the registered type may legitimately be an
		// interface (so the not-instantiable check is skipped) and it contributes no graph edges.
		if (info.Production == ProductionKind.Instance)
		{
			bool memberIsStatic = ValidateInstanceMember(containerSymbol, info, compilation, diagnostics);
			return new InstanceModel(
				info.ImplementationType,
				info.Symbol.Name,
				info.Lifetime,
				new EquatableArray<string>(info.ServiceTypes.ToArray()),
				new EquatableArray<ParameterModel>([]),
				false,
				info.Symbol.IsReferenceType,
				ProductionKind.Instance,
				info.ProductionMember,
				memberIsStatic);
		}

		// Select the producer: a container method (Factory) or the implementation's constructor (the
		// default). A factory produces the instance, so the registered type may be an interface and is
		// not subject to the not-instantiable check that a constructed type is.
		IMethodSymbol? producer;
		if (info.Production == ProductionKind.Factory)
		{
			producer = ResolveFactory(containerSymbol, info, compilation, diagnostics);
			if (producer is null)
			{
				return null;
			}
		}
		else
		{
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

			producer = SelectConstructor(info.Symbol, containerSymbol, serviceToImpl.Keys);
			if (producer is null)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.NoAccessibleConstructor,
					info.Location,
					new EquatableArray<string>([Display(info.ImplementationType),])));
				return null;
			}
		}

		// A factory's parameters resolve from the graph exactly like a constructor's.
		List<ParameterModel> parameters = ClassifyParameters(producer, info, serviceToImpl, diagnostics);

		// Disposability follows the type the container actually owns: a factory's concrete return type
		// (which may implement IDisposable behind a non-disposable service interface), or the constructed
		// implementation type. Using info.Symbol for a factory would miss a DisposableX behind an IX and
		// leak it.
		ITypeSymbol disposalType = info.Production == ProductionKind.Factory ? producer.ReturnType : info.Symbol;
		bool disposable = disposableSymbol is not null && ImplementsInterface(disposalType, disposableSymbol);

		return new InstanceModel(
			info.ImplementationType,
			info.Symbol.Name,
			info.Lifetime,
			new EquatableArray<string>(info.ServiceTypes.ToArray()),
			new EquatableArray<ParameterModel>(parameters.ToArray()),
			disposable,
			info.Symbol.IsReferenceType,
			info.Production,
			info.ProductionMember,
			producer is { IsStatic: true, });

		static bool ImplementsInterface(ITypeSymbol type, INamedTypeSymbol @interface)
		{
			return SymbolEqualityComparer.Default.Equals(type, @interface)
			       || type.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, @interface));
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
		Dictionary<string, string> serviceToImpl,
		List<DiagnosticInfo> diagnostics)
	{
		List<ParameterModel> parameters = new();
		foreach (IParameterSymbol parameter in producer.Parameters)
		{
			ParameterModel parameterModel = ClassifyParameter(parameter);
			parameters.Add(parameterModel);

			if (parameterModel.Kind != DependencyKind.Arg && !serviceToImpl.ContainsKey(parameterModel.ServiceType))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.MissingDependency,
					info.Location,
					new EquatableArray<string>([
						Display(info.ServiceTypes[0]),
						Display(info.ImplementationType),
						Display(parameterModel.ServiceType),
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
			new EquatableArray<string>([Display(info.ServiceTypes[0]), info.ProductionMember!,])));
		return null;
	}

	/// <summary>
	///     Validates an <c>Instance</c> registration against the named container member, reporting
	///     <see cref="Diagnostics.InvalidInstance">AWT109</see> when no accessible field or property of
	///     that name (on the container or an accessible base type) holds the registered type.
	/// </summary>
	private static bool ValidateInstanceMember(
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
				return member.IsStatic;
			}
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.InvalidInstance,
			info.Location,
			new EquatableArray<string>([Display(info.ServiceTypes[0]), info.ProductionMember!,])));
		return false;
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
				ParameterModel parameter = ClassifyParameter(p);
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
	private static ParameterModel ClassifyParameter(IParameterSymbol parameter)
	{
		if (HasArgAttribute(parameter))
		{
			return new ParameterModel(parameter.Type.ToDisplayString(FullyQualified), DependencyKind.Arg);
		}

		if (parameter.Type is INamedTypeSymbol { IsGenericType: true, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System")
		{
			if (named is { Name: "Lazy", TypeArguments.Length: 1, } && !IsRelationshipType(named.TypeArguments[0]))
			{
				return new ParameterModel(named.TypeArguments[0].ToDisplayString(FullyQualified), DependencyKind.Lazy);
			}

			if (named is { Name: "Func", TypeArguments.Length: >= 1, })
			{
				// Func<T> defers resolution; Func<TArg…, T> additionally supplies runtime arguments (the
				// leading type arguments) to the produced service's [Arg]-marked parameters.
				ITypeSymbol[] typeArgs = named.TypeArguments.ToArray();
				ITypeSymbol service = typeArgs[typeArgs.Length - 1];
				if (!IsRelationshipType(service))
				{
					string[] argTypes = typeArgs.Take(typeArgs.Length - 1)
						.Select(t => t.ToDisplayString(FullyQualified))
						.ToArray();
					return new ParameterModel(
						service.ToDisplayString(FullyQualified), DependencyKind.Func, new EquatableArray<string>(argTypes));
				}
			}
		}

		return new ParameterModel(parameter.Type.ToDisplayString(FullyQualified), DependencyKind.Direct);
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
	///     The ordered runtime-argument types of an instance: the service types of its <c>[Arg]</c>-marked
	///     constructor parameters, in declaration order.
	/// </summary>
	private static string[] ArgTypesOf(InstanceModel instance)
		=> instance.ConstructorParameters.AsArray()
			.Where(p => p.Kind == DependencyKind.Arg)
			.Select(p => p.ServiceType)
			.ToArray();

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
		Dictionary<string, string> serviceToImpl,
		Dictionary<string, int> implToIndex,
		List<DiagnosticInfo> diagnostics)
	{
		for (int i = 0; i < instances.Count; i++)
		{
			InstanceModel instance = instances[i];
			LocationInfo? location = instanceLocations[i];

			if (ArgTypesOf(instance).Length > 0 && instance.Lifetime != Lifetime.Transient)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ParameterizedLifetime,
					location,
					new EquatableArray<string>([Display(instance.ImplementationType), instance.Lifetime.ToString(),])));
			}

			foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
			{
				if (parameter.Kind == DependencyKind.Arg
				    || !serviceToImpl.TryGetValue(parameter.ServiceType, out string? targetImpl))
				{
					continue;
				}

				ValidateDependency(
					instance, parameter, ArgTypesOf(instances[implToIndex[targetImpl]]), location, diagnostics);
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
		LocationInfo? location,
		List<DiagnosticInfo> diagnostics)
	{
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
						string.Join(", ", requested.Select(Display)),
						string.Join(", ", expected.Select(Display)),
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
					diagnostics.Add(new DiagnosticInfo(
						Diagnostics.CaptiveDependency,
						instanceLocations[singleton],
						new EquatableArray<string>([
							Display(instances[singleton].ImplementationType),
							Display(ReferencedService(instances[parent], instances[node])),
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

		// The service type the parent's constructor used to reach this dependency - the alias the developer
		// wrote - which is the one of the dependency's service types that appears among the parent's
		// parameters. Falls back to the first service type if no parameter matches (it always should).
		static string ReferencedService(InstanceModel parent, InstanceModel dependency)
		{
			string[] dependencyServices = dependency.ServiceTypes.AsArray();
			return parent.ConstructorParameters.AsArray()
				.Select(p => p.ServiceType)
				.FirstOrDefault(dependencyServices.Contains) ?? dependencyServices[0];
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
	private static string Display(string fullyQualified)
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
			ServiceTypes = new List<string>();
		}

		public string ImplementationType { get; }
		public INamedTypeSymbol Symbol { get; }
		public Lifetime Lifetime { get; }
		public LocationInfo? Location { get; }
		public ProductionKind Production { get; }
		public string? ProductionMember { get; }
		public List<string> ServiceTypes { get; }
	}
}
