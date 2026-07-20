using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     <see cref="FullyQualified" /> plus the nullable reference type modifier, used for a generated module-scan
	///     factory's parameter types. The factory is a <c>public</c> cross-assembly member, so its signature must
	///     mirror the scanned constructor exactly; without the modifier a <c>IClock?</c> parameter would be emitted
	///     as a non-nullable <c>IClock</c>, silently widening a nullable dependency the author declared.
	/// </summary>
	private static readonly SymbolDisplayFormat FullyQualifiedWithNullability =
		SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
			SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
			| SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

	/// <summary>
	///     Expands every <c>[Scan]</c> declared on a <c>[Module]</c> into the factories the module self-compiles:
	///     one <c>public static</c> method per match that constructs the (possibly <c>internal</c>) implementation
	///     and returns its accessible exposure interface. The emitter pairs each with a
	///     <c>[GeneratedScanRegistration&lt;IExposure&gt;(factory, Lifetime = …)]</c>, which a consuming container
	///     reads like a container <c>[Scan]</c> match - collection-eligible (several matches under one interface
	///     resolve as an <c>IEnumerable</c>) and overridable by an explicit registration. Runs in the
	///     <em>module's own</em> build, so diagnostics land at the library's source locations, and the scan sees the
	///     library's <c>internal</c> types (the whole point: keep implementations internal, expose interfaces).
	///     Reuses the container scan's candidate discovery, filters and their diagnostics
	///     (AWT138/AWT143/AWT172/AWT173/AWT174/AWT183/AWT184/AWT185), and adds AWT195/AWT196/AWT197/AWT200/AWT202
	///     for the self-compilation constraints (accessible parameters, a single accessible exposure per match, no
	///     injection metadata the factory could not mirror, the module's own assembly only).
	/// </summary>
	private static List<ModuleScanExpansion> CollectModuleScanFactories(
		INamedTypeSymbol module,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics,
		CancellationToken cancellationToken)
	{
		List<ModuleScanExpansion> factories = new();

		foreach (AttributeData attribute in module.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "ScanAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
			{
				continue;
			}

			if (ScanMarker(attribute, attributeClass) is { } marker)
			{
				ExpandModuleScan(attribute, marker, module, compilation, factories, diagnostics, cancellationToken);
			}
			else if (IsMarkerlessScan(attribute, attributeClass))
			{
				ExpandModuleScan(attribute, null, module, compilation, factories, diagnostics, cancellationToken);
			}
		}

		return factories;
	}

	/// <summary>
	///     One expanded module-scan match: the equatable <see cref="ModuleFactory" /> the module's partial emits,
	///     plus the live symbols behind it, which the model never carries (they would break incremental caching)
	///     but a same-compilation container needs to register the match directly (see <c>Collect</c>).
	/// </summary>
	private sealed record ModuleScanExpansion(
		ModuleFactory Factory,
		INamedTypeSymbol Service,
		INamedTypeSymbol Implementation,
		Location? Location);

	/// <summary>
	///     Expands one module <c>[Scan]</c> over its candidates, mirroring <see cref="ExpandScan" /> but emitting a
	///     generated factory per match instead of a <see cref="RawRegistration" />. A module scans its own assembly
	///     only (AWT202 rejects <c>InAssembliesOf</c>), so an <c>internal</c> match is legitimate.
	/// </summary>
	private static void ExpandModuleScan(
		AttributeData attribute,
		INamedTypeSymbol? marker,
		INamedTypeSymbol module,
		Compilation compilation,
		List<ModuleScanExpansion> factories,
		List<DiagnosticInfo> diagnostics,
		CancellationToken cancellationToken)
	{
		Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

		// v1: a module scans its own assembly only. An InAssembliesOf sweep from a module would see just the
		// target's public types, which a container [Scan] already covers, so it is rejected (AWT202) rather
		// than silently doing less than the container form.
		if (ScanAssemblies(attribute) is not null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ModuleScanForeignAssemblies,
				LocationInfo.From(location),
				new EquatableArray<string>([Display(module.ToDisplayString(FullyQualified)),])));
			return;
		}

		ScanMatch match = new(ScanExposureOf(attribute), ScanLifetime(attribute), location, ScanSkipsUnconstructable(attribute));
		ScanFilters filters = ScanFiltersOf(attribute);

		if ((match.Exposure & ScanExposures.All) == 0)
		{
			diagnostics.Add(new DiagnosticInfo(Diagnostics.ScanExposesNothing, LocationInfo.From(location), new EquatableArray<string>([])));
			return;
		}

		if (marker is null && MarkerlessScanError(match.Exposure, filters, assemblies: null) is { } reason)
		{
			diagnostics.Add(new DiagnosticInfo(Diagnostics.MarkerlessScanInvalid, LocationInfo.From(location), new EquatableArray<string>([reason,])));
			return;
		}

		bool openMarker = marker is not null && IsOpenGenericMarker(marker);
		INamedTypeSymbol? markerDefinition = marker?.OriginalDefinition;
		ScanMarkerInfo markerInfo = new(marker, openMarker, markerDefinition);

		string? markerDisplay = null;
		if (marker is not null)
		{
			markerDisplay = (openMarker ? markerDefinition! : marker).ToDisplayString(FullyQualified);
		}

		ScanFilterHits hits = new();
		int assignable = 0;
		int registered = 0;
		int produced = 0;
		foreach (ScanCandidate candidate in ScanCandidates(assemblies: null, compilation, marker, location, diagnostics))
		{
			cancellationToken.ThrowIfCancellationRequested();
			assignable++;
			if (!PassesScanFilters(candidate.Type, filters, hits))
			{
				continue;
			}

			registered++;

			// A private/protected nested type the module cannot name is skipped silently: unlike a container scan
			// (AWT193), a module scans its own assembly, so an internal implementation is not inaccessible here and
			// is the expected match. Only a truly unnameable candidate reaches this, which no author asked for.
			if (candidate.Inaccessible)
			{
				continue;
			}

			produced += RegisterModuleScanMatch(candidate.Type, match, markerInfo, module, compilation, factories, diagnostics);
		}

		ReportScanFilterDiagnostics(filters, hits, new ScanFilterCounts(assignable, registered, produced), assemblies: null, markerDisplay, location, diagnostics);
	}

	/// <summary>
	///     The marker a module <c>[Scan]</c> selects on, plus whether it is an open generic and its original
	///     definition, bundled so the per-match exposure computation takes one value rather than three.
	/// </summary>
	private readonly record struct ScanMarkerInfo(INamedTypeSymbol? Marker, bool Open, INamedTypeSymbol? Definition);

	/// <summary>
	///     Emits the factory for one module-scan match, or reports why it cannot: the exposure must resolve to a
	///     single interface a consumer in another assembly can name (AWT196 when none, AWT197 when several, since a
	///     factory returns one accessible type and a shared instance across interfaces cannot be expressed), and the
	///     constructor's parameters must all be accessible outside the assembly (AWT195), because they appear on the
	///     generated <c>public</c> factory and are resolved from the consumer's graph. A match carrying injection
	///     metadata the factory cannot mirror ([Inject] properties, [Inject]/[Arg] parameters) is AWT200. A match
	///     another scan of this module already exposed the same way is deduped silently (a differing lifetime
	///     across the overlap is AWT142, first scan wins); one another scan exposed under a <em>different</em>
	///     interface is AWT197, since two factories would split the shared instance. Returns the number of
	///     factories contributed (0 or 1).
	/// </summary>
	private static int RegisterModuleScanMatch(
		INamedTypeSymbol type,
		ScanMatch match,
		ScanMarkerInfo markerInfo,
		INamedTypeSymbol module,
		Compilation compilation,
		List<ModuleScanExpansion> factories,
		List<DiagnosticInfo> diagnostics)
	{
		string typeName = type.ToDisplayString(FullyQualified);

		List<INamedTypeSymbol> distinct = AccessibleExposures(type, match, markerInfo, compilation);

		if (distinct.Count == 0)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ModuleScanNoAccessibleExposure,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([Display(typeName),])));
			return 0;
		}

		if (distinct.Count > 1)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ModuleScanMultipleExposures,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([
					Display(typeName),
					string.Join(", ", distinct.Select(exposure => Display(exposure.ToDisplayString(FullyQualified)))),
				])));
			return 0;
		}

		INamedTypeSymbol service = distinct[0];
		string serviceName = service.ToDisplayString(FullyQualified);

		if (OverlapResolved(typeName, serviceName, match, factories, diagnostics))
		{
			return 0;
		}

		if (!TryMirrorFactoryParameters(type, module, compilation, match, typeName, diagnostics, out EquatableArray<FactoryParameter> parameters))
		{
			return 0;
		}

		factories.Add(new ModuleScanExpansion(
			new ModuleFactory(
				ModuleFactoryName(type),
				serviceName,
				typeName,
				match.Lifetime,
				match.SkipUnconstructable,
				parameters),
			service,
			type,
			match.Location));
		return 1;
	}

	/// <summary>
	///     The interfaces one module-scan match would expose, restricted to those a consumer in any assembly can
	///     name and deduped by name. <c>Self</c> exposes the concrete type (externally usable only when it is
	///     itself public); <c>Marker</c> and <c>MatchingInterface</c> contribute their accessible interfaces. A
	///     combined <c>Marker | MatchingInterface</c> can select the same interface twice, hence the dedup.
	/// </summary>
	private static List<INamedTypeSymbol> AccessibleExposures(
		INamedTypeSymbol type,
		ScanMatch match,
		ScanMarkerInfo markerInfo,
		Compilation compilation)
	{
		List<INamedTypeSymbol> exposures = new();
		if (match.RegisterSelf && IsExternallyAccessible(type))
		{
			exposures.Add(type);
		}

		if (match.RegisterMarker && markerInfo.Marker is not null)
		{
			exposures.AddRange(
				(markerInfo.Open
					? ClosedMarkerInterfaces(type, markerInfo.Definition!)
					: MarkerInterfaces(type, markerInfo.Marker, compilation))
				.Where(IsExternallyAccessible));
		}

		if (match.RegisterMatchingInterface)
		{
			exposures.AddRange(MatchingInterfaces(type).Where(IsExternallyAccessible));
		}

		HashSet<string> seen = new(StringComparer.Ordinal);
		return exposures.Where(exposure => seen.Add(exposure.ToDisplayString(FullyQualified))).ToList();
	}

	/// <summary>
	///     Whether an earlier scan of this module already matched <paramref name="typeName" />, in which case this
	///     match adds no new factory. Two <c>[Scan]</c>s on one module can match the same type: under the same
	///     exposure the overlap is deduped silently to the first factory (AWT142 when the lifetimes differ, the
	///     first scan's lifetime winning consistently, mirroring the container's per-implementation dedup); under a
	///     different exposure it is AWT197, because each factory constructs its own instance, so the single shared
	///     instance a container scan gives one implementation across interfaces cannot be expressed and emitting
	///     both would silently split it. Returns <see langword="true" /> when the overlap is handled here.
	/// </summary>
	private static bool OverlapResolved(
		string typeName,
		string serviceName,
		ScanMatch match,
		List<ModuleScanExpansion> factories,
		List<DiagnosticInfo> diagnostics)
	{
		ModuleScanExpansion? overlapping = factories.Find(expansion => expansion.Factory.ImplementationType == typeName);
		if (overlapping is null)
		{
			return false;
		}

		if (overlapping.Factory.ServiceType != serviceName)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ModuleScanMultipleExposures,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([
					Display(typeName),
					$"{Display(overlapping.Factory.ServiceType)}, {Display(serviceName)}",
				])));
			return true;
		}

		if (overlapping.Factory.Lifetime != match.Lifetime)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanLifetimeConflict,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([
					Display(typeName),
					overlapping.Factory.Lifetime.ToString(),
					match.Lifetime.ToString(),
				])));
		}

		return true;
	}

	/// <summary>
	///     Builds the generated factory's parameter list by mirroring the greediest accessible constructor of
	///     <paramref name="type" />, or reports why it cannot and returns <see langword="false" />. Accessibility is
	///     asked against the module (it emits the <c>new</c>), so an internal constructor in the module's own
	///     assembly qualifies, and a greediest-fallback keeps a type with no satisfiable-here constructor building
	///     (its parameters are resolved by the consumer, not us). An <c>[Inject]</c> property or an <c>[Inject]</c>/
	///     <c>[Arg]</c> parameter carries per-dependency semantics (keys, optionality, deferral, resolution
	///     arguments) a mirrored factory signature cannot express, so it is AWT200 rather than silently dropped. A
	///     parameter whose type is not nameable outside the assembly is AWT195, the v1 limitation, because it
	///     appears on the <c>public</c> factory signature and is resolved from the consumer's graph.
	/// </summary>
	private static bool TryMirrorFactoryParameters(
		INamedTypeSymbol type,
		INamedTypeSymbol module,
		Compilation compilation,
		ScanMatch match,
		string typeName,
		List<DiagnosticInfo> diagnostics,
		out EquatableArray<FactoryParameter> parameters)
	{
		parameters = default;

		IPropertySymbol? injectedProperty = InjectedProperties(type).FirstOrDefault();
		if (injectedProperty is not null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ModuleScanInjectionMetadata,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([Display(typeName), $"its property '{injectedProperty.Name}' is marked [Inject]",])));
			return false;
		}

		IMethodSymbol? constructor = SelectConstructor(
			type, module, compilation, Array.Empty<string>(), new ExternalSurface(false, new HashSet<string>(StringComparer.Ordinal)), _ => true);
		if (constructor is null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NoAccessibleConstructor,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([Display(typeName),])));
			return false;
		}

		List<FactoryParameter> mirrored = new();
		foreach (IParameterSymbol parameter in constructor.Parameters)
		{
			ImmutableArray<AttributeData> parameterAttributes = parameter.GetAttributes();
			if (HasInject(parameterAttributes) || HasArgAttribute(parameterAttributes))
			{
				string attributeName = HasInject(parameterAttributes) ? "[Inject]" : "[Arg]";
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ModuleScanInjectionMetadata,
					LocationInfo.From(match.Location),
					new EquatableArray<string>([Display(typeName), $"its constructor parameter '{parameter.Name}' is marked {attributeName}",])));
				return false;
			}

			if (!IsExternallyAccessible(parameter.Type))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ModuleScanParameterInaccessible,
					LocationInfo.From(match.Location),
					new EquatableArray<string>([Display(typeName), Display(parameter.Type.ToDisplayString(FullyQualified)),])));
				return false;
			}

			mirrored.Add(new FactoryParameter(parameter.Type.ToDisplayString(FullyQualifiedWithNullability), parameter.Name));
		}

		parameters = new EquatableArray<FactoryParameter>(mirrored.ToArray());
		return true;
	}

	/// <summary>
	///     A deterministic factory method name for one match, stable across library versions: prefixed to avoid
	///     clashing with the module's own members, carrying the match's simple name for readability, and suffixed
	///     with an FNV-1a hash of its fully-qualified name so two same-named matches in different namespaces stay
	///     distinct. The name depends only on the match's own full name — never on scan or discovery order — so
	///     adding or removing other matches in a later library version does not rename it, and a consumer compiled
	///     against the older assembly still binds after a drop-in upgrade.
	/// </summary>
	private static string ModuleFactoryName(INamedTypeSymbol type)
	{
		uint hash = 2166136261;
		foreach (char character in type.ToDisplayString(FullyQualified))
		{
			hash = unchecked((hash ^ character) * 16777619);
		}

		return $"Awaiten__Scan_{type.Name}_{hash:x8}";
	}

	/// <summary>
	///     Whether a type can be named from an unrelated assembly: it (and every enclosing type) is <c>public</c>,
	///     and any generic type arguments are themselves externally accessible. An array is accessible when its
	///     element type is. Used to gate a self-compiled scan's exposure interface and its factory's parameter types,
	///     which appear on a <c>public</c> generated signature.
	/// </summary>
	private static bool IsExternallyAccessible(ITypeSymbol type)
	{
		switch (type)
		{
			case IArrayTypeSymbol array:
				return IsExternallyAccessible(array.ElementType);
			case ITypeParameterSymbol:
				return true;
			case INamedTypeSymbol named:
				for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
				{
					if (current.DeclaredAccessibility != Accessibility.Public)
					{
						return false;
					}
				}

				return named.TypeArguments.All(IsExternallyAccessible);
			default:
				return false;
		}
	}
}
