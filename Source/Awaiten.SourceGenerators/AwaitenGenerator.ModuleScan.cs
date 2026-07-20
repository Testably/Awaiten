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

			produced += RegisterModuleScanMatch(candidate.Type, match, marker, openMarker, markerDefinition, module, compilation, factories, diagnostics);
		}

		ReportScanFilterDiagnostics(filters, hits, new ScanFilterCounts(assignable, registered, produced), assemblies: null, markerDisplay, location, diagnostics);
	}

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
		INamedTypeSymbol? marker,
		bool openMarker,
		INamedTypeSymbol? markerDefinition,
		INamedTypeSymbol module,
		Compilation compilation,
		List<ModuleScanExpansion> factories,
		List<DiagnosticInfo> diagnostics)
	{
		string typeName = type.ToDisplayString(FullyQualified);

		// The interfaces this match would expose, restricted to those a consumer in any assembly can name. Self
		// exposes the concrete type, externally usable only when the type itself is public.
		List<INamedTypeSymbol> exposures = new();
		if (match.RegisterSelf && IsExternallyAccessible(type))
		{
			exposures.Add(type);
		}

		if (match.RegisterMarker && marker is not null)
		{
			exposures.AddRange(
				(openMarker ? ClosedMarkerInterfaces(type, markerDefinition!) : MarkerInterfaces(type, marker, compilation))
				.Where(IsExternallyAccessible));
		}

		if (match.RegisterMatchingInterface)
		{
			exposures.AddRange(MatchingInterfaces(type).Where(IsExternallyAccessible));
		}

		// A combined Marker | MatchingInterface can select the same interface twice; dedup by name.
		List<INamedTypeSymbol> distinct = new();
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (INamedTypeSymbol exposure in exposures)
		{
			if (seen.Add(exposure.ToDisplayString(FullyQualified)))
			{
				distinct.Add(exposure);
			}
		}

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

		// Two [Scan]s on one module can match the same type. Under the same exposure the overlap is deduped to
		// the first factory, mirroring the container's per-implementation dedup, with AWT142 when the lifetimes
		// differ (first scan's lifetime wins consistently). Under different exposures it is AWT197: each factory
		// constructs its own instance, so the single shared instance a container scan gives one implementation
		// across several interfaces cannot be expressed, and emitting both would silently split it.
		ModuleScanExpansion? overlapping = factories.Find(expansion => expansion.Factory.ImplementationType == typeName);
		if (overlapping is not null)
		{
			if (overlapping.Factory.ServiceType != serviceName)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ModuleScanMultipleExposures,
					LocationInfo.From(match.Location),
					new EquatableArray<string>([
						Display(typeName),
						$"{Display(overlapping.Factory.ServiceType)}, {Display(serviceName)}",
					])));
				return 0;
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

			return 0;
		}

		// [Inject] properties and [Inject]/[Arg] constructor parameters carry per-dependency semantics (keys,
		// optionality, deferral, resolution arguments) that a mirrored factory signature cannot express, so the
		// consumer would silently construct the match differently than a container scan would (AWT200).
		foreach (IPropertySymbol property in InjectedProperties(type))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ModuleScanInjectionMetadata,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([Display(typeName), $"its property '{property.Name}' is marked [Inject]",])));
			return 0;
		}

		// The constructor the generated factory calls. Accessibility is asked against the module (it emits the
		// 'new'), so an internal constructor in the module's own assembly qualifies. A greediest-fallback keeps a
		// type with no satisfiable-here constructor building (its parameters are resolved by the consumer, not us).
		IMethodSymbol? constructor = SelectConstructor(
			type, module, compilation, Array.Empty<string>(), new ExternalSurface(false, new HashSet<string>(StringComparer.Ordinal)), _ => true);
		if (constructor is null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NoAccessibleConstructor,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([Display(typeName),])));
			return 0;
		}

		List<FactoryParameter> parameters = new();
		foreach (IParameterSymbol parameter in constructor.Parameters)
		{
			// AWT200 for parameters, same reasoning as the [Inject] property check above.
			ImmutableArray<AttributeData> parameterAttributes = parameter.GetAttributes();
			if (HasInject(parameterAttributes) || HasArgAttribute(parameterAttributes))
			{
				string attributeName = HasInject(parameterAttributes) ? "[Inject]" : "[Arg]";
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ModuleScanInjectionMetadata,
					LocationInfo.From(match.Location),
					new EquatableArray<string>([Display(typeName), $"its constructor parameter '{parameter.Name}' is marked {attributeName}",])));
				return 0;
			}

			// The parameter type appears on the public factory signature and is resolved from the consumer's graph,
			// so it must be nameable outside the assembly (AWT195). This is the v1 limitation on self-compiled scans.
			if (!IsExternallyAccessible(parameter.Type))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ModuleScanParameterInaccessible,
					LocationInfo.From(match.Location),
					new EquatableArray<string>([Display(typeName), Display(parameter.Type.ToDisplayString(FullyQualified)),])));
				return 0;
			}

			parameters.Add(new FactoryParameter(parameter.Type.ToDisplayString(FullyQualifiedWithNullability), parameter.Name));
		}

		factories.Add(new ModuleScanExpansion(
			new ModuleFactory(
				ModuleFactoryName(type),
				serviceName,
				typeName,
				match.Lifetime,
				match.SkipUnconstructable,
				new EquatableArray<FactoryParameter>(parameters.ToArray())),
			service,
			type,
			match.Location));
		return 1;
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
