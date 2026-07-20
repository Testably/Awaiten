using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     Expands each <c>[Scan]</c> on the container into overridable registrations for every concrete class
	///     assignable to the marker, exposed per <c>ScanAs</c>. An unbound generic marker matches a type
	///     implementing a closed form of it, registered under that closed interface (Autofac's
	///     <c>AsClosedTypesOf</c>). Covers the container's own assembly, or the assemblies named by
	///     <c>InAssembliesOf</c>, sorted by name for reproducibility, then narrowed by the optional
	///     <c>NamePatterns</c>/<c>NamespacePatterns</c>/<c>Exclude</c> filters. Abstract/static classes, generic
	///     definitions and the marker itself are skipped; a match the generated container cannot name is reported as
	///     AWT193 and then dropped. A markerless scan (the parameterless <c>[Scan]</c>) matches every concrete type
	///     instead, narrowed by those same filters. Reports
	///     AWT138/AWT139/AWT140/AWT143/AWT172/AWT173/AWT174/AWT182/AWT183/AWT184/AWT185/AWT187/AWT188/AWT193. The synthesized
	///     registrations are <see cref="RawRegistration.IsScan" />, so an explicit registration wins single
	///     resolution while every match still joins its collection.
	/// </summary>
	private static List<RawRegistration> CollectScans(
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		List<RawRegistration> result = new();

		foreach (AttributeData attribute in containerSymbol.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "ScanAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
			{
				continue;
			}

			if (ScanMarker(attribute, attributeClass) is { } marker)
			{
				ExpandScan(attribute, marker, compilation, result, diagnostics);
			}
			else if (IsMarkerlessScan(attribute, attributeClass))
			{
				ExpandScan(attribute, null, compilation, result, diagnostics);
			}
		}

		return result;
	}

	/// <summary>
	///     Whether a <c>[Scan]</c> is the markerless form: the non-generic attribute invoked through its
	///     parameterless constructor (no <c>typeof</c> argument). The generic <c>[Scan&lt;TMarker&gt;]</c> always
	///     carries a marker, and a non-generic <c>[Scan(...)]</c> with a malformed argument is not markerless.
	/// </summary>
	private static bool IsMarkerlessScan(AttributeData attribute, INamedTypeSymbol attributeClass)
		=> !attributeClass.IsGenericType && attribute.ConstructorArguments.Length == 0;

	/// <summary>
	///     Expands one <c>[Scan]</c> over its candidate types, registering each match per <c>ScanAs</c>. An unbound
	///     generic marker matches implementers of any closed form of it, registered under that closed interface,
	///     while a closed marker matches types assignable to it. A markerless scan (<paramref name="marker" /> is
	///     <see langword="null" />) matches every concrete type, narrowed by its filters. Every assignable match is
	///     counted even when an explicit registration overrides it, so AWT138 fires only when nothing matched.
	/// </summary>
	private static void ExpandScan(
		AttributeData attribute,
		INamedTypeSymbol? marker,
		Compilation compilation,
		List<RawRegistration> result,
		List<DiagnosticInfo> diagnostics)
	{
		Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

		// AWT143: an InAssembliesOf that resolves to no assembly (an empty array, or entries naming no type)
		// is reported and the scan registers nothing. It is not redirected to the container's own assembly,
		// which is what an unset InAssembliesOf means.
		List<IAssemblySymbol>? assemblies = ScanAssemblies(attribute);
		if (assemblies is { Count: 0, })
		{
			diagnostics.Add(new DiagnosticInfo(Diagnostics.ScanAssembliesEmpty, LocationInfo.From(location), new EquatableArray<string>([])));
			return;
		}

		ScanMatch match = new(ScanExposureOf(attribute), ScanLifetime(attribute), location, ScanSkipsUnconstructable(attribute));
		ScanFilters filters = ScanFiltersOf(attribute);

		// AWT185: As resolved to no recognized ScanAs flag (e.g. `Self & Marker`, or an out-of-range cast), so the
		// scan would expose nothing.
		if ((match.Exposure & ScanExposures.All) == 0)
		{
			diagnostics.Add(new DiagnosticInfo(Diagnostics.ScanExposesNothing, LocationInfo.From(location), new EquatableArray<string>([])));
			return;
		}

		// AWT183: a markerless scan cannot register under a marker it does not name, and needs a scoping filter, or
		// it would register every concrete type in scope. Rejected before any registration.
		if (marker is null && MarkerlessScanError(match.Exposure, filters, assemblies) is { } reason)
		{
			diagnostics.Add(new DiagnosticInfo(Diagnostics.MarkerlessScanInvalid, LocationInfo.From(location), new EquatableArray<string>([reason,])));
			return;
		}

		bool openMarker = marker is not null && IsOpenGenericMarker(marker);
		INamedTypeSymbol? markerDefinition = marker?.OriginalDefinition;

		// Null for a markerless scan; otherwise the marker's display string. The null both feeds the marker-only
		// diagnostics and tells ReportScanFilterDiagnostics which "nothing happened" signal to use.
		string? markerDisplay = null;
		if (marker is not null)
		{
			markerDisplay = (openMarker ? markerDefinition! : marker).ToDisplayString(FullyQualified);
		}

		ScanFilterHits hits = new();
		int assignable = 0;
		int registered = 0;
		int produced = 0;
		foreach (ScanCandidate candidate in ScanCandidates(assemblies, compilation, marker, location, diagnostics))
		{
			assignable++;
			if (!PassesScanFilters(candidate.Type, filters, hits))
			{
				continue;
			}

			registered++;

			// AWT193: the match survived the filters, so the author does mean to register it, but the generated
			// container cannot name the type and no registration could reference it. Reported here rather than
			// during gathering, where it would fire once per internal type in every scanned assembly, and unlike
			// the other per-match warnings also for a markerless scan: AWT183 forces one to narrow its candidates,
			// so a match reaching this point was asked for either way.
			if (candidate.Inaccessible)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ScanTypeInaccessible,
					LocationInfo.From(location),
					new EquatableArray<string>([Display(candidate.Type.ToDisplayString(FullyQualified)),])));
				continue;
			}

			produced += RegisterScanMatch(candidate.Type, ScanContracts(candidate.Type, match, marker, openMarker, markerDefinition, compilation), markerDisplay, match, result, diagnostics);
		}

		ReportScanFilterDiagnostics(filters, hits, new ScanFilterCounts(assignable, registered, produced), assemblies, markerDisplay, location, diagnostics);
	}

	/// <summary>
	///     The interfaces one match registers under, unioned across the requested exposures: the marker interfaces
	///     when <c>Marker</c> is set (never for a markerless scan, which names none) and the <c>I</c> + name
	///     convention interface when <c>MatchingInterface</c> is set. An interface the generated code could not
	///     reference (internal to another assembly) is dropped, like an inaccessible candidate type, and returned
	///     alongside so an exposure emptied by the drop reports AWT188 instead of a "not implemented" warning.
	///     <see cref="ScanContractSet.AmbiguousMatching" /> flags several accessible same-named convention
	///     interfaces, which all register (the caller reports AWT187). Deduplicated by fully-qualified name,
	///     since a combined <c>Marker | MatchingInterface</c> can select the same interface twice.
	/// </summary>
	private static ScanContractSet ScanContracts(
		INamedTypeSymbol type,
		ScanMatch match,
		INamedTypeSymbol? marker,
		bool openMarker,
		INamedTypeSymbol? markerDefinition,
		Compilation compilation)
	{
		List<INamedTypeSymbol> contracts = new();
		if (match.RegisterMarker && marker is not null)
		{
			contracts.AddRange(openMarker ? ClosedMarkerInterfaces(type, markerDefinition!) : MarkerInterfaces(type, marker, compilation));
		}

		List<INamedTypeSymbol> matching = new();
		if (match.RegisterMatchingInterface)
		{
			matching = MatchingInterfaces(type);
			contracts.AddRange(matching);
		}

		List<INamedTypeSymbol> inaccessible = new();
		contracts.RemoveAll(contract =>
		{
			if (compilation.IsSymbolAccessibleWithin(contract, compilation.Assembly))
			{
				return false;
			}

			inaccessible.Add(contract);
			return true;
		});

		HashSet<string> seen = new(StringComparer.Ordinal);
		contracts.RemoveAll(contract => !seen.Add(contract.ToDisplayString(FullyQualified)));

		// The convention tie is ambiguous only when several same-named interfaces actually register: one dropped
		// as inaccessible above cannot make the registration ambiguous (it is reported separately, as AWT188).
		bool ambiguousMatching = matching.Count(contract => compilation.IsSymbolAccessibleWithin(contract, compilation.Assembly)) > 1;
		return new ScanContractSet(contracts, inaccessible, ambiguousMatching);
	}

	/// <summary>
	///     The outcome of contract selection for one scan match: the interfaces it registers under, the
	///     interfaces an exposure selected but had to drop because the generated code cannot reference them
	///     (they feed AWT188 when the match ends up contributing nothing), and whether several same-named
	///     convention interfaces register because no own-namespace winner decided the tie (AWT187).
	/// </summary>
	private sealed record ScanContractSet(List<INamedTypeSymbol> Contracts, List<INamedTypeSymbol> Inaccessible, bool AmbiguousMatching);

	/// <summary>
	///     The reason a markerless <c>[Scan]</c> is invalid (an AWT183 fragment), or <see langword="null" /> when it
	///     is well-formed: the <c>Marker</c> exposure has no marker to register under, and a scan that does not
	///     positively bound its candidates would sweep every concrete type in scope. A name or namespace axis scopes
	///     the scan only when it declares includes and every one of them names something: because the includes on an
	///     axis are OR-combined, a single unbounded pattern (like <c>*</c>) leaves the whole axis unbounded even
	///     alongside narrower patterns. An <c>InAssembliesOf</c> also scopes; an exclude-only filter does not.
	/// </summary>
	private static string? MarkerlessScanError(ScanExposures exposure, ScanFilters filters, List<IAssemblySymbol>? assemblies)
	{
		if ((exposure & ScanExposures.Marker) != 0)
		{
			return "includes the Marker exposure, which registers under a marker it does not name; use Self and/or MatchingInterface, or name a marker";
		}

		bool nameScopes = filters.NameIncludes.Count > 0
		                  && filters.NameIncludes.All(pattern => !NamePatternMatchesEverything(pattern));
		bool namespaceScopes = filters.NamespaceIncludes.Count > 0
		                       && filters.NamespaceIncludes.All(NamespacePatternScopes);
		bool scoped = nameScopes || namespaceScopes || assemblies is not null;
		return scoped
			? null
			: "declares no narrowing NamePatterns, NamespacePatterns or InAssembliesOf, so it would register every concrete type; add a filter to scope it";
	}

	/// <summary>
	///     Whether a name include is only <c>*</c> wildcards, matching every simple name: it cannot scope a
	///     markerless scan (AWT183) and is redundant as a filter (AWT174).
	/// </summary>
	private static bool NamePatternMatchesEverything(string pattern)
		=> pattern.All(character => character == '*');

	/// <summary>
	///     Whether a namespace include matches every namespace, i.e. every segment is <c>**</c> (which absorbs any
	///     run of segments). Feeds AWT174; the AWT183 scoping check uses the stricter
	///     <see cref="NamespacePatternScopes" />, since a pattern can fall short of matching everything and still
	///     not scope.
	/// </summary>
	private static bool NamespacePatternMatchesEverything(string pattern)
		=> pattern.Split('.').All(segment => segment == "**");

	/// <summary>
	///     Whether a namespace include positively bounds a markerless scan: at least one segment contains a
	///     non-wildcard character. A wildcard-only pattern (<c>*</c>, <c>**.*</c>) constrains at most the segment
	///     depth, which still sweeps effectively every concrete type, so it does not count as a scope for AWT183.
	/// </summary>
	private static bool NamespacePatternScopes(string pattern)
		=> pattern.Split('.').Any(segment => segment.Any(character => character != '*'));

	/// <summary>
	///     Registers one scan match per the <c>ScanAs</c> flags: as its own concrete type and under each contract
	///     interface, returning how many registrations it contributed. A marker match that contributed nothing
	///     reports AWT188 per interface that was found but dropped as inaccessible, else AWT139 (<c>Marker</c>
	///     requested but no assignable interface, usual cause: a base-type marker) or AWT182
	///     (<c>MatchingInterface</c> requested but no <c>I</c> + name interface). Setting the <c>Self</c> flag
	///     exempts all three, since the self registration still covers the type. A match that registered under
	///     several same-named convention interfaces (an unresolved tie) reports AWT187. A markerless match is
	///     never warned: not conforming to the convention is the normal case when scanning broadly.
	/// </summary>
	private static int RegisterScanMatch(
		INamedTypeSymbol type,
		ScanContractSet contracts,
		string? markerDisplay,
		ScanMatch match,
		List<RawRegistration> result,
		List<DiagnosticInfo> diagnostics)
	{
		string typeName = type.ToDisplayString(FullyQualified);
		int produced = 0;

		if (match.RegisterSelf)
		{
			result.Add(ScanRegistration(typeName, typeName, type, type, match));
			produced++;
		}

		foreach (INamedTypeSymbol contract in contracts.Contracts)
		{
			result.Add(ScanRegistration(contract.ToDisplayString(FullyQualified), typeName, type, contract, match));
			produced++;
		}

		// AWT187: no own-namespace winner decided the convention tie, so several same-named interfaces all
		// registered above — usually one of them is an incidental same-named interface. Like the other per-match
		// warnings, a markerless scan is exempt.
		if (contracts.AmbiguousMatching && markerDisplay is not null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanAmbiguousMatchingInterfaces,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([Display(typeName), Display("I" + type.Name),])));
		}

		// The match contributed nothing: an interface exposure was requested but found no interface to register
		// under (and Self was not requested, which always registers). Warn only for a marker scan (a non-null
		// markerDisplay); a markerless scan skips a non-conforming type silently and reports emptiness once, at
		// the scan level (AWT184).
		if (produced == 0 && markerDisplay is not null)
		{
			if (contracts.Inaccessible.Count > 0)
			{
				// AWT188: the exposure did find its interface, but the generated code cannot reference it; the
				// "implements no interface" warnings would mislead, so name the inaccessible interface instead.
				foreach (INamedTypeSymbol contract in contracts.Inaccessible)
				{
					diagnostics.Add(new DiagnosticInfo(
						Diagnostics.ScanInterfaceInaccessible,
						LocationInfo.From(match.Location),
						new EquatableArray<string>([Display(typeName), Display(contract.ToDisplayString(FullyQualified)),])));
				}
			}
			else if (match.RegisterMarker)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ScanNoImplementedInterfaces,
					LocationInfo.From(match.Location),
					new EquatableArray<string>([Display(typeName), Display(markerDisplay),])));
			}
			else if (match.RegisterMatchingInterface)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ScanNoMatchingInterface,
					LocationInfo.From(match.Location),
					new EquatableArray<string>([Display(typeName), Display("I" + type.Name),])));
			}
		}

		return produced;
	}

	/// <summary>
	///     The interfaces the <c>MatchingInterface</c> exposure registers a match under: the interfaces it
	///     implements whose name is <c>I</c> + the match's own name (the <c>Foo</c>/<c>IFoo</c> convention),
	///     preferring those declared in the match's own namespace. Namespaces are compared by name, so a convention
	///     interface in a same-named namespace of another assembly (a contracts project sharing the root namespace)
	///     still counts as the type's own. When no candidate is in the own namespace, every same-named match
	///     registers, deterministically ordered (the caller reports the ambiguity as AWT187). Generic interfaces
	///     are excluded, and assignability is preserved
	///     (the interface is drawn from the type's implemented set, not synthesized from the name).
	/// </summary>
	private static List<INamedTypeSymbol> MatchingInterfaces(INamedTypeSymbol type)
	{
		string expected = "I" + type.Name;
		List<INamedTypeSymbol> matches = type.AllInterfaces
			.Where(contract => !contract.IsGenericType && contract.Name == expected)
			.ToList();

		string ownNamespace = type.ContainingNamespace.ToDisplayString();
		List<INamedTypeSymbol> sameNamespace = matches
			.Where(contract => contract.ContainingNamespace.ToDisplayString() == ownNamespace)
			.ToList();
		List<INamedTypeSymbol> selected = sameNamespace.Count > 0 ? sameNamespace : matches;

		selected.Sort((left, right) => string.CompareOrdinal(
			left.ToDisplayString(FullyQualified),
			right.ToDisplayString(FullyQualified)));
		return selected;
	}

	/// <summary>
	///     The interfaces a closed marker registers a match under: every implemented interface assignable to the
	///     marker (itself and any more-derived service interfaces), never unrelated interfaces such as
	///     <c>IDisposable</c>.
	/// </summary>
	private static List<INamedTypeSymbol> MarkerInterfaces(INamedTypeSymbol type, INamedTypeSymbol marker, Compilation compilation)
		=> type.AllInterfaces.Where(contract => compilation.HasImplicitConversion(contract, marker)).ToList();

	/// <summary>
	///     The interfaces an open marker registers a match under: the closed forms of the marker the type
	///     implements, restricted to interfaces. A base-type marker yields none, which surfaces as AWT139.
	/// </summary>
	private static List<INamedTypeSymbol> ClosedMarkerInterfaces(INamedTypeSymbol type, INamedTypeSymbol markerDefinition)
		=> ClosedMarkerForms(type, markerDefinition).Where(contract => contract.TypeKind == TypeKind.Interface).ToList();

	/// <summary>
	///     The per-scan settings shared by every match of one <c>[Scan]</c>: how matches are exposed, the lifetime,
	///     the attribute location for diagnostics, and whether an unconstructable match is skipped with a warning
	///     (<c>SkipUnconstructable</c>) instead of erroring. Bundled so the per-match registration takes one handle.
	/// </summary>
	private sealed record ScanMatch(ScanExposures Exposure, Lifetime Lifetime, Location? Location, bool SkipUnconstructable)
	{
		public bool RegisterSelf => (Exposure & ScanExposures.Self) != 0;

		public bool RegisterMarker => (Exposure & ScanExposures.Marker) != 0;

		public bool RegisterMatchingInterface => (Exposure & ScanExposures.MatchingInterface) != 0;
	}

	/// <summary>
	///     Whether the scanned marker is an unbound/open generic definition (<c>typeof(IView&lt;&gt;)</c>): either
	///     Roslyn flagged it <c>IsUnboundGenericType</c>, or it has arity and is its own definition (its type
	///     arguments are the bare type parameters).
	/// </summary>
	private static bool IsOpenGenericMarker(INamedTypeSymbol marker)
		=> marker.IsUnboundGenericType
		   || (marker.Arity > 0 && SymbolEqualityComparer.Default.Equals(marker, marker.OriginalDefinition));

	/// <summary>
	///     The distinct closed forms of <paramref name="markerDefinition" /> that <paramref name="type" />
	///     implements (its interfaces) or inherits (its base types), e.g. <c>IView&lt;VM1&gt;</c> and
	///     <c>IView&lt;VM2&gt;</c> for a type that implements the marker at two type arguments. Ordered by
	///     fully-qualified name so multiple matches register deterministically.
	/// </summary>
	private static List<INamedTypeSymbol> ClosedMarkerForms(INamedTypeSymbol type, INamedTypeSymbol markerDefinition)
	{
		List<INamedTypeSymbol> closed = new();
		HashSet<string> seen = new(StringComparer.Ordinal);

		// A match may close the marker as an interface it implements or as a base type it inherits.
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

	/// <summary>
	///     A type and every base type up its inheritance chain, so a marker closed as a base type is found
	///     alongside one closed as an interface.
	/// </summary>
	private static IEnumerable<INamedTypeSymbol> SelfAndBaseTypes(INamedTypeSymbol type)
	{
		for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
		{
			yield return current;
		}
	}

	/// <summary>
	///     The concrete-type candidates a <c>[Scan]</c> enumerates: the container's own assembly when
	///     <paramref name="assemblies" /> is <see langword="null" /> (<c>InAssembliesOf</c> unset), else the
	///     listed assemblies. Filtered to concrete classes assignable to the marker (or every concrete class when
	///     <paramref name="marker" /> is <see langword="null" />, a markerless scan) and sorted by fully-qualified
	///     name so the registrations are reproducible. Reports AWT140 for any <c>InAssembliesOf</c> assembly that
	///     holds no such type (a likely missing <c>ProjectReference</c>).
	/// </summary>
	private static List<ScanCandidate> ScanCandidates(
		List<IAssemblySymbol>? assemblies,
		Compilation compilation,
		INamedTypeSymbol? marker,
		Location? location,
		List<DiagnosticInfo> diagnostics)
	{
		List<ScanCandidate> candidates = new();

		if (assemblies is null)
		{
			candidates.AddRange(ScanCandidatesIn(compilation.Assembly.GlobalNamespace, marker, compilation));
		}
		else
		{
			foreach (IAssemblySymbol assembly in assemblies)
			{
				AddAssemblyCandidates(assembly, marker, compilation, candidates, location, diagnostics);
			}
		}

		// Cross-assembly enumeration order is not guaranteed stable; sort by fully-qualified name so the
		// generated output (and any snapshot) is reproducible.
		candidates.Sort((left, right) => string.CompareOrdinal(
			left.Type.ToDisplayString(FullyQualified),
			right.Type.ToDisplayString(FullyQualified)));
		return candidates;
	}

	/// <summary>
	///     The scan candidates one namespace tree contributes: every type the marker matched, each flagged with
	///     whether the generated container can name it. The accessibility verdict is carried rather than acted on,
	///     mirroring <see cref="ScanContractSet.Inaccessible" /> on the contract side, so that it is the caller (which
	///     has applied the scan's filters) that decides between reporting AWT193 and staying silent.
	/// </summary>
	private static IEnumerable<ScanCandidate> ScanCandidatesIn(INamespaceSymbol ns, INamedTypeSymbol? marker, Compilation compilation)
		=> EnumerateTypes(ns)
			.Where(type => IsScanCandidate(type, marker, compilation))
			.Select(type => new ScanCandidate(type, !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly)));

	/// <summary>
	///     One type a <c>[Scan]</c> matched, and whether it is inaccessible to the generated container (internal to
	///     another assembly, or a private/protected nested type), in which case nothing can be registered for it.
	/// </summary>
	private readonly record struct ScanCandidate(INamedTypeSymbol Type, bool Inaccessible);

	/// <summary>
	///     The assemblies named by <c>InAssembliesOf</c> (each entry's containing assembly, deduped). Null when the
	///     argument is unset or explicitly null, meaning the container's own assembly is scanned instead; an empty
	///     list means the argument was set but resolved to no assembly (AWT143).
	/// </summary>
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

	/// <summary>
	///     Appends one referenced assembly's scan candidates, reporting AWT140 when it holds none (almost always a
	///     missing <c>ProjectReference</c> or the wrong marker type). An inaccessible match still counts as a
	///     candidate: it is the more specific AWT193, not a "found nothing at all" hint, that the assembly earns.
	/// </summary>
	private static void AddAssemblyCandidates(
		IAssemblySymbol assembly,
		INamedTypeSymbol? marker,
		Compilation compilation,
		List<ScanCandidate> candidates,
		Location? location,
		List<DiagnosticInfo> diagnostics)
	{
		int contributed = candidates.Count;
		candidates.AddRange(ScanCandidatesIn(assembly.GlobalNamespace, marker, compilation));

		if (candidates.Count == contributed)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanAssemblyHasNoCandidates,
				LocationInfo.From(location),
				new EquatableArray<string>([assembly.Name,])));
		}
	}

	/// <summary>
	///     Whether a type is a concrete class assignable to the marker (and not the marker itself), shared by
	///     candidate gathering and the AWT140 emptiness check. An unbound generic marker requires the type to
	///     implement a closed form of it; a <see langword="null" /> marker (a markerless scan) accepts every
	///     concrete class, leaving the narrowing to the filters. Generic type definitions are skipped, since there
	///     is no closed type to construct, and so is a type with no name the generated code could write at all
	///     (<c>&lt;Module&gt;</c>, a compiler-generated closure): no author wrote it, so it must not reach AWT193.
	///     Accessibility itself deliberately plays no part here: it is decided last, on what the marker (and then the
	///     filters) already selected, so that AWT193 names only the types the author asked for rather than every
	///     internal type in a scanned assembly.
	/// </summary>
	private static bool IsScanCandidate(INamedTypeSymbol type, INamedTypeSymbol? marker, Compilation compilation)
	{
		if (type is not { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsImplicitClass: false, CanBeReferencedByName: true, }
		    || (marker is not null && SymbolEqualityComparer.Default.Equals(type, marker))
		    || HasOpenTypeParameters(type))
		{
			return false;
		}

		if (marker is null)
		{
			return true;
		}

		return IsOpenGenericMarker(marker)
			? ClosedMarkerForms(type, marker.OriginalDefinition).Count > 0
			: compilation.HasImplicitConversion(type, marker);
	}

	/// <summary>
	///     Whether a type declares type parameters of its own or is nested inside a type that does. Either way
	///     there is no single closed type the generated container could construct.
	/// </summary>
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

	/// <summary>
	///     A single overridable, collection-eligible registration contributed by a <c>[Scan]</c>: <c>IsScan</c> so
	///     it never conflicts with an explicit registration over the same implementation and always joins its
	///     service's collection, carrying the scan's <c>SkipUnconstructable</c> opt-in for the
	///     unconstructable-match prune.
	/// </summary>
	private static RawRegistration ScanRegistration(string service, string implementation, INamedTypeSymbol type, INamedTypeSymbol serviceSymbol, ScanMatch match)
		=> new(service, implementation, match.Lifetime, type, match.Location, ProductionKind.Constructor, null, false, null, serviceSymbol, true, match.SkipUnconstructable);

	/// <summary>
	///     The scanned marker: the type argument of the generic <c>[Scan&lt;TMarker&gt;]</c>, or the
	///     <c>typeof(...)</c> constructor argument of <c>[Scan(typeof(TMarker))]</c>. Null when the attribute is
	///     malformed.
	/// </summary>
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

	/// <summary>
	///     The lifetime named on a <c>[Scan]</c> (<c>Lifetime = AwaitenLifetime.X</c>); its underlying int lines up
	///     with the generator's <c>Lifetime</c> enum. Defaults to <c>Transient</c> when unset, matching the
	///     attribute default.
	/// </summary>
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

	/// <summary>
	///     The <c>SkipUnconstructable</c> flag named on a <c>[Scan]</c>: when set, a match the container cannot
	///     construct is skipped with AWT141 instead of erroring. Defaults to false when unset, matching the
	///     attribute default.
	/// </summary>
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

	/// <summary>
	///     The exposure named on a <c>[Scan]</c> (<c>As = ScanAs.X</c>); its underlying int lines up with the
	///     generator's <c>ScanExposures</c> enum. Defaults to <c>Self</c> when unset, matching the attribute default.
	/// </summary>
	private static ScanExposures ScanExposureOf(AttributeData attribute)
	{
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == "As" && argument.Value.Value is int value)
			{
				return (ScanExposures)value;
			}
		}

		return ScanExposures.Self;
	}

	/// <summary>
	///     The name, namespace and exact-type filters named on a <c>[Scan]</c>, each pattern list split into
	///     includes and <c>!</c>-prefixed excludes. Empty when the scan declares no filter.
	/// </summary>
	private sealed class ScanFilters
	{
		public List<string> NameIncludes { get; } = new();

		public List<string> NameExcludes { get; } = new();

		public List<string> NamespaceIncludes { get; } = new();

		public List<string> NamespaceExcludes { get; } = new();

		public List<INamedTypeSymbol> ExcludeTypes { get; } = new();

		public bool IsEmpty => NameIncludes.Count == 0 && NameExcludes.Count == 0
		                       && NamespaceIncludes.Count == 0 && NamespaceExcludes.Count == 0
		                       && ExcludeTypes.Count == 0;
	}

	/// <summary>Which exclusions actually removed a candidate, so an exclusion that never applied is reported as stale.</summary>
	private sealed class ScanFilterHits
	{
		public HashSet<string> NameExcludes { get; } = new(StringComparer.Ordinal);

		public HashSet<string> NamespaceExcludes { get; } = new(StringComparer.Ordinal);

		public HashSet<INamedTypeSymbol> ExcludeTypes { get; } = new(SymbolEqualityComparer.Default);
	}

	/// <summary>
	///     Candidates a scan's marker matched (<paramref name="Assignable" />), how many survived its filters
	///     (<paramref name="Registered" />), and how many registrations those survivors actually contributed
	///     (<paramref name="Produced" />, which falls short of <paramref name="Registered" /> for a survivor the
	///     container cannot name (AWT193) and for a <c>MatchingInterface</c> survivor with no <c>I</c> + name
	///     interface).
	/// </summary>
	private readonly record struct ScanFilterCounts(int Assignable, int Registered, int Produced);

	/// <summary>
	///     The <c>NamePatterns</c>, <c>NamespacePatterns</c> and <c>Exclude</c> named on a <c>[Scan]</c>. Null, empty
	///     and (for patterns) bare <c>!</c> entries are dropped.
	/// </summary>
	private static ScanFilters ScanFiltersOf(AttributeData attribute)
	{
		ScanFilters filters = new();
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			switch (argument.Key)
			{
				case "NamePatterns":
					AddPatterns(argument.Value, filters.NameIncludes, filters.NameExcludes);
					break;
				case "NamespacePatterns":
					AddPatterns(argument.Value, filters.NamespaceIncludes, filters.NamespaceExcludes);
					break;
				case "Exclude" when argument.Value.Kind == TypedConstantKind.Array && !argument.Value.IsNull:
					foreach (TypedConstant element in argument.Value.Values)
					{
						if (element.Value is INamedTypeSymbol type
						    && !filters.ExcludeTypes.Contains(type, SymbolEqualityComparer.Default))
						{
							filters.ExcludeTypes.Add(type);
						}
					}

					break;
			}
		}

		return filters;
	}

	private static void AddPatterns(TypedConstant value, List<string> includes, List<string> excludes)
	{
		if (value.Kind != TypedConstantKind.Array || value.IsNull)
		{
			return;
		}

		foreach (TypedConstant element in value.Values)
		{
			if (element.Value is not string pattern || pattern.Length == 0)
			{
				continue;
			}

			if (pattern[0] == '!')
			{
				if (pattern.Length > 1)
				{
					excludes.Add(pattern.Substring(1));
				}
			}
			else
			{
				includes.Add(pattern);
			}
		}
	}

	/// <summary>
	///     Whether a marker-assignable candidate survives the scan's filters: it matches no exclusion (name/namespace
	///     pattern or <c>Exclude</c> type) and, on each axis that declares includes, matches at least one. Records
	///     every exclusion that applied on <paramref name="hits" /> even when the candidate is dropped for another
	///     reason, so a stale exclusion can be told apart from one that did its job.
	/// </summary>
	private static bool PassesScanFilters(INamedTypeSymbol type, ScanFilters filters, ScanFilterHits hits)
	{
		if (filters.IsEmpty)
		{
			return true;
		}

		string name = type.Name;
		string ns = type.ContainingNamespace is { IsGlobalNamespace: false, } containing
			? containing.ToDisplayString()
			: string.Empty;

		bool excluded = false;
		foreach (string pattern in filters.NameExcludes.Where(pattern => NameGlob(name, pattern)))
		{
			hits.NameExcludes.Add(pattern);
			excluded = true;
		}

		foreach (string pattern in filters.NamespaceExcludes.Where(pattern => NamespaceGlob(ns, pattern)))
		{
			hits.NamespaceExcludes.Add(pattern);
			excluded = true;
		}

		foreach (INamedTypeSymbol excludedType in filters.ExcludeTypes.Where(excludedType => SymbolEqualityComparer.Default.Equals(type, excludedType)))
		{
			hits.ExcludeTypes.Add(excludedType);
			excluded = true;
		}

		if (excluded)
		{
			return false;
		}

		return (filters.NameIncludes.Count == 0 || filters.NameIncludes.Any(pattern => NameGlob(name, pattern)))
		       && (filters.NamespaceIncludes.Count == 0 || filters.NamespaceIncludes.Any(pattern => NamespaceGlob(ns, pattern)));
	}

	/// <summary>
	///     Reports the filter-related scan diagnostics after the candidate loop. For a marker scan (non-null
	///     <paramref name="markerDisplay" />): AWT138 (marker matched nothing in the own assembly) or AWT172 (the
	///     filters removed every marker match). For a markerless scan (null <paramref name="markerDisplay" />):
	///     AWT184 when it contributed no registration at all. In both cases AWT173 per exclusion that never applied,
	///     and AWT174 per include pattern that matches every candidate.
	/// </summary>
	private static void ReportScanFilterDiagnostics(
		ScanFilters filters,
		ScanFilterHits hits,
		ScanFilterCounts counts,
		List<IAssemblySymbol>? assemblies,
		string? markerDisplay,
		Location? location,
		List<DiagnosticInfo> diagnostics)
	{
		if (markerDisplay is null)
		{
			// A markerless scan does not warn per non-conforming type (that is the norm when scanning broadly), so
			// its only "nothing happened" signal is that no registration was produced across every candidate.
			if (counts.Produced == 0)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.MarkerlessScanRegisteredNothing,
					LocationInfo.From(location),
					new EquatableArray<string>([])));

				// No candidate existed at all: mirror the marker path's early return, since a stale-exclusion or
				// redundant-pattern hint is noise when the scan saw nothing to filter.
				if (counts.Assignable == 0)
				{
					return;
				}
			}
		}
		else if (counts.Assignable == 0)
		{
			// AWT138's "in this assembly" wording only fits an own-assembly scan; an InAssembliesOf scan already
			// reported the more actionable AWT140 per named assembly.
			if (assemblies is null)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ScanMatchedNothing,
					LocationInfo.From(location),
					new EquatableArray<string>([Display(markerDisplay),])));
			}

			return;
		}
		else if (counts.Registered == 0)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanFiltersMatchedNothing,
				LocationInfo.From(location),
				new EquatableArray<string>([Display(markerDisplay),])));
		}

		foreach (string pattern in filters.NameExcludes.Where(pattern => !hits.NameExcludes.Contains(pattern)))
		{
			ReportStaleExclusion("!" + pattern, location, diagnostics);
		}

		foreach (string pattern in filters.NamespaceExcludes.Where(pattern => !hits.NamespaceExcludes.Contains(pattern)))
		{
			ReportStaleExclusion("!" + pattern, location, diagnostics);
		}

		foreach (INamedTypeSymbol type in filters.ExcludeTypes.Where(type => !hits.ExcludeTypes.Contains(type)))
		{
			ReportStaleExclusion(Display(type.ToDisplayString(FullyQualified)), location, diagnostics);
		}

		foreach (string pattern in filters.NameIncludes.Where(NamePatternMatchesEverything))
		{
			ReportRedundantPattern(pattern, location, diagnostics);
		}

		foreach (string pattern in filters.NamespaceIncludes.Where(NamespacePatternMatchesEverything))
		{
			ReportRedundantPattern(pattern, location, diagnostics);
		}
	}

	private static void ReportStaleExclusion(string entry, Location? location, List<DiagnosticInfo> diagnostics)
		=> diagnostics.Add(new DiagnosticInfo(
			Diagnostics.ScanExclusionNeverMatched,
			LocationInfo.From(location),
			new EquatableArray<string>([entry,])));

	private static void ReportRedundantPattern(string pattern, Location? location, List<DiagnosticInfo> diagnostics)
		=> diagnostics.Add(new DiagnosticInfo(
			Diagnostics.ScanPatternMatchesEverything,
			LocationInfo.From(location),
			new EquatableArray<string>([pattern,])));

	/// <summary>
	///     Ordinal glob match of a simple type name: <c>*</c> matches any run of characters and the pattern is
	///     anchored to the whole name. The only wildcard; there is no <c>.</c> separator to respect.
	/// </summary>
	private static bool NameGlob(string text, string pattern)
	{
		string[] parts = pattern.Split('*');
		if (parts.Length == 1)
		{
			return string.Equals(text, pattern, StringComparison.Ordinal);
		}

		if (!text.StartsWith(parts[0], StringComparison.Ordinal)
		    || !text.EndsWith(parts[parts.Length - 1], StringComparison.Ordinal))
		{
			return false;
		}

		int index = parts[0].Length;
		int end = text.Length - parts[parts.Length - 1].Length;
		if (index > end)
		{
			return false;
		}

		for (int part = 1; part < parts.Length - 1; part++)
		{
			if (parts[part].Length == 0)
			{
				continue;
			}

			int found = text.IndexOf(parts[part], index, end - index, StringComparison.Ordinal);
			if (found < 0)
			{
				return false;
			}

			index = found + parts[part].Length;
		}

		return true;
	}

	/// <summary>
	///     Segment-aware ordinal glob match of a namespace: split on <c>.</c>, a <c>**</c> segment matches zero or
	///     more whole segments and any other segment matches exactly one via <see cref="NameGlob" />. The empty
	///     string is the global namespace (zero segments).
	/// </summary>
	private static bool NamespaceGlob(string ns, string pattern)
		=> MatchSegments(
			ns.Length == 0 ? Array.Empty<string>() : ns.Split('.'),
			0,
			pattern.Length == 0 ? Array.Empty<string>() : pattern.Split('.'),
			0);

	private static bool MatchSegments(string[] text, int textIndex, string[] pattern, int patternIndex)
	{
		while (patternIndex < pattern.Length)
		{
			if (pattern[patternIndex] == "**")
			{
				return MatchAfterDoubleStar(text, textIndex, pattern, patternIndex);
			}

			if (textIndex >= text.Length || !NameGlob(text[textIndex], pattern[patternIndex]))
			{
				return false;
			}

			textIndex++;
			patternIndex++;
		}

		return textIndex == text.Length;
	}

	/// <summary>
	///     Matches the remaining pattern starting at a run of <c>**</c> segments: collapses the run, then (if the
	///     pattern continues) tries every split of the remaining text so <c>**</c> absorbs zero or more segments.
	/// </summary>
	private static bool MatchAfterDoubleStar(string[] text, int textIndex, string[] pattern, int patternIndex)
	{
		while (patternIndex < pattern.Length && pattern[patternIndex] == "**")
		{
			patternIndex++;
		}

		if (patternIndex == pattern.Length)
		{
			return true;
		}

		for (int skip = textIndex; skip <= text.Length; skip++)
		{
			if (MatchSegments(text, skip, pattern, patternIndex))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	///     Every named type in an assembly, walking nested types and child namespaces, so a <c>[Scan]</c> can
	///     consider every concrete class the container's assembly declares.
	/// </summary>
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
	///     Drops every implementation from a <c>[Scan(SkipUnconstructable = true)]</c> that the container cannot
	///     construct, reporting AWT141 where the default keeps the AWT101 error, so an incidentally-matched helper
	///     type does not break the build. An implementation also registered explicitly, or by a scan without the
	///     opt-in, is never dropped. Iterates to a fixpoint, since dropping one match can orphan another.
	/// </summary>
	private static void PruneUnconstructableScanMatches(
		List<RawRegistration> raw,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		ExternalSurface external,
		HashSet<string> constraintRejected,
		List<DiagnosticInfo> diagnostics)
	{
		if (!raw.Any(registration => registration.ScanSkipsUnconstructable))
		{
			return;
		}

		// Iterate to a fixpoint: each round re-derives the shrinking satisfiable surface, because dropping one
		// match can orphan another that depended on one of its services.
		bool dropped = true;
		while (dropped)
		{
			dropped = PruneUnconstructableScanRound(raw, containerSymbol, compilation, external, constraintRejected, diagnostics);
		}
	}

	/// <summary>
	///     One prune pass over the opted-in scan matches: drops each implementation unconstructable against the
	///     round's satisfiable surface (every registered service, plus the variance candidates a Direct/Func
	///     parameter could be redirected to), reporting AWT141, and returns whether anything was dropped so the
	///     caller can iterate to a fixpoint. The surface is a snapshot from the round's start, so a drop
	///     mid-round can leave a stale verdict; the next round re-checks against the shrunk surface.
	/// </summary>
	private static bool PruneUnconstructableScanRound(
		List<RawRegistration> raw,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		ExternalSurface external,
		HashSet<string> constraintRejected,
		List<DiagnosticInfo> diagnostics)
	{
		// The satisfiable surface as of this round: every registered (service, key), plus the variance candidates
		// a Direct/Func parameter could be redirected to. An implementation with any registration that did not
		// opt in (an explicit one, or a scan without SkipUnconstructable) is pinned to error semantics.
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

		ScanSatisfiability surface = new(services, constraintRejected, external, new VarianceState(varianceCandidates, compilation));

		// Check each opted-in implementation once (its registrations share symbol and location).
		bool dropped = false;
		HashSet<string> checkedImpls = new(StringComparer.Ordinal);
		foreach (RawRegistration registration in raw.Where(r => r.ScanSkipsUnconstructable).ToList())
		{
			if (pinnedImpls.Contains(registration.ImplementationType) || !checkedImpls.Add(registration.ImplementationType))
			{
				continue;
			}

			string? reason = UnconstructableScanMatchReason(registration, containerSymbol, compilation, surface);
			if (reason is null)
			{
				continue;
			}

			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanMatchSkipped,
				LocationInfo.From(registration.Location),
				new EquatableArray<string>([DisplayInstance(registration.ImplementationType), reason,])));
			raw.RemoveAll(r => r.IsScan && r.ImplementationType == registration.ImplementationType);

			// The dropped match no longer registers under anything, so its queued ambiguity warning (AWT187)
			// would contradict the AWT141 just reported. The other per-match warnings cannot co-occur with a
			// drop: they only fire when the match produced no registration to prune.
			diagnostics.RemoveAll(diagnostic => diagnostic.Descriptor == Diagnostics.ScanAmbiguousMatchingInterfaces
			                                    && diagnostic.MessageArgs.AsArray()[0] == Display(registration.ImplementationType));
			dropped = true;
		}

		return dropped;
	}

	/// <summary>
	///     The round's satisfiable surface, threaded through the unconstructable-match checks as one value: every
	///     registered <c>(service, key)</c>, the constraint-rejected services, the container's external-resolution
	///     surface, and the variance candidates a Direct/Func parameter could be redirected to.
	/// </summary>
	private readonly record struct ScanSatisfiability(
		HashSet<ServiceKey> Services,
		HashSet<string> ConstraintRejected,
		ExternalSurface External,
		VarianceState Variance);

	/// <summary>
	///     The reason one opted-in scan match cannot be produced against the round's surface (an AWT141 fragment),
	///     or <see langword="null" /> when it can. A self-compiled module-scan match is produced by its generated
	///     module factory, not by a constructor this container can see (the implementation is internal to the
	///     module's assembly), so its satisfiability is the factory's parameters, which mirror that constructor's. A
	///     same-compilation module-scan match is constructed directly, but through the same greedy constructor pick
	///     the factory would mirror, so its prune asks about that constructor too.
	/// </summary>
	private static string? UnconstructableScanMatchReason(
		RawRegistration registration,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		ScanSatisfiability surface)
		=> registration is { Production: ProductionKind.Factory, Origin: not null, ProductionMember: not null, }
			? FirstUnsatisfiableFactoryReason(registration, surface)
			: FirstUnconstructableReason(registration.Implementation, containerSymbol, compilation, surface, registration.GreedyConstructor);

	/// <summary>
	///     The reason a scanned implementation cannot be constructed (an AWT141 fragment), or <see langword="null" />
	///     when every dependency is satisfiable. Mirrors the AWT101 checks in <see cref="BuildInstance" /> against
	///     the round's satisfiable surface (registered services, always-satisfiable kinds, <c>[ImportServices]</c>
	///     fall-through, constraint-rejected services and variance redirection). A not-settable or <c>[Arg]</c>
	///     member is not a reason; it stays the targeted AWT136/AWT137 error.
	/// </summary>
	private static string? FirstUnconstructableReason(
		INamedTypeSymbol implementation,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		ScanSatisfiability surface,
		bool greedyConstructor = false)
	{
		IMethodSymbol? constructor = SelectConstructor(
			implementation, containerSymbol, compilation, surface.Services.Select(service => service.Service), surface.External,
			greedyConstructor ? _ => true : null);
		if (constructor is null)
		{
			return "it has no constructor accessible to the container";
		}

		if (FirstUnsatisfiableParameterReason(constructor, surface) is { } parameterReason)
		{
			return parameterReason;
		}

		foreach (IPropertySymbol property in InjectedProperties(implementation))
		{
			if (UnsatisfiableInjectedMemberReason(property, containerSymbol, compilation, surface.Services, surface.ConstraintRejected, surface.External.ServiceTypes) is { } reason)
			{
				return reason;
			}
		}

		return null;
	}

	/// <summary>
	///     The reason a self-compiled module-scan match cannot be produced (an AWT141 fragment), or
	///     <see langword="null" /> when every parameter of its generated module factory is satisfiable. The factory's
	///     parameters mirror the internal implementation's constructor, plain and unattributed (the module's own
	///     build rejected the rest, AWT200), so the check is the same per-parameter satisfiability a scanned
	///     constructor gets. A factory member missing from the module is not a prune concern; the emitted call would
	///     fail compilation with a targeted error anyway.
	/// </summary>
	private static string? FirstUnsatisfiableFactoryReason(RawRegistration registration, ScanSatisfiability surface)
	{
		IMethodSymbol? factory = registration.Origin!.GetMembers(registration.ProductionMember!)
			.OfType<IMethodSymbol>()
			.FirstOrDefault();
		return factory is null
			? null
			: FirstUnsatisfiableParameterReason(factory, surface);
	}

	/// <summary>
	///     The reason one of a production method's parameters cannot be satisfied from the round's surface (an
	///     AWT141 fragment), or <see langword="null" /> when all are. Shared by the constructor check
	///     (<see cref="FirstUnconstructableReason" />) and the generated-module-factory check
	///     (<see cref="FirstUnsatisfiableFactoryReason" />), whose parameters resolve identically.
	/// </summary>
	private static string? FirstUnsatisfiableParameterReason(IMethodSymbol method, ScanSatisfiability surface)
	{
		foreach (IParameterSymbol parameter in method.Parameters)
		{
			ParameterModel model = ClassifyParameter(parameter, asyncFactory: false, surface.External.ServiceTypes);
			bool satisfiable =
				model.Kind is DependencyKind.Arg or DependencyKind.External
				|| IsSynthesizedCollection(model.Kind)
				|| (surface.External.ImportServices && model is { Kind: DependencyKind.Direct, Key: null, })
				|| surface.Services.Contains(KeyOf(model))
				|| surface.ConstraintRejected.Contains(model.ServiceType)
				|| IsVarianceSatisfiable(model, parameter.Type, surface.Variance);
			if (!satisfiable)
			{
				return $"it requires '{DisplayKeyed(model.ServiceType, model.Key)}', which is not registered";
			}
		}

		return null;
	}

	/// <summary>
	///     The reason a scanned implementation's <c>[Inject]</c> member cannot be satisfied (an AWT141 fragment), or
	///     <see langword="null" /> otherwise. An injected member resolves from the graph like a Direct parameter (no
	///     external fall-through or variance redirect, though an <c>[ImportService&lt;T&gt;]</c> member does resolve
	///     externally). A not-settable, <c>[Arg]</c>-marked, external, optional or collection member is never a reason.
	/// </summary>
	private static string? UnsatisfiableInjectedMemberReason(
		IPropertySymbol property,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		HashSet<ServiceKey> services,
		HashSet<string> constraintRejected,
		HashSet<string> externalServiceTypes)
	{
		ParameterModel member = ClassifyDependency(property.Type, property.GetAttributes(), asyncFactory: false, location: null, externalServiceTypes);
		if (property.SetMethod is not { } setter || !IsAccessibleSetter(setter, containerSymbol, compilation)
		    || member.Kind is DependencyKind.Arg or DependencyKind.External || IsInjectOptional(property.GetAttributes()))
		{
			return null;
		}

		if (!IsSynthesizedCollection(member.Kind)
		    && !services.Contains(KeyOf(member))
		    && !constraintRejected.Contains(member.ServiceType))
		{
			return $"its injected member '{property.Name}' requires '{DisplayKeyed(member.ServiceType, member.Key)}', which is not registered";
		}

		return null;
	}

	/// <summary>
	///     Whether a Direct/Func dependency with no exact registration would be satisfied by variance redirection
	///     (mirroring <c>RedirectVariance</c>): the declared type unwraps to the classified service and a
	///     variance-compatible registration exists.
	/// </summary>
	private static bool IsVarianceSatisfiable(ParameterModel model, ITypeSymbol declaredType, VarianceState variance)
		=> model.Key is null
		   && model.Kind is DependencyKind.Direct or DependencyKind.Func
		   && UnderlyingServiceType(declaredType) is { } requested
		   && requested.ToDisplayString(FullyQualified) == model.ServiceType
		   && FindVarianceMatch(requested, model.ServiceType, variance) is not null;
}
