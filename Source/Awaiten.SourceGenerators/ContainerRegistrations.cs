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
			// pre-built container member to expose. Factory wins if both are set (it is the more specific
			// directive), otherwise the instance is constructed.
			string? factory = NamedArgument(attribute, "Factory");
			string? instanceMember = NamedArgument(attribute, "Instance");
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
				productionMember));
		}

		return result;

		static string? NamedArgument(AttributeData attribute, string name)
		{
			foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
			{
				if (argument.Key == name && argument.Value.Value is string { Length: > 0, } value)
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
}
