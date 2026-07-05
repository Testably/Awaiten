using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     Redirects a single-service consumer parameter requesting a closed generic interface with no exact
	///     registration to a variance-compatible registration (Part A), e.g. a registered
	///     <c>IHandler&lt;DomainEvent&gt;</c> (<c>in T</c>) satisfying <c>IHandler&lt;OrderPlaced&gt;</c>. It runs
	///     only on a miss, so an exact registration always wins; keyed parameters and collections are out of scope.
	///     The redirect reuses the target's resolver by rewriting the parameter's service type and records a
	///     top-level dispatch alias (Part B) so imperative resolution routes to the same target. Only direct and
	///     <c>Func&lt;…, T&gt;</c> shapes redirect; the invariant <c>Lazy&lt;T&gt;</c> / <c>Task&lt;T&gt;</c>
	///     wrappers stay AWT101.
	/// </summary>
	private static ParameterModel RedirectVariance(
		ParameterModel parameterModel,
		IParameterSymbol parameter,
		Dictionary<ServiceKey, string> serviceToImpl,
		VarianceState variance)
	{
		if (variance.Candidates.Count == 0
		    || parameterModel.Key is not null
		    || parameterModel.Kind is not (DependencyKind.Direct or DependencyKind.Func)
		    || serviceToImpl.ContainsKey(KeyOf(parameterModel))
		    || UnderlyingServiceType(parameter.Type) is not { } requested
		    // The unwrapped symbol must denote the classified service type: a shape the classification treats as
		    // an opaque direct dependency (ValueTask<T>, a nested relationship like Func<Lazy<T>>) unwraps to a
		    // different type here, and redirecting it would emit an argument the declared parameter type cannot
		    // accept. Likewise Func<…, Owned<T>>, whose classified service is the inner T, not the Owned<T> handle.
		    || requested.ToDisplayString(FullyQualified) != parameterModel.ServiceType
		    || FindVarianceMatch(requested, parameterModel.ServiceType, variance) is not { } variantMatch)
		{
			return parameterModel;
		}

		// Record the requested closed type as a top-level dispatch alias (Part B) before the redirect rewrites
		// ServiceType. The same requested type always picks the same nearest target, so first-seen wins keeps the
		// alias stable across consumers.
		string requestedType = parameterModel.ServiceType;
		if (serviceToImpl.TryGetValue(new ServiceKey(variantMatch, null), out string? variantImpl)
		    && !variance.Aliases.ContainsKey(requestedType))
		{
			variance.Aliases.Add(requestedType, variantImpl);
			variance.AliasOrder.Add(requestedType);
		}

		return parameterModel with { ServiceType = variantMatch, };
	}

	/// <summary>
	///     Records an unkeyed collection parameter whose element is a closed generic interface (Part C), so its
	///     members can later be unioned with every variance-compatible registration (a collection of
	///     <c>IHandler&lt;OrderPlaced&gt;</c> includes a registered <c>IHandler&lt;DomainEvent&gt;</c>). All shapes
	///     (sync, async, awaited) share one membership per (element type, key). Keyed collections are left untouched,
	///     since every variance candidate is unkeyed.
	/// </summary>
	private static void RecordRequestedCollectionElement(
		ParameterModel parameterModel,
		IParameterSymbol parameter,
		VarianceState variance)
	{
		if (variance.Candidates.Count == 0
		    || parameterModel.Key is not null
		    || parameterModel.Kind is not (DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable)
		    || CollectionElementSymbol(parameter.Type) is not { } element
		    || !variance.CollectionSeen.Add(parameterModel.ServiceType))
		{
			return;
		}

		variance.CollectionElements.Add((parameterModel.ServiceType, element));
	}

	/// <summary>
	///     Unions every variance-compatible registration's members into each requested closed-generic collection's
	///     membership (Part C): the exact and open-expanded members keep their order and lead, and variance members
	///     follow in candidate registration order, deduped by implementation. A collection with only a variance
	///     match (no exact member) gains a fresh membership entry. Run after the instance loop and before the
	///     parameterized prune and edge building, so the unioned members drive analysis and emission.
	/// </summary>
	private static void UnionVarianceCollectionMembers(
		VarianceState variance,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		List<ServiceKey> serviceMemberOrder)
	{
		foreach ((string ServiceType, INamedTypeSymbol Symbol) requested in variance.CollectionElements)
		{
			List<(string ServiceType, INamedTypeSymbol Symbol)> matches =
				VarianceMatches(requested.Symbol, requested.ServiceType, variance);
			if (matches.Count == 0)
			{
				continue;
			}

			ServiceKey requestedKey = new(requested.ServiceType, null);
			if (!serviceMembers.TryGetValue(requestedKey, out List<string>? members))
			{
				members = new List<string>();
				serviceMembers.Add(requestedKey, members);
				serviceMemberOrder.Add(requestedKey);
			}

			UnionMatchedMembers(members, matches, serviceMembers);
		}
	}

	/// <summary>
	///     Appends each matched candidate's members to <paramref name="members" /> in candidate order, deduped by
	///     implementation (a candidate whose members were all pruned, or that failed to build, contributes none).
	/// </summary>
	private static void UnionMatchedMembers(
		List<string> members,
		List<(string ServiceType, INamedTypeSymbol Symbol)> matches,
		Dictionary<ServiceKey, List<string>> serviceMembers)
	{
		foreach ((string ServiceType, INamedTypeSymbol Symbol) match in matches)
		{
			if (!serviceMembers.TryGetValue(new ServiceKey(match.ServiceType, null), out List<string>? candidateMembers))
			{
				continue;
			}

			foreach (string member in candidateMembers.Where(member => !members.Contains(member)))
			{
				members.Add(member);
			}
		}
	}

	/// <summary>
	///     Exposes each closed generic type that was variance-redirected as a single service under its chosen
	///     target instance (Part B), so <c>Resolve&lt;T&gt;()</c> / <c>Resolve(T)</c> (and the Func/Lazy/Owned
	///     variants and registration metadata, all driven by an instance's <c>Services</c>) route it to the same
	///     resolver the consumer parameter uses. Skipped when an exact registration already owns the type (it never
	///     does when the alias was recorded, since a redirect fires only on a miss) or the target failed to build.
	/// </summary>
	private static void ApplyVarianceDispatchAliases(
		VarianceState variance,
		List<InstanceModel> instances,
		Dictionary<string, int> implToIndex,
		Dictionary<ServiceKey, string> serviceToImpl)
	{
		foreach (string requestedType in variance.AliasOrder)
		{
			if (serviceToImpl.ContainsKey(new ServiceKey(requestedType, null))
			    || !implToIndex.TryGetValue(variance.Aliases[requestedType], out int index))
			{
				continue;
			}

			ServiceKey aliasKey = new(requestedType, null);
			ServiceKey[] existing = instances[index].Services.AsArray();
			if (System.Array.IndexOf(existing, aliasKey) >= 0)
			{
				continue;
			}

			ServiceKey[] augmented = new ServiceKey[existing.Length + 1];
			System.Array.Copy(existing, augmented, existing.Length);
			augmented[existing.Length] = aliasKey;
			instances[index] = instances[index] with { Services = new EquatableArray<ServiceKey>(augmented), };
		}
	}

	/// <summary>
	///     The single service type a parameter resolves, as a symbol: the result of a <c>Func&lt;…, T&gt;</c>
	///     relationship, or the parameter type itself for a direct dependency, so the variance redirect can compare
	///     it against the registered service symbols. Returns <see langword="null" /> for a non-named type.
	/// </summary>
	private static INamedTypeSymbol? UnderlyingServiceType(ITypeSymbol type)
	{
		if (type is INamedTypeSymbol { IsGenericType: true, Name: "Func", TypeArguments.Length: >= 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System")
		{
			return named.TypeArguments[named.TypeArguments.Length - 1] as INamedTypeSymbol;
		}

		return type as INamedTypeSymbol;
	}

	/// <summary>
	///     The named element type symbol of a collection dependency (a synchronous shape, an
	///     <c>IAsyncEnumerable&lt;T&gt;</c>, or an awaited <c>Task&lt;C&gt;</c>), or <see langword="null" />
	///     otherwise. Variance-matching needs the symbol, where the parameter model carries only the element string.
	/// </summary>
	private static INamedTypeSymbol? CollectionElementSymbol(ITypeSymbol type)
	{
		if (type is IArrayTypeSymbol array)
		{
			return array.ElementType as INamedTypeSymbol;
		}

		// An awaited collection wraps a synchronous shape in Task<C>; recurse into the inner collection type.
		if (IsTask(type, out ITypeSymbol awaitedCollection))
		{
			return CollectionElementSymbol(awaitedCollection);
		}

		if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
		    && named.Name is "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection" or "IAsyncEnumerable")
		{
			return named.TypeArguments[0] as INamedTypeSymbol;
		}

		return null;
	}

	/// <summary>
	///     Every registered service that satisfies a requested closed generic interface through declared C# variance,
	///     in registration order (e.g. a registered <c>IHandler&lt;DomainEvent&gt;</c> with <c>in T</c> satisfies
	///     <c>IHandler&lt;OrderPlaced&gt;</c>). The exact request is skipped. A single-service consumer picks the
	///     nearest (<see cref="FindVarianceMatch" />); a collection unions them all. An invariant or non-interface
	///     request yields nothing, so it still reports AWT101.
	/// </summary>
	private static List<(string ServiceType, INamedTypeSymbol Symbol)> VarianceMatches(
		INamedTypeSymbol requested,
		string requestedServiceType,
		VarianceState variance)
	{
		List<(string ServiceType, INamedTypeSymbol Symbol)> matches = new();

		// Variance is defined for constructed generic interfaces only. A non-generic or non-interface request
		// cannot be variance-matched (and classes never carry variance).
		if (!requested.IsGenericType || requested.TypeKind != TypeKind.Interface)
		{
			return matches;
		}

		INamedTypeSymbol requestedDefinition = requested.OriginalDefinition;

		// The interface must declare at least one variant (in/out) type parameter; an invariant interface
		// (IStore<T> with no in/out) never matches a differently-closed registration.
		if (!HasDeclaredVariance(requested))
		{
			return matches;
		}

		foreach ((string ServiceType, INamedTypeSymbol Symbol) candidate in variance.Candidates)
		{
			// Skip the exact request (handled by the normal lookup) and any candidate of a different interface.
			if (candidate.ServiceType == requestedServiceType
			    || !SymbolEqualityComparer.Default.Equals(candidate.Symbol.OriginalDefinition, requestedDefinition))
			{
				continue;
			}

			// The candidate satisfies the request when an instance of the candidate's service IS-A the requested
			// service, exactly the implicit reference conversion C# variance defines.
			if (VarianceCompatible(candidate.Symbol, requested, variance.Compilation))
			{
				matches.Add(candidate);
			}
		}

		return matches;
	}

	/// <summary>
	///     Whether the generic interface's definition declares at least one variant (<c>in</c>/<c>out</c>) type
	///     parameter, the precondition for any differently-closed construction of it to be convertible.
	/// </summary>
	private static bool HasDeclaredVariance(INamedTypeSymbol service)
		=> service.OriginalDefinition.TypeParameters.Any(parameter => parameter.Variance is VarianceKind.In or VarianceKind.Out);

	/// <summary>
	///     The single registered service that best satisfies a requested closed generic interface through declared C#
	///     variance (Part A). When several match, the nearest wins (the one every other match's service is assignable
	///     to), falling back to registration order for determinism. Returns the matching candidate's service-type
	///     string, or <see langword="null" /> when there is no match.
	/// </summary>
	private static string? FindVarianceMatch(
		INamedTypeSymbol requested,
		string requestedServiceType,
		VarianceState variance)
	{
		(string ServiceType, INamedTypeSymbol Symbol)? best = null;
		foreach ((string ServiceType, INamedTypeSymbol Symbol) candidate in VarianceMatches(requested, requestedServiceType, variance))
		{
			// The candidate is nearer the request than the current best when the best's service converts to it:
			// under contravariance the more-derived closure sits between the request and the more-general one
			// (IHandler<object> IS-A IHandler<DomainEvent> IS-A IHandler<OrderPlaced>), and under covariance the
			// more-general closure does. In both cases the conversion target is the better pick.
			if (best is not { } current || VarianceCompatible(current.Symbol, candidate.Symbol, variance.Compilation))
			{
				best = candidate;
			}
		}

		return best?.ServiceType;
	}

	/// <summary>
	///     True when an instance of the constructed generic interface <paramref name="from" /> IS-A
	///     <paramref name="to" /> through declared C# variance: both must be the same generic interface
	///     definition, and at each type-argument position the declared variance must hold. Covariant (<c>out</c>)
	///     requires the <c>from</c> argument assignable to the <c>to</c> argument, contravariant (<c>in</c>)
	///     requires the reverse, and an invariant position requires identical arguments. Reference conversions
	///     only (a value-type argument at a variant position is never variance-convertible in C#).
	/// </summary>
	private static bool VarianceCompatible(INamedTypeSymbol from, INamedTypeSymbol to, Compilation compilation)
	{
		if (!SymbolEqualityComparer.Default.Equals(from.OriginalDefinition, to.OriginalDefinition))
		{
			return false;
		}

		ImmutableArray<ITypeParameterSymbol> parameters = to.OriginalDefinition.TypeParameters;
		if (from.TypeArguments.Length != parameters.Length || to.TypeArguments.Length != parameters.Length)
		{
			return false;
		}

		for (int i = 0; i < parameters.Length; i++)
		{
			ITypeSymbol fromArg = from.TypeArguments[i];
			ITypeSymbol toArg = to.TypeArguments[i];
			if (SymbolEqualityComparer.Default.Equals(fromArg, toArg))
			{
				continue;
			}

			switch (parameters[i].Variance)
			{
				case VarianceKind.Out when fromArg.IsReferenceType && toArg.IsReferenceType && IsReferenceAssignable(fromArg, toArg, compilation):
				case VarianceKind.In when fromArg.IsReferenceType && toArg.IsReferenceType && IsReferenceAssignable(toArg, fromArg, compilation):
					continue;
				default:
					return false;
			}
		}

		return true;
	}

	/// <summary>
	///     True when an identity or implicit reference conversion exists from <paramref name="from" /> to
	///     <paramref name="to" />, exactly what a variant type-argument position requires. Classified by the
	///     compiler rather than re-derived, so it covers every reference conversion, including an interface to
	///     <c>object</c>, array covariance, and the variance conversions a nested variant position needs
	///     (<c>IEnumerable&lt;OrderPlaced&gt;</c> to <c>IEnumerable&lt;DomainEvent&gt;</c>).
	/// </summary>
	private static bool IsReferenceAssignable(ITypeSymbol from, ITypeSymbol to, Compilation compilation)
	{
		Microsoft.CodeAnalysis.Operations.CommonConversion conversion = compilation.ClassifyCommonConversion(from, to);
		return conversion.IsIdentity || (conversion.IsImplicit && conversion.IsReference);
	}

	/// <summary>
	///     The mutable variance state threaded through instance building: the candidate registrations (every
	///     unkeyed closed-generic-interface registration, from coalescing), the compilation (whose conversion
	///     classification decides variance compatibility), and the accumulators the redirect fills (the top-level
	///     dispatch aliases (Part B) and the requested collection elements (Part C)), drained after the instance
	///     loop. Empty <see cref="Candidates" /> short-circuits every variance step.
	/// </summary>
	private sealed class VarianceState
	{
		public VarianceState(List<(string ServiceType, INamedTypeSymbol Symbol)> candidates, Compilation compilation)
		{
			Candidates = candidates;
			Compilation = compilation;
		}

		public List<(string ServiceType, INamedTypeSymbol Symbol)> Candidates { get; }
		public Compilation Compilation { get; }
		public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);
		public List<string> AliasOrder { get; } = new();
		public List<(string ServiceType, INamedTypeSymbol Symbol)> CollectionElements { get; } = new();
		public HashSet<string> CollectionSeen { get; } = new(StringComparer.Ordinal);
	}
}
