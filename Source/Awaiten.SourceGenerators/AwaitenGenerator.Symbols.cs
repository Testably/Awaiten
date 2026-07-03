using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

	/// <summary>
	///     The <c>System.IAsyncDisposable</c> symbol when - and only when - the referenced Awaiten runtime
	///     actually exposes its async-disposal surface, otherwise <see langword="null" />. The runtime gates
	///     that surface (<c>Owned&lt;T&gt;.DisposeAsync</c> and the awaiting scope drain) behind
	///     <c>#if NET || NETSTANDARD2_1_OR_GREATER</c>, so a consumer that binds the netstandard2.0 asset -
	///     net6.0 / net7.0, a netstandard2.1 library, or net48 even with Microsoft.Bcl.AsyncInterfaces - gets an
	///     <c>Owned&lt;T&gt;</c> with no <c>DisposeAsync</c> even though its own compilation can see
	///     <c>System.IAsyncDisposable</c>. Emitting the surface there would hand back a handle that cannot be
	///     <c>await using</c>d and would track async-only services the synchronous drain cannot release. Reading
	///     the capability off <c>Owned&lt;T&gt;</c> keeps the generated container consistent with the exact
	///     runtime asset it compiles against - the realized result of that same <c>#if</c>.
	/// </summary>
	private static INamedTypeSymbol? AsyncDisposableSupport(Compilation compilation)
	{
		if (compilation.GetTypeByMetadataName("System.IAsyncDisposable") is not { } asyncDisposable)
		{
			return null;
		}

		return compilation.GetTypeByMetadataName("Awaiten.Owned`1") is { } owned
		       && owned.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, asyncDisposable))
			? asyncDisposable
			: null;
	}

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
	///     Reads the container's <c>SyncResolveAfterInit</c> flag from its <c>[Container]</c> attribute
	///     (default <see langword="false" />: strict async resolution, where an async-tainted service is
	///     reachable only through <c>ResolveAsync</c>).
	/// </summary>
	internal static bool ReadSyncResolveAfterInit(INamedTypeSymbol containerSymbol)
	{
		if (TryGetAwaitenAttribute(containerSymbol.GetAttributes(), "ContainerAttribute", out AttributeData? attribute))
		{
			foreach (KeyValuePair<string, TypedConstant> argument in attribute!.NamedArguments)
			{
				if (argument.Key == "SyncResolveAfterInit" && argument.Value.Value is bool value)
				{
					return value;
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
