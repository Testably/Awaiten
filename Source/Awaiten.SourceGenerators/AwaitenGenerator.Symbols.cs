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
	///     Reads the container's <c>LifetimeSafety</c> from its <c>[Container]</c> attribute. Strict (the
	///     default, enum value 0) unless the attribute explicitly sets <c>Loose</c>.
	/// </summary>
	internal static bool ReadStrict(INamedTypeSymbol containerSymbol)
	{
		if (TryGetAwaitenAttribute(containerSymbol.GetAttributes(), "ContainerAttribute", out AttributeData? attribute))
		{
			foreach (KeyValuePair<string, TypedConstant> argument in attribute!.NamedArguments)
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
	///     Reads the container's <c>AsyncSafety</c> from its <c>[Container]</c> attribute. Strict (the default,
	///     enum value 0, where an async-tainted service is reachable only through <c>ResolveAsync</c>) unless the
	///     attribute explicitly sets <c>SyncAfterInit</c>.
	/// </summary>
	internal static bool ReadSyncAfterInit(INamedTypeSymbol containerSymbol)
	{
		if (TryGetAwaitenAttribute(containerSymbol.GetAttributes(), "ContainerAttribute", out AttributeData? attribute))
		{
			foreach (KeyValuePair<string, TypedConstant> argument in attribute!.NamedArguments)
			{
				// AsyncSafety is an enum; its TypedConstant value is the underlying int (Strict = 0, SyncAfterInit = 1).
				if (argument.Key == "AsyncSafety" && argument.Value.Value is int value)
				{
					return value == 1;
				}
			}
		}

		return false;
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
	///     fully-qualified display, or <see langword="null" /> when unset. Used for <c>WhenInjectedInto</c>.
	/// </summary>
	private static string? NamedTypeArgument(AttributeData attribute, string name)
	{
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == name && argument.Value.Value is INamedTypeSymbol type)
			{
				return type.ToDisplayString(FullyQualified);
			}
		}

		return null;
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
