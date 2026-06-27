using System.Collections.Immutable;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

/// <summary>
///     Reads the lifetime registrations declared by the Awaiten lifetime attributes on a container.
///     Shared by the <see cref="AwaitenGenerator" /> (which emits the container) and the analyzers
///     (which report suppressible lifetime diagnostics in-source), so both see the same registrations.
/// </summary>
internal static class ContainerRegistrations
{
	public const string AttributeNamespace = "Awaiten";

	private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

	public static List<RawRegistration> Collect(INamedTypeSymbol containerSymbol)
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

			// A 'Factory' names a container method that produces the instance; an 'Instance' names a
			// pre-built container member to expose. Setting both is contradictory (reported as AWT110);
			// when only one is set it selects the production, otherwise the instance is constructed. An
			// explicit empty string is kept (not treated as absent) so it surfaces as AWT108/AWT109 rather
			// than silently downgrading to a constructor.
			string? factory = NamedArgument(attribute, "Factory");
			string? instanceMember = NamedArgument(attribute, "Instance");
			bool conflictingDirectives = factory is not null && instanceMember is not null;
			(ProductionKind production, string? productionMember) = factory is not null
				? (ProductionKind.Factory, factory)
				: instanceMember is not null
					? (ProductionKind.Instance, instanceMember)
					: (ProductionKind.Constructor, (string?)null);

			Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
			result.Add(new RawRegistration(
				service.ToDisplayString(FullyQualified),
				implementation.ToDisplayString(FullyQualified),
				lifetime.Value,
				(INamedTypeSymbol)implementation,
				location,
				production,
				productionMember,
				conflictingDirectives));
		}

		return result;

		static string? NamedArgument(AttributeData attribute, string name)
		{
			foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
			{
				if (argument.Key == name && argument.Value.Value is string value)
				{
					return value;
				}
			}

			return null;
		}

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

	/// <summary>
	///     The members named <paramref name="name" /> that the generated container partial can reach: the
	///     container's own members (any accessibility, since a partial can use its own private members)
	///     plus members inherited from base types that a derived type can actually access (everything but
	///     private, with internal / private-protected restricted to the same assembly).
	/// </summary>
	public static IEnumerable<ISymbol> AccessibleMembers(INamedTypeSymbol container, string name)
	{
		foreach (ISymbol member in container.GetMembers(name))
		{
			yield return member;
		}

		for (INamedTypeSymbol? baseType = container.BaseType; baseType is not null; baseType = baseType.BaseType)
		{
			foreach (ISymbol member in baseType.GetMembers(name))
			{
				if (IsAccessibleFromDerived(member, container))
				{
					yield return member;
				}
			}
		}

		static bool IsAccessibleFromDerived(ISymbol member, INamedTypeSymbol container)
			=> member.DeclaredAccessibility switch
			{
				Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal => true,
				Accessibility.Internal or Accessibility.ProtectedAndInternal =>
					SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, container.ContainingAssembly),
				_ => false,
			};
	}

	/// <summary>
	///     The ordinary methods named <paramref name="name" /> on the container (or an accessible base
	///     type) whose return type is implicitly convertible to <paramref name="serviceType" /> - the
	///     candidate factory methods for a <c>Factory</c> registration. None means AWT108; more than one
	///     means an ambiguous factory (AWT112). Shared with the analyzer so AWT106 sees the same producer.
	/// </summary>
	public static List<IMethodSymbol> FindFactoryCandidates(
		INamedTypeSymbol container, string name, ITypeSymbol serviceType, Compilation compilation)
	{
		List<IMethodSymbol> candidates = new();
		foreach (ISymbol member in AccessibleMembers(container, name))
		{
			if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, } method
			    && compilation.HasImplicitConversion(method.ReturnType, serviceType))
			{
				candidates.Add(method);
			}
		}

		return candidates;
	}
}
