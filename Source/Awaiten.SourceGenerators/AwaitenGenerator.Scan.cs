using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     Expands each <c>[Scan]</c> on the container into overridable registrations for every concrete class
	///     assignable to the scanned marker - as the type itself, under the (marker-assignable) interfaces it
	///     implements, or both, per <c>ScanAs</c>. When the marker is an unbound generic (<c>typeof(IView&lt;&gt;)</c>),
	///     a match is a concrete type implementing a <em>closed</em> form of it, registered under that closed
	///     interface (Autofac's <c>AsClosedTypesOf</c>). The scan covers the container's own assembly by default, or
	///     the assemblies named by <c>InAssembliesOf</c>; matches register in a deterministic order (by
	///     fully-qualified name) so generated output is reproducible. Abstract/static classes, generic type
	///     definitions, types the container's assembly cannot access, and the marker itself are skipped. Reports
	///     AWT138 when a scan matches nothing, AWT139 when an interfaces-only scan matches a type with no
	///     assignable interface, AWT140 when an <c>InAssembliesOf</c> assembly has no candidate types, and AWT143
	///     when <c>InAssembliesOf</c> resolves to no assembly at all.
	/// </summary>
	/// <remarks>
	///     The synthesized registrations carry <see cref="RawRegistration.IsScan" />, so coalescing lets an
	///     explicit registration of the same implementation take precedence for single resolution (a scan never
	///     conflicts over lifetime or production), while every match still joins its service's collection.
	/// </remarks>
	private static List<RawRegistration> CollectScans(
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		List<RawRegistration> result = new();

		foreach (AttributeData attribute in containerSymbol.GetAttributes())
		{
			if (attribute.AttributeClass is { Name: "ScanAttribute", } attributeClass
			    && attributeClass.ContainingNamespace?.ToDisplayString() == AttributeNamespace
			    && ScanMarker(attribute, attributeClass) is { } marker)
			{
				ExpandScan(attribute, marker, compilation, result, diagnostics);
			}
		}

		return result;
	}

	// Expands one [Scan] over its candidate types, registering each match per ScanAs. An unbound generic marker
	// (typeof(IView<>)) matches implementers of any closed form of it, registered under that closed interface
	// (AsClosedTypesOf); a closed marker matches types assignable to it, registered under it. Every assignable
	// match is counted even when an explicit registration overrides it, so AWT138 fires only when nothing matched.
	private static void ExpandScan(
		AttributeData attribute,
		INamedTypeSymbol marker,
		Compilation compilation,
		List<RawRegistration> result,
		List<DiagnosticInfo> diagnostics)
	{
		Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

		// AWT143: an InAssembliesOf that resolves to no assembly at all (an empty array, or entries naming no
		// type) is reported and the scan registers nothing - not silently redirected to the container's own
		// assembly, which is what an unset InAssembliesOf means.
		List<IAssemblySymbol>? assemblies = ScanAssemblies(attribute);
		if (assemblies is { Count: 0, })
		{
			diagnostics.Add(new DiagnosticInfo(Diagnostics.ScanAssembliesEmpty, LocationInfo.From(location), new EquatableArray<string>([])));
			return;
		}

		ScanMatch match = new(ScanExposureOf(attribute), ScanLifetime(attribute), location, ScanSkipsUnconstructable(attribute));
		bool openMarker = IsOpenGenericMarker(marker);
		INamedTypeSymbol markerDefinition = marker.OriginalDefinition;
		string markerDisplay = (openMarker ? markerDefinition : marker).ToDisplayString(FullyQualified);

		int matched = 0;
		foreach (INamedTypeSymbol type in ScanCandidates(assemblies, compilation, marker, location, diagnostics))
		{
			matched++;
			List<INamedTypeSymbol> contracts = openMarker
				? ClosedMarkerInterfaces(type, markerDefinition)
				: MarkerInterfaces(type, marker, compilation);
			RegisterScanMatch(type, contracts, markerDisplay, match, result, diagnostics);
		}

		// AWT138 fires only for an own-assembly scan (its "in this assembly" wording is accurate there): an
		// InAssembliesOf scan that matched nothing already reported the more actionable AWT140 for each named
		// assembly, so AWT138 would be redundant and misworded.
		if (matched == 0 && assemblies is null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanMatchedNothing,
				LocationInfo.From(location),
				new EquatableArray<string>([Display(markerDisplay),])));
		}
	}

	// Registers one scan match per ScanAs: as its own concrete type and under each contract interface. An
	// interfaces-only scan that found no contract to register under reports AWT139 - the usual cause is a
	// base-type marker. SelfAndImplementedInterfaces is exempt, since its self registration still covers the type.
	private static void RegisterScanMatch(
		INamedTypeSymbol type,
		List<INamedTypeSymbol> contracts,
		string markerDisplay,
		ScanMatch match,
		List<RawRegistration> result,
		List<DiagnosticInfo> diagnostics)
	{
		string typeName = type.ToDisplayString(FullyQualified);

		if (match.RegisterSelf)
		{
			result.Add(ScanRegistration(typeName, typeName, type, type, match));
		}

		if (!match.RegisterInterfaces)
		{
			return;
		}

		foreach (INamedTypeSymbol contract in contracts)
		{
			result.Add(ScanRegistration(contract.ToDisplayString(FullyQualified), typeName, type, contract, match));
		}

		if (contracts.Count == 0 && match.Exposure == ScanExposure.ImplementedInterfaces)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanNoImplementedInterfaces,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([
					Display(typeName),
					Display(markerDisplay),
				])));
		}
	}

	// The interfaces a closed marker registers a match under: every implemented interface assignable to the marker
	// (so a marker interface registers the match under itself and any more-derived service interfaces), never
	// unrelated interfaces such as IDisposable.
	private static List<INamedTypeSymbol> MarkerInterfaces(INamedTypeSymbol type, INamedTypeSymbol marker, Compilation compilation)
		=> type.AllInterfaces.Where(contract => compilation.HasImplicitConversion(contract, marker)).ToList();

	// The interfaces an open marker registers a match under: the closed forms of the marker the type implements,
	// restricted to interfaces (a base-type marker yields none, which surfaces as AWT139).
	private static List<INamedTypeSymbol> ClosedMarkerInterfaces(INamedTypeSymbol type, INamedTypeSymbol markerDefinition)
		=> ClosedMarkerForms(type, markerDefinition).Where(contract => contract.TypeKind == TypeKind.Interface).ToList();

	// The per-scan settings shared by every match of one [Scan]: how matches are exposed, the lifetime applied,
	// the attribute location for diagnostics, and whether an unconstructable match is skipped with a warning
	// (SkipUnconstructable) instead of erroring. Bundled so the per-match registration takes one handle.
	private sealed record ScanMatch(ScanExposure Exposure, Lifetime Lifetime, Location? Location, bool SkipUnconstructable)
	{
		public bool RegisterSelf => Exposure is ScanExposure.Self or ScanExposure.SelfAndImplementedInterfaces;

		public bool RegisterInterfaces => Exposure is ScanExposure.ImplementedInterfaces or ScanExposure.SelfAndImplementedInterfaces;
	}

	// Whether the scanned marker is an unbound/open generic definition (typeof(IView<>)): either Roslyn flagged
	// it IsUnboundGenericType, or it has arity and is its own definition (its type arguments are the bare
	// type parameters).
	private static bool IsOpenGenericMarker(INamedTypeSymbol marker)
		=> marker.IsUnboundGenericType
		   || (marker.Arity > 0 && SymbolEqualityComparer.Default.Equals(marker, marker.OriginalDefinition));

	/// <summary>
	///     The distinct closed forms of <paramref name="markerDefinition" /> that <paramref name="type" />
	///     implements (its interfaces) or inherits (its base types) - for example <c>IView&lt;VM1&gt;</c> and
	///     <c>IView&lt;VM2&gt;</c> for a type that implements the marker at two type arguments. Ordered by
	///     fully-qualified name so multiple matches register deterministically.
	/// </summary>
	private static List<INamedTypeSymbol> ClosedMarkerForms(INamedTypeSymbol type, INamedTypeSymbol markerDefinition)
	{
		List<INamedTypeSymbol> closed = new();
		HashSet<string> seen = new(StringComparer.Ordinal);

		// The type's interfaces and its own inheritance chain: a match may close the marker as an interface it
		// implements or as a base type it inherits.
		foreach (INamedTypeSymbol candidate in type.AllInterfaces.Concat(SelfAndBaseTypes(type)))
		{
			if (candidate.IsGenericType
			    && SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, markerDefinition)
			    && seen.Add(candidate.ToDisplayString(FullyQualified)))
			{
				closed.Add(candidate);
			}
		}

		closed.Sort((left, right) => string.CompareOrdinal(
			left.ToDisplayString(FullyQualified),
			right.ToDisplayString(FullyQualified)));
		return closed;
	}

	// A type and every base type up its inheritance chain, so a marker closed as a base type is found alongside
	// one closed as an implemented interface.
	private static IEnumerable<INamedTypeSymbol> SelfAndBaseTypes(INamedTypeSymbol type)
	{
		for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
		{
			yield return current;
		}
	}

	/// <summary>
	///     The concrete-type candidates a <c>[Scan]</c> enumerates: the container's own assembly when
	///     <paramref name="assemblies" /> is <see langword="null" /> (<c>InAssembliesOf</c> unset), or the listed
	///     assemblies. Filtered to concrete classes assignable to the marker and sorted by fully-qualified name so
	///     the resulting registrations are reproducible across builds. Reports AWT140 for any
	///     <c>InAssembliesOf</c> assembly that holds no such type (a likely missing <c>ProjectReference</c>).
	/// </summary>
	private static List<INamedTypeSymbol> ScanCandidates(
		List<IAssemblySymbol>? assemblies,
		Compilation compilation,
		INamedTypeSymbol marker,
		Location? location,
		List<DiagnosticInfo> diagnostics)
	{
		List<INamedTypeSymbol> candidates = new();

		if (assemblies is null)
		{
			candidates.AddRange(EnumerateTypes(compilation.Assembly.GlobalNamespace)
				.Where(type => IsScanCandidate(type, marker, compilation)));
		}
		else
		{
			foreach (IAssemblySymbol assembly in assemblies)
			{
				AddAssemblyCandidates(assembly, marker, compilation, candidates, location, diagnostics);
			}
		}

		// Cross-assembly enumeration order is not guaranteed stable; sort by fully-qualified name so the
		// generated output (and any snapshot) is reproducible across builds.
		candidates.Sort((left, right) => string.CompareOrdinal(
			left.ToDisplayString(FullyQualified),
			right.ToDisplayString(FullyQualified)));
		return candidates;
	}

	// The assemblies named by InAssembliesOf (each entry's containing assembly, deduped). Null when the argument
	// is unset (or explicitly null), which means the container's own assembly is scanned instead; an empty list
	// means the argument was set but resolved to no assembly (AWT143).
	private static List<IAssemblySymbol>? ScanAssemblies(AttributeData attribute)
	{
		List<IAssemblySymbol>? assemblies = null;
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key != "InAssembliesOf" || argument.Value.Kind != TypedConstantKind.Array || argument.Value.IsNull)
			{
				continue;
			}

			assemblies ??= new List<IAssemblySymbol>();
			foreach (TypedConstant element in argument.Value.Values)
			{
				if (element.Value is INamedTypeSymbol markerType
				    && !assemblies.Contains(markerType.ContainingAssembly, SymbolEqualityComparer.Default))
				{
					assemblies.Add(markerType.ContainingAssembly);
				}
			}
		}

		return assemblies;
	}

	// Appends one referenced assembly's scan candidates, reporting AWT140 when it holds none - almost always a
	// missing ProjectReference or the wrong marker type.
	private static void AddAssemblyCandidates(
		IAssemblySymbol assembly,
		INamedTypeSymbol marker,
		Compilation compilation,
		List<INamedTypeSymbol> candidates,
		Location? location,
		List<DiagnosticInfo> diagnostics)
	{
		int contributed = candidates.Count;
		candidates.AddRange(EnumerateTypes(assembly.GlobalNamespace)
			.Where(type => IsScanCandidate(type, marker, compilation)));

		if (candidates.Count == contributed)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanAssemblyHasNoCandidates,
				LocationInfo.From(location),
				new EquatableArray<string>([assembly.Name,])));
		}
	}

	// Whether a type is a concrete class assignable to the scanned marker (and not the marker itself) - the
	// per-type predicate shared by candidate gathering and the AWT140 emptiness check. For an unbound generic
	// marker (typeof(IView<>)) the type must implement a closed form of it instead. A generic type definition
	// (Handler<T>, or a type nested inside one) has no closed form to construct, and a type the container's
	// assembly cannot access (a private nested class, or an internal class in a referenced assembly without
	// InternalsVisibleTo) cannot be referenced from the generated code - both are skipped rather than emitted
	// as uncompilable registrations.
	private static bool IsScanCandidate(INamedTypeSymbol type, INamedTypeSymbol marker, Compilation compilation)
	{
		if (type is not { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsImplicitClass: false, }
		    || SymbolEqualityComparer.Default.Equals(type, marker)
		    || HasOpenTypeParameters(type)
		    || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
		{
			return false;
		}

		return IsOpenGenericMarker(marker)
			? ClosedMarkerForms(type, marker.OriginalDefinition).Count > 0
			: compilation.HasImplicitConversion(type, marker);
	}

	// Whether a type declares type parameters of its own or is nested inside a type that does - either way there
	// is no single closed type the generated container could construct.
	private static bool HasOpenTypeParameters(INamedTypeSymbol type)
	{
		for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
		{
			if (current.Arity > 0)
			{
				return true;
			}
		}

		return false;
	}

	// A single overridable, collection-eligible registration contributed by a [Scan]: IsScan so it never conflicts
	// with an explicit registration over the same implementation and always joins its service's collection, and
	// carrying the scan's SkipUnconstructable opt-in for the unconstructable-match prune.
	private static RawRegistration ScanRegistration(string service, string implementation, INamedTypeSymbol type, INamedTypeSymbol serviceSymbol, ScanMatch match)
		=> new(service, implementation, match.Lifetime, type, match.Location, ProductionKind.Constructor, null, false, null, serviceSymbol, true, match.SkipUnconstructable);

	// The scanned marker: the type argument of the generic [Scan<TMarker>], or the typeof(...) constructor
	// argument of the non-generic [Scan(typeof(TMarker))]. Null when the attribute is malformed.
	private static INamedTypeSymbol? ScanMarker(AttributeData attribute, INamedTypeSymbol attributeClass)
	{
		if (attributeClass.IsGenericType)
		{
			return attributeClass.TypeArguments.Length == 1
				? attributeClass.TypeArguments[0] as INamedTypeSymbol
				: null;
		}

		return attribute.ConstructorArguments.Length == 1
			? attribute.ConstructorArguments[0].Value as INamedTypeSymbol
			: null;
	}

	// The lifetime named on a [Scan] (Lifetime = AwaitenLifetime.X); its underlying int lines up with the
	// generator's Lifetime enum. Defaults to Transient when unset, matching the attribute default.
	private static Lifetime ScanLifetime(AttributeData attribute)
	{
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == "Lifetime" && argument.Value.Value is int value)
			{
				return (Lifetime)value;
			}
		}

		return Lifetime.Transient;
	}

	// The SkipUnconstructable flag named on a [Scan]: when set, a match the container cannot construct is
	// skipped with AWT141 instead of erroring. Defaults to false when unset, matching the attribute default.
	private static bool ScanSkipsUnconstructable(AttributeData attribute)
	{
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == "SkipUnconstructable" && argument.Value.Value is bool value)
			{
				return value;
			}
		}

		return false;
	}

	// The exposure named on a [Scan] (As = ScanAs.X); its underlying int lines up with the generator's
	// ScanExposure enum. Defaults to Self when unset, matching the attribute default.
	private static ScanExposure ScanExposureOf(AttributeData attribute)
	{
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == "As" && argument.Value.Value is int value)
			{
				return (ScanExposure)value;
			}
		}

		return ScanExposure.Self;
	}

	// Every named type in an assembly, walking nested types and child namespaces, so a [Scan] can consider
	// every concrete class the container's assembly declares.
	private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
	{
		foreach (INamedTypeSymbol type in ns.GetTypeMembers())
		{
			yield return type;
			foreach (INamedTypeSymbol nested in EnumerateNested(type))
			{
				yield return nested;
			}
		}

		foreach (INamespaceSymbol child in ns.GetNamespaceMembers())
		{
			foreach (INamedTypeSymbol type in EnumerateTypes(child))
			{
				yield return type;
			}
		}
	}

	private static IEnumerable<INamedTypeSymbol> EnumerateNested(INamedTypeSymbol type)
	{
		foreach (INamedTypeSymbol nested in type.GetTypeMembers())
		{
			yield return nested;
			foreach (INamedTypeSymbol deeper in EnumerateNested(nested))
			{
				yield return deeper;
			}
		}
	}

	/// <summary>
	///     Drops every implementation contributed by a <c>[Scan(SkipUnconstructable = true)]</c> that the
	///     container cannot construct - one with no accessible constructor, or with a constructor parameter or
	///     <c>[Inject]</c> member whose service has no registration - reporting the skip as a warning (AWT141)
	///     where the default keeps the AWT101 error. Such a scan sweeps every concrete class assignable to its
	///     marker, so an incidentally-matched helper type must not break the build. An implementation that is
	///     also registered explicitly, or matched by a scan without the opt-in, is never dropped (asking for a
	///     type by name - or scanning without opting in - makes its missing dependency a real fault). Iterates
	///     to a fixpoint, because dropping one match can orphan another scanned match that depended on one of
	///     its services.
	/// </summary>
	private static void PruneUnconstructableScanMatches(
		List<RawRegistration> raw,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		bool importServices,
		HashSet<string> constraintRejected,
		List<DiagnosticInfo> diagnostics)
	{
		if (!raw.Any(registration => registration.ScanSkipsUnconstructable))
		{
			return;
		}

		// Iterate to a fixpoint: each round re-derives the shrinking satisfiable surface, because dropping one
		// match can orphan another scanned match that depended on one of its services.
		bool dropped = true;
		while (dropped)
		{
			dropped = PruneUnconstructableScanRound(raw, containerSymbol, compilation, importServices, constraintRejected, diagnostics);
		}
	}

	/// <summary>
	///     One prune pass over the opted-in scan matches: drops each implementation unconstructable against the
	///     round's satisfiable surface (every registered service, plus the variance candidates a Direct/Func
	///     parameter could be redirected to), reporting AWT141, and returns whether anything was dropped so the
	///     caller can iterate to a fixpoint. The surface is a snapshot from the round's start, so a drop mid-round
	///     can leave a stale verdict for a later implementation - the next round re-checks against the shrunk surface.
	/// </summary>
	private static bool PruneUnconstructableScanRound(
		List<RawRegistration> raw,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		bool importServices,
		HashSet<string> constraintRejected,
		List<DiagnosticInfo> diagnostics)
	{
		// The satisfiable surface as of this round: every registered (service, key), plus the variance candidates
		// a Direct/Func parameter could be redirected to. An implementation with any registration that did not opt
		// in - an explicit one, or a scan without SkipUnconstructable - is pinned to error semantics.
		HashSet<ServiceKey> services = new();
		HashSet<string> pinnedImpls = new(StringComparer.Ordinal);
		List<(string ServiceType, INamedTypeSymbol Symbol)> varianceCandidates = new();
		HashSet<string> varianceSeen = new(StringComparer.Ordinal);
		foreach (RawRegistration registration in raw)
		{
			services.Add(new ServiceKey(registration.ServiceType, registration.Key));
			if (!registration.ScanSkipsUnconstructable)
			{
				pinnedImpls.Add(registration.ImplementationType);
			}

			if (registration.Key is null
			    && registration.ServiceSymbol is { IsGenericType: true, TypeKind: TypeKind.Interface, } variantService
			    && HasDeclaredVariance(variantService)
			    && varianceSeen.Add(registration.ServiceType))
			{
				varianceCandidates.Add((registration.ServiceType, variantService));
			}
		}

		VarianceState variance = new(varianceCandidates, compilation);

		// Check each opted-in implementation once (its registrations share symbol and location).
		bool dropped = false;
		HashSet<string> checkedImpls = new(StringComparer.Ordinal);
		foreach (RawRegistration registration in raw.Where(r => r.ScanSkipsUnconstructable).ToList())
		{
			if (pinnedImpls.Contains(registration.ImplementationType)
			    || !checkedImpls.Add(registration.ImplementationType)
			    || FirstUnconstructableReason(registration.Implementation, containerSymbol, services, constraintRejected, importServices, variance) is not { } reason)
			{
				continue;
			}

			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanMatchSkipped,
				LocationInfo.From(registration.Location),
				new EquatableArray<string>([DisplayInstance(registration.ImplementationType), reason,])));
			raw.RemoveAll(r => r.IsScan && r.ImplementationType == registration.ImplementationType);
			dropped = true;
		}

		return dropped;
	}

	/// <summary>
	///     The reason a scanned implementation cannot be constructed - a human-readable fragment for AWT141 - or
	///     <see langword="null" /> when every dependency is satisfiable. Mirrors the checks that would otherwise
	///     surface as AWT101 in <see cref="BuildInstance" />: the always-satisfiable kinds (a runtime argument, a
	///     collection - empty is legal - and an external <c>[FromServices]</c> dependency), the
	///     <c>[ImportServices]</c> fall-through, the registered service set, constraint-rejected services (their
	///     AWT126 already errors) and variance redirection. A not-settable or <c>[Arg]</c>-marked injected member
	///     is not a reason: those stay the targeted AWT136/AWT137 errors - real faults in the type, not a scan
	///     having swept in a type the graph cannot satisfy.
	/// </summary>
	private static string? FirstUnconstructableReason(
		INamedTypeSymbol implementation,
		INamedTypeSymbol containerSymbol,
		HashSet<ServiceKey> services,
		HashSet<string> constraintRejected,
		bool importServices,
		VarianceState variance)
	{
		IMethodSymbol? constructor = SelectConstructor(
			implementation, containerSymbol, services.Select(service => service.Service), importServices: importServices);
		if (constructor is null)
		{
			return "it has no constructor accessible to the container";
		}

		foreach (IParameterSymbol parameter in constructor.Parameters)
		{
			ParameterModel model = ClassifyParameter(parameter, asyncFactory: false);
			bool satisfiable =
				model.Kind is DependencyKind.Arg or DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable or DependencyKind.KeyedCollection or DependencyKind.External
				|| (importServices && model is { Kind: DependencyKind.Direct, Key: null, })
				|| services.Contains(KeyOf(model))
				|| constraintRejected.Contains(model.ServiceType)
				|| IsVarianceSatisfiable(model, parameter.Type, variance);
			if (!satisfiable)
			{
				return $"it requires '{DisplayKeyed(model.ServiceType, model.Key)}', which is not registered";
			}
		}

		foreach (IPropertySymbol property in InjectedProperties(implementation))
		{
			// An [Inject] member resolves from the graph like a Direct constructor parameter (no external
			// fall-through, no variance redirect - mirroring ClassifyInjectedMember).
			ParameterModel member = ClassifyDependency(property.Type, property.GetAttributes(), asyncFactory: false, location: null);
			if (property.SetMethod is not { } setter || !IsAccessibleSetter(setter, containerSymbol) || member.Kind == DependencyKind.Arg)
			{
				continue;
			}

			if (member.Kind is not (DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.KeyedCollection)
			    && !services.Contains(KeyOf(member))
			    && !constraintRejected.Contains(member.ServiceType))
			{
				return $"its injected member '{property.Name}' requires '{DisplayKeyed(member.ServiceType, member.Key)}', which is not registered";
			}
		}

		return null;
	}

	// Whether a Direct/Func dependency with no exact registration would be satisfied by variance redirection
	// (mirroring RedirectVariance): the declared type unwraps to the classified service and a
	// variance-compatible registration exists.
	private static bool IsVarianceSatisfiable(ParameterModel model, ITypeSymbol declaredType, VarianceState variance)
		=> model.Key is null
		   && model.Kind is DependencyKind.Direct or DependencyKind.Func
		   && UnderlyingServiceType(declaredType) is { } requested
		   && requested.ToDisplayString(FullyQualified) == model.ServiceType
		   && FindVarianceMatch(requested, model.ServiceType, variance) is not null;
}
