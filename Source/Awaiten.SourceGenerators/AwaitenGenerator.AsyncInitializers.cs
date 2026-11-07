using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     One usable <c>[AsyncInitializer&lt;T&gt;(method)]</c> of a container or module: the convention
	///     <see cref="Type" /> and the instance <see cref="Method" /> of it that initializes an implementation.
	/// </summary>
	private sealed record AsyncInitializerConvention(INamedTypeSymbol Type, IMethodSymbol Method)
	{
		/// <summary>The convention as a diagnostic names it, <c>Type.Method</c>.</summary>
		public string Display => $"{AwaitenGenerator.Display(Type.ToDisplayString(FullyQualified))}.{Method.Name}";
	}

	/// <summary>
	///     The conventions of each declaring scope met in one graph build (see <see cref="ScopeAsyncInitializers" />),
	///     resolved once per scope.
	/// </summary>
	private sealed class AsyncInitializerScopes
	{
		private readonly Compilation _compilation;
		private readonly Dictionary<INamedTypeSymbol, List<AsyncInitializerConvention>> _scopes = new(SymbolEqualityComparer.Default);

		public AsyncInitializerScopes(Compilation compilation) => _compilation = compilation;

		/// <summary>The conventions that judge the registrations <paramref name="scope" /> declares.</summary>
		public List<AsyncInitializerConvention> Of(INamedTypeSymbol scope)
		{
			if (!_scopes.TryGetValue(scope, out List<AsyncInitializerConvention>? conventions))
			{
				_scopes[scope] = conventions = ScopeAsyncInitializers(scope, _compilation);
			}

			return conventions;
		}
	}

	/// <summary>
	///     The conventions that judge the registrations a container or module declares: a module's own, and a container's
	///     own after those of the base it extends. Unusable conventions are left out; their owner's build reports them
	///     (AWT233).
	/// </summary>
	private static List<AsyncInitializerConvention> ScopeAsyncInitializers(INamedTypeSymbol scope, Compilation compilation)
	{
		List<AsyncInitializerConvention> conventions = HasAwaitenAttribute(scope.GetAttributes(), "ContainerAttribute") && ReadExtends(scope) is { } baseContainer
			? DeclaredAsyncInitializers(baseContainer, compilation, null)
			: new List<AsyncInitializerConvention>();
		conventions.AddRange(DeclaredAsyncInitializers(scope, compilation, null));
		return conventions;
	}

	/// <summary>
	///     The usable <c>[AsyncInitializer&lt;T&gt;(method)]</c> conventions declared directly on <paramref name="owner" />,
	///     in declaration order, reporting each unusable one as <see cref="Diagnostics.InvalidAsyncInitializer">AWT233</see>
	///     when <paramref name="diagnostics" /> is given.
	/// </summary>
	private static List<AsyncInitializerConvention> DeclaredAsyncInitializers(INamedTypeSymbol owner, Compilation compilation, List<DiagnosticInfo>? diagnostics)
	{
		List<AsyncInitializerConvention> conventions = new();
		foreach (AttributeData attribute in owner.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "AsyncInitializerAttribute", IsGenericType: true, TypeArguments.Length: 1, } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace
			    || attributeClass.TypeArguments[0] is not INamedTypeSymbol { TypeKind: not TypeKind.Error, } type
			    || attribute.ConstructorArguments.Length != 1
			    || attribute.ConstructorArguments[0].Value is not string name)
			{
				continue;
			}

			if (AsyncInitializerFault(type, name, owner, compilation, out IMethodSymbol? method) is { } reason)
			{
				diagnostics?.Add(new DiagnosticInfo(
					Diagnostics.InvalidAsyncInitializer,
					LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? owner.Locations.FirstOrDefault()),
					new EquatableArray<string>([Display(type.ToDisplayString(FullyQualified)), name, reason,])));
				continue;
			}

			conventions.Add(new AsyncInitializerConvention(type, method!));
		}

		return conventions;
	}

	/// <summary>
	///     Why the method <paramref name="name" /> of <paramref name="type" /> cannot initialize an implementation, or
	///     <see langword="null" /> with the <paramref name="method" /> when it can: the only method of that name on the type
	///     or its base types and interfaces (an override counts as the method it overrides), an instance method that is not
	///     generic, accessible from <paramref name="owner" />, returning <c>Task</c> or <c>ValueTask</c>, whose parameters
	///     are passed by value and carry none of the attributes a hook would use to put Awaiten into the domain type.
	/// </summary>
	private static string? AsyncInitializerFault(INamedTypeSymbol type, string name, INamedTypeSymbol owner, Compilation compilation, out IMethodSymbol? method)
	{
		method = null;
		List<IMethodSymbol> candidates = new();
		foreach (INamedTypeSymbol declaring in ConventionTypeAndBases(type))
		{
			foreach (IMethodSymbol candidate in declaring.GetMembers(name).OfType<IMethodSymbol>())
			{
				if (candidate.MethodKind == MethodKind.Ordinary && !candidates.Any(known => Overrides(known, candidate)))
				{
					candidates.Add(candidate);
				}
			}
		}

		if (candidates.Count == 0)
		{
			return $"'{Display(type.ToDisplayString(FullyQualified))}' has no method '{name}'";
		}

		if (candidates.Count > 1)
		{
			return "the method is overloaded, so there is no way to choose which one to call; give the overloads different names";
		}

		IMethodSymbol found = candidates[0];
		if (found.IsStatic)
		{
			return "the method is static, but an async initializer is called on the instance";
		}

		if (found.IsGenericMethod)
		{
			return "the method is generic, so there is no way to choose its type arguments";
		}

		if (!compilation.IsSymbolAccessibleWithin(found, owner))
		{
			return $"the method is not accessible from '{Display(owner.ToDisplayString(FullyQualified))}'";
		}

		if (!IsAsyncHookReturn(found.ReturnType, compilation))
		{
			return $"it returns '{Display(found.ReturnType.ToDisplayString(FullyQualified))}'; return Task or ValueTask instead";
		}

		foreach (IParameterSymbol parameter in found.Parameters)
		{
			if (parameter.RefKind != RefKind.None)
			{
				return $"its parameter '{parameter.Name}' is passed by reference";
			}

			if (MirroredAwayAttribute(parameter.GetAttributes()) is { } attribute)
			{
				return $"its parameter '{parameter.Name}' is marked {attribute}, which puts Awaiten into the domain type; use an OnActivated hook instead";
			}
		}

		method = found;
		return null;

		static bool Overrides(IMethodSymbol derived, IMethodSymbol candidate)
		{
			for (IMethodSymbol? overridden = derived.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
			{
				if (SymbolEqualityComparer.Default.Equals(overridden.OriginalDefinition, candidate.OriginalDefinition))
				{
					return true;
				}
			}

			return false;
		}
	}

	/// <summary>
	///     The types a convention's method can be declared on: an interface and the interfaces it extends, or a class and
	///     its base classes.
	/// </summary>
	private static IEnumerable<INamedTypeSymbol> ConventionTypeAndBases(INamedTypeSymbol type)
	{
		if (type.TypeKind == TypeKind.Interface)
		{
			return new[] { type, }.Concat(type.AllInterfaces);
		}

		List<INamedTypeSymbol> types = new();
		for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
		{
			types.Add(current);
		}

		return types;
	}

	/// <summary>
	///     Whether a value of <paramref name="type" /> is a <paramref name="conventionType" />: the type itself, an
	///     implementation of the interface, or a class derived from the class.
	/// </summary>
	private static bool IsOfConventionType(ITypeSymbol type, INamedTypeSymbol conventionType)
	{
		if (SymbolEqualityComparer.Default.Equals(type, conventionType))
		{
			return true;
		}

		if (conventionType.TypeKind == TypeKind.Interface)
		{
			return type.AllInterfaces.Contains(conventionType, SymbolEqualityComparer.Default);
		}

		for (ITypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
		{
			if (SymbolEqualityComparer.Default.Equals(current, conventionType))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	///     The conventions that run on an implementation, in declaration order: those whose type
	///     <paramref name="matches" /> says it is, each method of <paramref name="implementation" /> once even when several
	///     conventions reach it, at the position of the first. Without the implementation (one hidden behind a generated
	///     factory) each convention method runs once.
	/// </summary>
	private static List<AsyncInitializerConvention> MatchingAsyncInitializers(IEnumerable<AsyncInitializerConvention> conventions, ITypeSymbol? implementation, Func<INamedTypeSymbol, bool> matches)
	{
		List<AsyncInitializerConvention> matched = new();
		HashSet<IMethodSymbol> methods = new(SymbolEqualityComparer.Default);
		foreach (AsyncInitializerConvention convention in conventions)
		{
			if (matches(convention.Type) && methods.Add(ImplementingMethod(convention.Method, implementation).OriginalDefinition))
			{
				matched.Add(convention);
			}
		}

		return matched;
	}

	/// <summary>
	///     The method a call of the convention <paramref name="method" /> runs on <paramref name="implementation" />: the
	///     implementation of an interface method, or the most derived override of a class method. An explicit interface
	///     implementation is a method of its own. Falls back to <paramref name="method" /> itself.
	/// </summary>
	private static IMethodSymbol ImplementingMethod(IMethodSymbol method, ITypeSymbol? implementation)
	{
		if (implementation is null)
		{
			return method;
		}

		if (method.ContainingType.TypeKind == TypeKind.Interface)
		{
			return implementation.FindImplementationForInterfaceMember(method) as IMethodSymbol ?? method;
		}

		for (ITypeSymbol? current = implementation; current is not null; current = current.BaseType)
		{
			foreach (IMethodSymbol candidate in current.GetMembers(method.Name).OfType<IMethodSymbol>())
			{
				for (IMethodSymbol? overridden = candidate; overridden is not null; overridden = overridden.OverriddenMethod)
				{
					if (SymbolEqualityComparer.Default.Equals(overridden.OriginalDefinition, method.OriginalDefinition))
					{
						return candidate;
					}
				}
			}
		}

		return method;
	}

	/// <summary>
	///     The async initializers of an instance: the conventions of the scope that declares its winning registration,
	///     decorator or composite (the module or base container it comes from, else the container) that match its owned
	///     type, or, for a self-compiled
	///     implementation behind a generated factory, the conventions its owner recorded. Their parameters are classified
	///     like those of an asynchronous <c>OnActivated</c> hook, so they take part in every graph check. A convention the
	///     container cannot call is reported as <see cref="Diagnostics.InaccessibleAsyncInitializer">AWT234</see> and left out.
	/// </summary>
	private static EquatableArray<AsyncInitializerModel> BuildAsyncInitializers(ImplInfo info, ITypeSymbol ownedType, BuildContext context)
	{
		List<AsyncInitializerConvention> conventions = context.AsyncInitializers.Of(info.ConventionScope(context.ContainerSymbol));
		if (conventions.Count == 0)
		{
			return default;
		}

		ImplementationTraits? traits = info.Traits;
		List<AsyncInitializerModel> models = new();
		foreach (AsyncInitializerConvention convention in MatchingAsyncInitializers(conventions, traits is null ? ownedType : null, type => traits is { } known
			         ? known.AsyncInitializers.Contains(type.ToDisplayString(FullyQualified))
			         : IsOfConventionType(ownedType, type)))
		{
			if (InaccessibleAsyncInitializerPart(convention, context.ContainerSymbol, context.Compilation) is { } reason)
			{
				context.Diagnostics.Add(new DiagnosticInfo(
					Diagnostics.InaccessibleAsyncInitializer,
					info.Location,
					new EquatableArray<string>([
						$"'{DisplayInstance(info.ImplementationType)}'",
						Display(convention.Type.ToDisplayString(FullyQualified)),
						convention.Method.Name,
						reason,
						"make it public, or grant this assembly InternalsVisibleTo in its assembly",
					])));
				continue;
			}

			models.Add(new AsyncInitializerModel(
				convention.Type.ToDisplayString(FullyQualified),
				convention.Method.Name,
				ClassifyHookParameterSymbols(convention.Method.Parameters, asynchronous: true, release: false, info, context)));
		}

		return new EquatableArray<AsyncInitializerModel>(models.ToArray());
	}

	/// <summary>
	///     Why the generated <paramref name="container" /> cannot call <paramref name="convention" />, or
	///     <see langword="null" />: its type or its method is not accessible from it, as with an internal convention of a
	///     module in another assembly that does not grant this one <c>InternalsVisibleTo</c>. A parameter type is at least
	///     as accessible as the method, so it needs no check of its own.
	/// </summary>
	private static string? InaccessibleAsyncInitializerPart(AsyncInitializerConvention convention, INamedTypeSymbol container, Compilation compilation)
	{
		string where = $"is not accessible from the container '{Display(container.ToDisplayString(FullyQualified))}'";
		if (!compilation.IsSymbolAccessibleWithin(convention.Type, container))
		{
			return $"'{Display(convention.Type.ToDisplayString(FullyQualified))}' {where}";
		}

		return compilation.IsSymbolAccessibleWithin(convention.Method, container) ? null : $"the method {where}";
	}

	/// <summary>
	///     <see cref="Diagnostics.InaccessibleAsyncInitializer">AWT234</see> for a self-compiled implementation of the
	///     module that one of the module's conventions initializes, when a consumer of the module could not call that
	///     convention: its type or its method is not public, or, for an internal module, which only its own assembly and
	///     the assemblies it grants <c>InternalsVisibleTo</c> import, not accessible in its assembly. An assembly granted
	///     <c>InternalsVisibleTo</c> can, so like AWT195 the failure may be left to such an assembly (see
	///     <see cref="SkipReason" />).
	/// </summary>
	private static DiagnosticInfo? ModuleAsyncInitializerFault(INamedTypeSymbol implementation, ModuleScanContext context, SelfCompiledSubject subject, Location? location)
	{
		List<AsyncInitializerConvention> conventions = DeclaredAsyncInitializers(context.Module, context.Compilation, null);
		foreach (AsyncInitializerConvention convention in MatchingAsyncInitializers(conventions, implementation, type => IsOfConventionType(implementation, type)))
		{
			string? reason = null;
			if (!FitsFactorySignature(convention.Type, context))
			{
				string where = IsHiddenInModule(convention.Type, context.Module, context.Compilation) ? "private to the module" : "not accessible outside the module's assembly";
				reason = $"'{Display(convention.Type.ToDisplayString(FullyQualified))}' is {where}";
			}
			else if (convention.Method.DeclaredAccessibility != Accessibility.Public
			         && (IsExternallyAccessible(context.Module) || !context.Compilation.IsSymbolAccessibleWithin(convention.Method, context.Module.ContainingAssembly)))
			{
				reason = "the method is not accessible outside the module's assembly";
			}

			if (reason is not null)
			{
				return new DiagnosticInfo(
					Diagnostics.InaccessibleAsyncInitializer,
					LocationInfo.From(location),
					new EquatableArray<string>([
						subject.Subject,
						Display(convention.Type.ToDisplayString(FullyQualified)),
						convention.Method.Name,
						$"{reason}, so a consumer in another assembly cannot call it",
						$"make it public or {subject.Remedy}",
					]));
			}
		}

		return null;
	}
}
