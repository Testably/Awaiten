using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     Recognizes a runtime-type dispatch dictionary (<c>IReadOnlyDictionary&lt;System.Type, TValue&gt;</c>),
	///     yielding its value type. Any value type is recognized so a malformed one is reported as AWT213 by
	///     <see cref="ResolveTypeDispatch" /> instead of as an unsupported keyed-dictionary key (AWT159).
	/// </summary>
	private static bool TryGetTypeDispatch(ITypeSymbol type, out ITypeSymbol? valueType)
	{
		if (TryGetKeyedCollectionElement(type, out _, out ITypeSymbol? keyType) && IsSystemType(keyType!))
		{
			valueType = ((INamedTypeSymbol)type).TypeArguments[1];
			return true;
		}

		valueType = null;
		return false;
	}

	/// <summary>
	///     The base interface <c>TBase</c> of a well-formed dispatch value <c>Func&lt;TBase&gt;</c>, or
	///     <see langword="null" /> when the value is anything else (including <c>Func&lt;Task&lt;TBase&gt;&gt;</c>).
	/// </summary>
	private static INamedTypeSymbol? DispatchBase(ITypeSymbol valueType)
		=> valueType is INamedTypeSymbol { Name: "Func", TypeArguments.Length: 1, } func
		   && func.ContainingNamespace?.ToDisplayString() == "System"
		   && func.TypeArguments[0] is INamedTypeSymbol { TypeKind: TypeKind.Interface, IsGenericType: false, } baseInterface
			? baseInterface
			: null;

	/// <summary>
	///     The closed generic service types a dispatch dictionary can key: every unkeyed, non-contextual registration
	///     declared closed (an explicit closed registration or a scan match, never a closing synthesized from an open
	///     generic registration, which exists only because something else named it) whose service still resolves after
	///     coalescing. Deduped by service type, in registration order.
	/// </summary>
	private static List<(string ServiceType, INamedTypeSymbol Symbol)> DispatchCandidates(
		List<RawRegistration> raw,
		Dictionary<ServiceKey, string> serviceToImpl)
	{
		List<(string, INamedTypeSymbol)> candidates = new();
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (RawRegistration registration in raw)
		{
			if (registration is { Key: null, WhenInjectedInto: null, IsSynthesized: false, ServiceSymbol: { IsGenericType: true, IsUnboundGenericType: false, } closed, }
			    && serviceToImpl.ContainsKey(new ServiceKey(registration.ServiceType, null))
			    && seen.Add(registration.ServiceType))
			{
				candidates.Add((registration.ServiceType, closed));
			}
		}

		return candidates;
	}

	/// <summary>
	///     Fills the entries of a synthesized dispatch dictionary: every candidate closing that implements the base
	///     interface, keyed by its single type argument. Reports <see cref="Diagnostics.UnsupportedTypeDispatch">AWT213</see>
	///     (and leaves the dictionary empty) when the value is not <c>Func&lt;TBase&gt;</c> over a non-generic
	///     interface, a <c>[FromKey]</c> is applied, a closing has more than one type argument, or the closings come from
	///     more than one generic definition. Called after <see cref="SuppressRegisteredCollectionSynthesis" />, so an
	///     explicitly registered dictionary (rewritten to Direct) is left alone.
	/// </summary>
	private static ParameterModel ResolveTypeDispatch(
		ParameterModel dependency,
		ITypeSymbol declaredType,
		ImplInfo info,
		List<(string ServiceType, INamedTypeSymbol Symbol)> candidates,
		List<DiagnosticInfo> diagnostics)
	{
		if (dependency.Kind != DependencyKind.TypeDispatch || !TryGetTypeDispatch(declaredType, out ITypeSymbol? valueType))
		{
			return dependency;
		}

		List<DispatchMember> members = new();
		string? reason = null;
		if (DispatchBase(valueType!) is not { } baseInterface)
		{
			reason = "needs a Func<TBase> value, where TBase is a non-generic interface that the dispatched generic interface extends";
		}
		else if (dependency.Key is not null)
		{
			reason = $"carries [FromKey({KeyArgumentDisplay(dependency.Key)})], but it holds every unkeyed closing; remove the [FromKey]";
		}
		else
		{
			reason = CollectDispatchMembers(baseInterface, candidates, members);
		}

		if (reason is not null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.UnsupportedTypeDispatch,
				dependency.Location ?? info.Location,
				new EquatableArray<string>([
					DisplayInstance(info.ImplementationType),
					Display(declaredType.ToDisplayString(FullyQualified)),
					reason,
				])));
			return dependency;
		}

		return dependency with { DispatchMembers = new EquatableArray<DispatchMember>(members.ToArray()), };
	}

	/// <summary>
	///     Adds every candidate implementing <paramref name="baseInterface" /> to <paramref name="members" />, or
	///     returns the AWT213 reason when one has more than one type argument or the candidates close more than one
	///     generic definition, so a type argument would not identify one entry.
	/// </summary>
	private static string? CollectDispatchMembers(
		INamedTypeSymbol baseInterface,
		List<(string ServiceType, INamedTypeSymbol Symbol)> candidates,
		List<DispatchMember> members)
	{
		INamedTypeSymbol? definition = null;
		foreach ((string serviceType, INamedTypeSymbol closed) in candidates)
		{
			if (!closed.AllInterfaces.Contains(baseInterface, SymbolEqualityComparer.Default))
			{
				continue;
			}

			if (closed.TypeArguments.Length != 1)
			{
				return $"cannot key '{Display(serviceType)}' by a single type argument; dispatch supports generics with exactly one type parameter";
			}

			if (definition is null)
			{
				definition = closed.OriginalDefinition;
			}
			else if (!SymbolEqualityComparer.Default.Equals(definition, closed.OriginalDefinition))
			{
				return $"is ambiguous: both '{definition.ToDisplayString()}' and '{closed.OriginalDefinition.ToDisplayString()}' implement '{baseInterface.ToDisplayString()}'; give each generic interface its own base interface";
			}

			members.Add(new DispatchMember(closed.TypeArguments[0].ToDisplayString(FullyQualified), serviceType));
		}

		return null;
	}

	/// <summary>
	///     The dependencies as the relationship analyses see them: a dispatch dictionary stands for one
	///     <c>Func&lt;T&gt;</c> per entry (exactly what its values are), every other dependency for itself. Lets the
	///     <c>Func</c> rules (AWT113, AWT118, AWT119/AWT120) apply to each entry unchanged.
	/// </summary>
	internal static IEnumerable<ParameterModel> AnalyzedDependencies(IEnumerable<ParameterModel> dependencies)
	{
		foreach (ParameterModel dependency in dependencies)
		{
			if (dependency.Kind != DependencyKind.TypeDispatch)
			{
				yield return dependency;
				continue;
			}

			foreach (DispatchMember member in dependency.DispatchMembers.AsArray())
			{
				yield return new ParameterModel(member.Service, DependencyKind.Func, Location: dependency.Location);
			}
		}
	}
}
