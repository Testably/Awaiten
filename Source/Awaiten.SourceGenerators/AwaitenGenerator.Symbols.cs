using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

	/// <summary>
	///     The <c>System.IAsyncDisposable</c> symbol when the consumer's compilation can emit an async drain,
	///     otherwise <see langword="null" />. Every Awaiten asset provides it (the netstandard2.0 asset through its
	///     Microsoft.Bcl.AsyncInterfaces dependency), so this is <see langword="null" /> only for a compilation that
	///     excludes those assets. <c>async ValueTask</c> also needs <c>AsyncValueTaskMethodBuilder</c>, which
	///     netstandard2.0 only gets from System.Threading.Tasks.Extensions.
	/// </summary>
	private static INamedTypeSymbol? AsyncDisposableSupport(Compilation compilation)
		=> compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder") is null
			? null
			: compilation.GetTypeByMetadataName("System.IAsyncDisposable");

	/// <summary>
	///     Reads the container's <c>LifetimeSafety</c> from its <c>[Container]</c> attribute, or from its base's when it
	///     extends one (see <see cref="ReadContainerOption" />). Strict (the default, enum value 0) unless the attribute
	///     explicitly sets <c>Loose</c>.
	/// </summary>
	internal static bool ReadStrict(INamedTypeSymbol containerSymbol)
		// LifetimeSafety is an enum; its TypedConstant value is the underlying int (Strict = 0, Loose = 1).
		=> ReadContainerOption(containerSymbol, "LifetimeSafety") is not { } value || value == 0;

	/// <summary>
	///     Reads the container's <c>AsyncSafety</c> from its <c>[Container]</c> attribute, or from its base's when it
	///     extends one (see <see cref="ReadContainerOption" />). Strict (the default, enum value 0, where an
	///     async-tainted service is reachable only through <c>ResolveAsync</c>) unless the attribute explicitly sets
	///     <c>SyncAfterInit</c>.
	/// </summary>
	internal static bool ReadSyncAfterInit(INamedTypeSymbol containerSymbol)
		// AsyncSafety is an enum; its TypedConstant value is the underlying int (Strict = 0, SyncAfterInit = 1).
		=> ReadContainerOption(containerSymbol, "AsyncSafety") == 1;

	/// <summary>
	///     An enum option explicitly set on the <c>[Container]</c> that decides it, or <see langword="null" /> for the
	///     default. A container that extends a base validates its graph exactly like the base, so the base decides; the
	///     extending container's own setting is ignored and reported (AWT225).
	/// </summary>
	private static int? ReadContainerOption(INamedTypeSymbol containerSymbol, string name)
	{
		INamedTypeSymbol owner = ReadExtends(containerSymbol) ?? containerSymbol;
		if (TryGetAwaitenAttribute(owner.GetAttributes(), "ContainerAttribute", out AttributeData? attribute))
		{
			foreach (KeyValuePair<string, TypedConstant> argument in attribute!.NamedArguments)
			{
				if (argument.Key == name && argument.Value.Value is int value)
				{
					return value;
				}
			}
		}

		return null;
	}

	private static string? NamedArgument(AttributeData attribute, string name)
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

	/// <summary>
	///     Reads a <c>System.Type</c> named argument (a <c>typeof(…)</c>) off an attribute, returning its
	///     <see cref="TypeIdentity" />, or <see langword="null" /> when unset. Used for <c>WhenInjectedInto</c>.
	/// </summary>
	private static string? NamedTypeArgument(AttributeData attribute, string name, Compilation compilation)
	{
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == name && argument.Value.Value is INamedTypeSymbol type)
			{
				return TypeIdentity(type, compilation);
			}
		}

		return null;
	}

	/// <summary>
	///     The private or protected type within <paramref name="type" /> (the type itself, a containing type or a type
	///     argument) that an <c>internal</c> member of the generated <c>Root</c> or <c>Scope</c> cannot expose, or
	///     <see langword="null" /> when there is none. Those members are as visible as the container, so a narrower
	///     type fits only when the container is itself nested privately within its reach; this mirrors the compiler's
	///     consistent-accessibility rule (CS0050, CS0051, CS0052).
	/// </summary>
	private static INamedTypeSymbol? LessAccessibleThanContainer(ITypeSymbol type, INamedTypeSymbol containerSymbol)
	{
		if (type is IArrayTypeSymbol array)
		{
			return LessAccessibleThanContainer(array.ElementType, containerSymbol);
		}

		for (INamedTypeSymbol? current = type as INamedTypeSymbol; current is not null; current = current.ContainingType)
		{
			if (current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal
			    && !ContainerIsAsRestricted(current, containerSymbol))
			{
				return current;
			}

			foreach (ITypeSymbol argument in current.TypeArguments)
			{
				if (LessAccessibleThanContainer(argument, containerSymbol) is { } narrower)
				{
					return narrower;
				}
			}
		}

		return null;
	}

	/// <summary>
	///     Whether the container is nested within the reach of the private or protected nested <paramref name="type" />
	///     behind a private (or, for a protected type, protected) level, so the generated members are no more visible
	///     than the type.
	/// </summary>
	private static bool ContainerIsAsRestricted(INamedTypeSymbol type, INamedTypeSymbol containerSymbol)
	{
		INamedTypeSymbol owner = type.ContainingType.OriginalDefinition;
		bool isPrivate = type.DeclaredAccessibility == Accessibility.Private;
		for (INamedTypeSymbol? level = containerSymbol; level is not null; level = level.ContainingType)
		{
			if (level.DeclaredAccessibility == Accessibility.Private)
			{
				for (INamedTypeSymbol? outer = level.ContainingType; outer is not null; outer = outer.ContainingType)
				{
					if (isPrivate ? SymbolEqualityComparer.Default.Equals(outer.OriginalDefinition, owner) : DerivesFrom(outer, owner))
					{
						return true;
					}
				}
			}
			else if (!isPrivate
			         && level.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedAndInternal
			         && level.ContainingType is { } parent
			         && DerivesFrom(parent, owner))
			{
				return true;
			}
		}

		return false;

		static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
		{
			for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
			{
				if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
				{
					return true;
				}
			}

			return false;
		}
	}

	private static void ImmutableArrayGuard(
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
