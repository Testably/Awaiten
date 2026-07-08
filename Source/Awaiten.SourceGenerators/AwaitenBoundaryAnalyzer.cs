using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Awaiten.SourceGenerators;

/// <summary>
///     Guards the boundary between application code and the container. Reports
///     <see cref="Diagnostics.ServiceLocatorInjection">AWT135</see> when a resolver seam
///     (<c>IAwaitenResolver</c> and everything that extends it: <c>IAwaitenAsyncResolver</c>,
///     <c>IAwaitenScope</c>, <c>IAwaitenRoot</c>, <c>IAwaitenContainerMetadata</c>) is held by a type that is not
///     a <c>[Container]</c> composition root, as a constructor parameter, property, or field - the Service
///     Locator anti-pattern. Also reports <see cref="Diagnostics.CompositionAttributeOutsideRoot">AWT134</see>
///     when a container-side composition attribute (a lifetime registration, <c>[Scan]</c>, <c>[Decorate]</c>,
///     <c>[Composite]</c>, <c>[Import]</c>, <c>[ImportService]</c>/<c>[ImportServices]</c>, or
///     <c>[InjectProperty]</c>) is applied to a class in an assembly that declares no <c>[Container]</c>.
/// </summary>
/// <remarks>
///     Both are analyzer (rather than generator) diagnostics so they can be suppressed in source with
///     <c>#pragma warning disable</c> or <c>[SuppressMessage]</c> (a legitimate seam such as the MS.DI bridge
///     adapter does exactly that), or turned into errors per project through editorconfig. AWT134 is a
///     best-effort boundary lint: it fires only in an assembly that declares no <c>[Container]</c>, so it stays
///     silent for a single-project app that mixes the root and its domain in one assembly (the cross-assembly
///     boundary is better enforced by an architecture test). AWT135 fires in every assembly, because a service
///     that reaches for the resolver is the same anti-pattern whether it lives beside the root or in a library.
///     The typed fast-path <c>IAwaitenResolver&lt;T&gt;</c> does not extend <c>IAwaitenResolver</c> and is a
///     single-service seam (closer to <c>Func&lt;T&gt;</c>), so it is intentionally not reported.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AwaitenBoundaryAnalyzer : DiagnosticAnalyzer
{
	private const string ContainerAttributeName = "Awaiten.ContainerAttribute";
	private const string ModuleAttributeName = "Awaiten.ModuleAttribute";
	private const string ResolverInterfaceName = "Awaiten.IAwaitenResolver";

	// The container-side composition attributes, matched by simple name within the Awaiten namespace so the
	// generic-arity forms (SingletonAttribute<T>, SingletonAttribute<T, S>, …) all match one entry, exactly as
	// the generator recognizes them.
	private static readonly ImmutableHashSet<string> CompositionAttributeNames = ImmutableHashSet.Create(
		StringComparer.Ordinal,
		"SingletonAttribute",
		"ScopedAttribute",
		"TransientAttribute",
		"ScanAttribute",
		"DecorateAttribute",
		"CompositeAttribute",
		"ImportAttribute",
		"ImportServiceAttribute",
		"ImportServicesAttribute",
		"InjectPropertyAttribute");

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		ImmutableArray.Create(Diagnostics.CompositionAttributeOutsideRoot, Diagnostics.ServiceLocatorInjection);

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.RegisterCompilationStartAction(static start =>
		{
			INamedTypeSymbol? containerAttribute = start.Compilation.GetTypeByMetadataName(ContainerAttributeName);
			INamedTypeSymbol? resolverInterface = start.Compilation.GetTypeByMetadataName(ResolverInterfaceName);
			if (containerAttribute is null || resolverInterface is null)
			{
				// The Awaiten assembly is not referenced; there is nothing to guard.
				return;
			}

			INamedTypeSymbol? moduleAttribute = start.Compilation.GetTypeByMetadataName(ModuleAttributeName);

			// AWT134 fires only in an assembly that declares no [Container] (a domain/application assembly). One
			// early-exit walk of the compilation's own types decides that once, so the whole boundary check is
			// free in a root/app assembly.
			bool assemblyDeclaresContainer = DeclaresContainer(start.Compilation.Assembly.GlobalNamespace, containerAttribute);

			start.RegisterSymbolAction(
				ctx => Analyze(
					(INamedTypeSymbol)ctx.Symbol, containerAttribute, moduleAttribute, resolverInterface, assemblyDeclaresContainer, ctx.ReportDiagnostic),
				SymbolKind.NamedType);
		});
	}

	private static void Analyze(
		INamedTypeSymbol type,
		INamedTypeSymbol containerAttribute,
		INamedTypeSymbol? moduleAttribute,
		INamedTypeSymbol resolverInterface,
		bool assemblyDeclaresContainer,
		Action<Diagnostic> report)
	{
		if (type.TypeKind != TypeKind.Class || type.IsImplicitlyDeclared)
		{
			return;
		}

		// A [Container] is the composition root, and a [Module] is composition code imported into one; both are
		// allowed to know about Awaiten. Everything else is domain/application code.
		bool isCompositionRoot = HasAttribute(type, containerAttribute)
		                         || (moduleAttribute is not null && HasAttribute(type, moduleAttribute));
		if (isCompositionRoot)
		{
			return;
		}

		ReportServiceLocatorSeams(type, resolverInterface, report);

		if (!assemblyDeclaresContainer)
		{
			ReportMisplacedCompositionAttributes(type, report);
		}
	}

	// AWT135: a resolver seam held as a constructor parameter, property, or field of a non-root type. A backing
	// field of an auto-property is implicitly declared and skipped, so an injected resolver property is reported
	// once (on the property).
	private static void ReportServiceLocatorSeams(INamedTypeSymbol type, INamedTypeSymbol resolverInterface, Action<Diagnostic> report)
	{
		foreach (IMethodSymbol constructor in type.InstanceConstructors)
		{
			if (constructor.IsImplicitlyDeclared)
			{
				continue;
			}

			foreach (IParameterSymbol parameter in constructor.Parameters)
			{
				if (IsResolverSeam(parameter.Type, resolverInterface))
				{
					ReportSeam(report, parameter, type, parameter.Type);
				}
			}
		}

		foreach (ISymbol member in type.GetMembers())
		{
			if (member.IsImplicitlyDeclared)
			{
				continue;
			}

			ITypeSymbol? memberType = member switch
			{
				IFieldSymbol field => field.Type,
				IPropertySymbol property => property.Type,
				_ => null,
			};

			if (memberType is not null && IsResolverSeam(memberType, resolverInterface))
			{
				ReportSeam(report, member, type, memberType);
			}
		}
	}

	// AWT134: container-side composition attributes applied to a domain class. The consumer-side escape hatches
	// [Arg]/[FromKey]/[Inject] are deliberately absent from CompositionAttributeNames, so they are never reported.
	private static void ReportMisplacedCompositionAttributes(INamedTypeSymbol type, Action<Diagnostic> report)
	{
		foreach (AttributeData attribute in type.GetAttributes())
		{
			if (attribute.AttributeClass is not { } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != "Awaiten"
			    || !CompositionAttributeNames.Contains(attributeClass.Name))
			{
				continue;
			}

			Location location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
			                    ?? type.Locations.FirstOrDefault()
			                    ?? Location.None;
			report(Diagnostic.Create(
				Diagnostics.CompositionAttributeOutsideRoot,
				location,
				type.Name,
				TrimAttributeSuffix(attributeClass.Name)));
		}
	}

	private static void ReportSeam(Action<Diagnostic> report, ISymbol site, INamedTypeSymbol type, ITypeSymbol resolverType)
	{
		Location location = site.Locations.FirstOrDefault() ?? type.Locations.FirstOrDefault() ?? Location.None;
		report(Diagnostic.Create(
			Diagnostics.ServiceLocatorInjection,
			location,
			type.Name,
			resolverType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
	}

	// A resolver seam is IAwaitenResolver itself, or anything that extends it (IAwaitenAsyncResolver,
	// IAwaitenScope, IAwaitenRoot, IAwaitenContainerMetadata, and a concrete container Root/Scope). The generic
	// IAwaitenResolver<T> does not implement the non-generic IAwaitenResolver, so it is not matched here.
	private static bool IsResolverSeam(ITypeSymbol type, INamedTypeSymbol resolverInterface)
		=> SymbolEqualityComparer.Default.Equals(type, resolverInterface)
		   || type.AllInterfaces.Contains(resolverInterface, SymbolEqualityComparer.Default);

	private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeType)
		=> symbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType));

	private static bool DeclaresContainer(INamespaceSymbol @namespace, INamedTypeSymbol containerAttribute)
	{
		foreach (INamedTypeSymbol type in @namespace.GetTypeMembers())
		{
			if (TypeOrNestedHasContainer(type, containerAttribute))
			{
				return true;
			}
		}

		foreach (INamespaceSymbol child in @namespace.GetNamespaceMembers())
		{
			if (DeclaresContainer(child, containerAttribute))
			{
				return true;
			}
		}

		return false;
	}

	private static bool TypeOrNestedHasContainer(INamedTypeSymbol type, INamedTypeSymbol containerAttribute)
	{
		if (HasAttribute(type, containerAttribute))
		{
			return true;
		}

		foreach (INamedTypeSymbol nested in type.GetTypeMembers())
		{
			if (TypeOrNestedHasContainer(nested, containerAttribute))
			{
				return true;
			}
		}

		return false;
	}

	private static string TrimAttributeSuffix(string name)
		=> name.EndsWith("Attribute", StringComparison.Ordinal)
			? name.Substring(0, name.Length - "Attribute".Length)
			: name;
}
