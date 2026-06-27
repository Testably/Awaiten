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
///     the container implementation. Invalid wiring (missing dependencies, cycles) is reported as a
///     build error.
/// </summary>
/// <remarks>
///     Phase 1 assumptions: a container is a non-generic <c>partial class</c> (it may be nested, in
///     which case every enclosing type must be declared <c>partial</c>); enclosing types are
///     non-generic; each constructed type has a single public constructor (when several exist, the
///     one with the most resolvable parameters is chosen).
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

		List<RawRegistration> raw = CollectRegistrations(containerSymbol);

		// service type -> (implementation, lifetime), first registration wins.
		Dictionary<string, RawRegistration> byService = new(StringComparer.Ordinal);
		foreach (RawRegistration registration in raw.Where(registration => !byService.ContainsKey(registration.ServiceType)))
		{
			byService.Add(registration.ServiceType, registration);
		}

		List<DiagnosticInfo> diagnostics = new();
		List<RegistrationModel> registrations = new();
		Dictionary<string, List<string>> dependencies = new(StringComparer.Ordinal);

		// Honor the first registration per service type only.
		foreach (RawRegistration registration in raw.Where(registration =>
			         ReferenceEquals(byService[registration.ServiceType], registration)))
		{
			cancellationToken.ThrowIfCancellationRequested();
			BuildRegistration(registration, containerSymbol, byService, registrations, dependencies, diagnostics);
		}

		DetectCycles(dependencies, containerSymbol, diagnostics);

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
			new EquatableArray<RegistrationModel>(registrations.ToArray()),
			new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));

		static string KeywordOf(INamedTypeSymbol symbol)
		{
			if (symbol.IsRecord)
			{
				return symbol.TypeKind == TypeKind.Struct ? "record struct" : "record";
			}

			return symbol.TypeKind == TypeKind.Struct ? "struct" : "class";
		}
	}

	private static void BuildRegistration(
		RawRegistration registration,
		INamedTypeSymbol containerSymbol,
		Dictionary<string, RawRegistration> byService,
		List<RegistrationModel> registrations,
		Dictionary<string, List<string>> dependencies,
		List<DiagnosticInfo> diagnostics)
	{
		// An abstract type or interface cannot be constructed; reject it instead of emitting a 'new'
		// against it (which would fail to compile in the generated source).
		if (registration.Implementation.IsAbstract || registration.Implementation.TypeKind == TypeKind.Interface)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NotInstantiable,
				registration.Location,
				new EquatableArray<string>([Display(registration.ImplementationType),])));
			return;
		}

		IMethodSymbol? constructor = SelectConstructor(registration.Implementation, containerSymbol, byService.Keys);
		if (constructor is null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NoAccessibleConstructor,
				registration.Location,
				new EquatableArray<string>([Display(registration.ImplementationType),])));
			return;
		}

		List<string> parameters = new();
		List<string> resolvedDependencies = new();
		foreach (IParameterSymbol parameter in constructor.Parameters)
		{
			string parameterType = parameter.Type.ToDisplayString(FullyQualified);
			parameters.Add(parameterType);
			if (byService.ContainsKey(parameterType))
			{
				resolvedDependencies.Add(parameterType);
			}
			else
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.MissingDependency,
					registration.Location,
					new EquatableArray<string>([
						Display(registration.ServiceType),
						Display(registration.ImplementationType),
						Display(parameterType),
					])));
			}
		}

		dependencies[registration.ServiceType] = resolvedDependencies;
		registrations.Add(new RegistrationModel(
			registration.ServiceType,
			registration.ImplementationType,
			registration.Implementation.Name,
			registration.Lifetime,
			new EquatableArray<string>(parameters.ToArray())));
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

	private static void DetectCycles(
		Dictionary<string, List<string>> dependencies,
		INamedTypeSymbol containerSymbol,
		List<DiagnosticInfo> diagnostics)
	{
		LocationInfo? containerLocation = LocationInfo.From(containerSymbol.Locations.FirstOrDefault());
		HashSet<string> visited = new(StringComparer.Ordinal);
		HashSet<string> onStack = new(StringComparer.Ordinal);
		List<string> path = new();
		HashSet<string> reportedCycles = new(StringComparer.Ordinal);

		foreach (string node in dependencies.Keys)
		{
			Visit(node);
		}

		void Visit(string node)
		{
			visited.Add(node);
			onStack.Add(node);
			path.Add(node);

			if (dependencies.TryGetValue(node, out List<string>? edges))
			{
				foreach (string next in edges)
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
			}

			onStack.Remove(node);
			path.RemoveAt(path.Count - 1);
		}

		void ReportCycle(string cycleStart)
		{
			int startIndex = path.LastIndexOf(cycleStart);
			List<string> cycle = path.GetRange(startIndex, path.Count - startIndex);
			cycle.Add(cycleStart);

			// Dedupe on the set of nodes so the same cycle is not reported once per back-edge.
			string signature = string.Join("|", cycle.Take(cycle.Count - 1).OrderBy(x => x, StringComparer.Ordinal));
			if (!reportedCycles.Add(signature))
			{
				return;
			}

			string rendered = string.Join(" -> ", cycle.Select(Display));
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.DependencyCycle,
				containerLocation,
				new EquatableArray<string>([rendered,])));
		}
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
}
