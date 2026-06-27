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
	private const string AttributeNamespace = "Awaiten";

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

		INamedTypeSymbol? disposableSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName("System.IDisposable");

		List<RawRegistration> raw = CollectRegistrations(containerSymbol);

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
			InstanceModel? instance = BuildInstance(info, containerSymbol, serviceToImpl, disposableSymbol, diagnostics);
			if (instance is not null)
			{
				implToIndex[info.ImplementationType] = instances.Count;
				instances.Add(instance);
				instanceLocations.Add(info.Location);
			}
		}

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

		foreach (RawRegistration registration in raw)
		{
			if (serviceToImpl.ContainsKey(registration.ServiceType))
			{
				continue;
			}

			if (!implInfos.TryGetValue(registration.ImplementationType, out ImplInfo? info))
			{
				info = new ImplInfo(
					registration.ImplementationType, registration.Implementation, registration.Lifetime, registration.Location);
				implInfos.Add(registration.ImplementationType, info);
				implOrder.Add(info);
			}
			else if (info.Lifetime != registration.Lifetime &&
			         reportedConflicts.Add(registration.ImplementationType))
			{
				// Same implementation registered under another service type but with a different lifetime;
				// coalescing keeps the first lifetime, so the contradiction is reported rather than dropped.
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ConflictingLifetime,
					registration.Location,
					new EquatableArray<string>([
						Display(registration.ImplementationType),
						info.Lifetime.ToString(),
						registration.Lifetime.ToString(),
					])));
			}

			serviceToImpl[registration.ServiceType] = registration.ImplementationType;
			info.ServiceTypes.Add(registration.ServiceType);
		}

		return (implOrder, serviceToImpl);
	}

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
			foreach (string paramService in instances[i].ConstructorParameterServiceTypes.AsArray())
			{
				if (serviceToImpl.TryGetValue(paramService, out string? depImpl) &&
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
		Dictionary<string, string> serviceToImpl,
		INamedTypeSymbol? disposableSymbol,
		List<DiagnosticInfo> diagnostics)
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

		IMethodSymbol? constructor = SelectConstructor(info.Symbol, containerSymbol, serviceToImpl.Keys);
		if (constructor is null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NoAccessibleConstructor,
				info.Location,
				new EquatableArray<string>([Display(info.ImplementationType),])));
			return null;
		}

		List<string> parameters = new();
		foreach (IParameterSymbol parameter in constructor.Parameters)
		{
			string parameterType = parameter.Type.ToDisplayString(FullyQualified);
			parameters.Add(parameterType);
			if (!serviceToImpl.ContainsKey(parameterType))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.MissingDependency,
					info.Location,
					new EquatableArray<string>([
						Display(info.ServiceTypes[0]),
						Display(info.ImplementationType),
						Display(parameterType),
					])));
			}
		}

		bool disposable = disposableSymbol is not null && ImplementsInterface(info.Symbol, disposableSymbol);

		// A disposable transient resolved from the container root is not released until the container is
		// disposed, so such instances accumulate; resolving it from a scope releases it with the scope.
		if (disposable && info.Lifetime == Lifetime.Transient)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.DisposableTransient,
				info.Location,
				new EquatableArray<string>([Display(info.ImplementationType),])));
		}

		return new InstanceModel(
			info.ImplementationType,
			info.Symbol.Name,
			info.Lifetime,
			new EquatableArray<string>(info.ServiceTypes.ToArray()),
			new EquatableArray<string>(parameters.ToArray()),
			disposable,
			info.Symbol.IsReferenceType);

		static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol @interface)
			=> type.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, @interface));
	}

	private static List<RawRegistration> CollectRegistrations(INamedTypeSymbol containerSymbol)
	{
		List<RawRegistration> result = new();
		foreach (AttributeData attribute in containerSymbol.GetAttributes())
		{
			if (attribute.AttributeClass is not { IsGenericType: true, } attributeClass)
			{
				continue;
			}

			if (attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
			{
				continue;
			}

			Lifetime? lifetime = attributeClass.Name switch
			{
				"SingletonAttribute" => Lifetime.Singleton,
				"TransientAttribute" => Lifetime.Transient,
				"ScopedAttribute" => Lifetime.Scoped,
				_ => null,
			};
			if (lifetime is null)
			{
				continue;
			}

			ImmutableArrayGuard(attributeClass.TypeArguments, out ITypeSymbol? implementation, out ITypeSymbol? service);
			if (implementation is null)
			{
				continue;
			}

			service ??= implementation;
			Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
			result.Add(new RawRegistration(
				service.ToDisplayString(FullyQualified),
				implementation.ToDisplayString(FullyQualified),
				lifetime.Value,
				(INamedTypeSymbol)implementation,
				LocationInfo.From(location)));
		}

		return result;

		static void ImmutableArrayGuard(
			ImmutableArray<ITypeSymbol> typeArguments,
			out ITypeSymbol? implementation,
			out ITypeSymbol? service)
		{
			implementation = typeArguments.Length > 0 ? typeArguments[0] : null;
			service = typeArguments.Length > 1 ? typeArguments[1] : null;
			if (implementation is not INamedTypeSymbol)
			{
				implementation = null;
			}
		}
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
			.Where(c => c.Parameters.All(p => registered.Contains(p.Type.ToDisplayString(FullyQualified))))
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

	private static void DetectCaptiveDependencies(
		List<InstanceModel> instances,
		Dictionary<int, List<int>> dependencies,
		List<LocationInfo?> instanceLocations,
		List<DiagnosticInfo> diagnostics)
	{
		for (int i = 0; i < instances.Count; i++)
		{
			if (instances[i].Lifetime != Lifetime.Singleton)
			{
				continue;
			}

			// Walk the singleton's graph through its transient dependencies (which are baked into it);
			// reaching a scoped service means the singleton would capture it for the container's life.
			HashSet<int> visited = new();
			Stack<int> stack = new(dependencies[i]);
			while (stack.Count > 0)
			{
				int node = stack.Pop();
				if (!visited.Add(node))
				{
					continue;
				}

				switch (instances[node].Lifetime)
				{
					case Lifetime.Scoped:
						diagnostics.Add(new DiagnosticInfo(
							Diagnostics.CaptiveDependency,
							instanceLocations[i],
							new EquatableArray<string>([
								Display(instances[i].ImplementationType),
								Display(instances[node].ServiceTypes.AsArray()[0]),
							])));
						break;
					case Lifetime.Transient:
						foreach (int next in dependencies[node])
						{
							stack.Push(next);
						}

						break;
				}
			}
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

	private static string Display(string fullyQualified)
		=> fullyQualified.StartsWith("global::", StringComparison.Ordinal)
			? fullyQualified.Substring("global::".Length)
			: fullyQualified;

	private sealed record RawRegistration(
		string ServiceType,
		string ImplementationType,
		Lifetime Lifetime,
		INamedTypeSymbol Implementation,
		LocationInfo? Location);

	private sealed class ImplInfo
	{
		public ImplInfo(string implementationType, INamedTypeSymbol symbol, Lifetime lifetime, LocationInfo? location)
		{
			ImplementationType = implementationType;
			Symbol = symbol;
			Lifetime = lifetime;
			Location = location;
			ServiceTypes = new List<string>();
		}

		public string ImplementationType { get; }
		public INamedTypeSymbol Symbol { get; }
		public Lifetime Lifetime { get; }
		public LocationInfo? Location { get; }
		public List<string> ServiceTypes { get; }
	}
}
