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

		// Pass 1: coalesce registrations by implementation. The first registration per service type
		// wins, and registrations of the same implementation share one instance.
		List<string> implOrder = new();
		Dictionary<string, ImplInfo> implInfos = new(StringComparer.Ordinal);
		Dictionary<string, string> serviceToImpl = new(StringComparer.Ordinal);

		foreach (RawRegistration registration in raw)
		{
			if (serviceToImpl.ContainsKey(registration.ServiceType))
			{
				continue;
			}

			if (!implInfos.TryGetValue(registration.ImplementationType, out ImplInfo? info))
			{
				info = new ImplInfo(registration.Implementation, registration.Lifetime, registration.Location);
				implInfos.Add(registration.ImplementationType, info);
				implOrder.Add(registration.ImplementationType);
			}

			serviceToImpl[registration.ServiceType] = registration.ImplementationType;
			info.ServiceTypes.Add(registration.ServiceType);
		}

		List<DiagnosticInfo> diagnostics = new();
		List<InstanceModel> instances = new();
		Dictionary<string, int> implToIndex = new(StringComparer.Ordinal);

		// Pass 2: validate each implementation, select its constructor and build the instance.
		foreach (string impl in implOrder)
		{
			cancellationToken.ThrowIfCancellationRequested();
			BuildInstance(impl, implInfos[impl], containerSymbol, serviceToImpl, disposableSymbol,
				instances, implToIndex, diagnostics);
		}

		// Dependency graph over instance indices (resolvable edges to built instances only).
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

		LocationInfo? containerLocation = LocationInfo.From(containerSymbol.Locations.FirstOrDefault());
		DetectCycles(instances, dependencies, containerLocation, diagnostics);

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

	private static void BuildInstance(
		string impl,
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Dictionary<string, string> serviceToImpl,
		INamedTypeSymbol? disposableSymbol,
		List<InstanceModel> instances,
		Dictionary<string, int> implToIndex,
		List<DiagnosticInfo> diagnostics)
	{
		// An abstract type or interface cannot be constructed; reject it instead of emitting a 'new'
		// against it (which would fail to compile in the generated source).
		if (info.Symbol.IsAbstract || info.Symbol.TypeKind == TypeKind.Interface)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NotInstantiable,
				info.Location,
				new EquatableArray<string>([Display(impl),])));
			return;
		}

		IMethodSymbol? constructor = SelectConstructor(info.Symbol, containerSymbol, serviceToImpl.Keys);
		if (constructor is null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NoAccessibleConstructor,
				info.Location,
				new EquatableArray<string>([Display(impl),])));
			return;
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
						Display(impl),
						Display(parameterType),
					])));
			}
		}

		bool disposable = disposableSymbol is not null && ImplementsInterface(info.Symbol, disposableSymbol);
		implToIndex[impl] = instances.Count;
		instances.Add(new InstanceModel(
			impl,
			info.Symbol.Name,
			info.Lifetime,
			new EquatableArray<string>(info.ServiceTypes.ToArray()),
			new EquatableArray<string>(parameters.ToArray()),
			disposable));
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

	private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol @interface)
	{
		foreach (INamedTypeSymbol implemented in type.AllInterfaces)
		{
			if (SymbolEqualityComparer.Default.Equals(implemented, @interface))
			{
				return true;
			}
		}

		return false;
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
		public ImplInfo(INamedTypeSymbol symbol, Lifetime lifetime, LocationInfo? location)
		{
			Symbol = symbol;
			Lifetime = lifetime;
			Location = location;
			ServiceTypes = new List<string>();
		}

		public INamedTypeSymbol Symbol { get; }
		public Lifetime Lifetime { get; }
		public LocationInfo? Location { get; }
		public List<string> ServiceTypes { get; }
	}
}
