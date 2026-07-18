using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
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
	///     (AWT138/AWT140/AWT143/AWT172/AWT173/AWT174/AWT183/AWT184/AWT185), and adds AWT195/AWT196/AWT197/AWT200
	///     for the self-compilation constraints (accessible parameters, a single accessible exposure per match, no
	///     injection metadata the factory could not mirror).
	/// </summary>
	private static List<ModuleFactory> CollectModuleScanFactories(
		INamedTypeSymbol module,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		List<ModuleFactory> factories = new();

		foreach (AttributeData attribute in module.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "ScanAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
			{
				continue;
			}

			if (ScanMarker(attribute, attributeClass) is { } marker)
			{
				ExpandModuleScan(attribute, marker, module, compilation, factories, diagnostics);
			}
			else if (IsMarkerlessScan(attribute, attributeClass))
			{
				ExpandModuleScan(attribute, null, module, compilation, factories, diagnostics);
			}
		}

		return factories;
	}

	/// <summary>
	///     Expands one module <c>[Scan]</c> over its candidates, mirroring <see cref="ExpandScan" /> but emitting a
	///     generated factory per match instead of a <see cref="RawRegistration" />. A module scans its own assembly
	///     (or the assemblies named by <c>InAssembliesOf</c>), so an <c>internal</c> match is legitimate.
	/// </summary>
	private static void ExpandModuleScan(
		AttributeData attribute,
		INamedTypeSymbol? marker,
		INamedTypeSymbol module,
		Compilation compilation,
		List<ModuleFactory> factories,
		List<DiagnosticInfo> diagnostics)
	{
		Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

		List<IAssemblySymbol>? assemblies = ScanAssemblies(attribute);
		if (assemblies is { Count: 0, })
		{
			diagnostics.Add(new DiagnosticInfo(Diagnostics.ScanAssembliesEmpty, LocationInfo.From(location), new EquatableArray<string>([])));
			return;
		}

		ScanMatch match = new(ScanExposureOf(attribute), ScanLifetime(attribute), location, ScanSkipsUnconstructable(attribute));
		ScanFilters filters = ScanFiltersOf(attribute);

		if ((match.Exposure & ScanExposures.All) == 0)
		{
			diagnostics.Add(new DiagnosticInfo(Diagnostics.ScanExposesNothing, LocationInfo.From(location), new EquatableArray<string>([])));
			return;
		}

		if (marker is null && MarkerlessScanError(match.Exposure, filters, assemblies) is { } reason)
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
		foreach (ScanCandidate candidate in ScanCandidates(assemblies, compilation, marker, location, diagnostics))
		{
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

		ReportScanFilterDiagnostics(filters, hits, new ScanFilterCounts(assignable, registered, produced), assemblies, markerDisplay, location, diagnostics);
	}

	/// <summary>
	///     Emits the factory for one module-scan match, or reports why it cannot: the exposure must resolve to a
	///     single interface a consumer in another assembly can name (AWT196 when none, AWT197 when several, since a
	///     factory returns one accessible type and a shared instance across interfaces cannot be expressed), and the
	///     constructor's parameters must all be accessible outside the assembly (AWT195), because they appear on the
	///     generated <c>public</c> factory and are resolved from the consumer's graph. A match carrying injection
	///     metadata the factory cannot mirror ([Inject] properties, [Inject]/[Arg] parameters) is AWT200, and a
	///     match another scan of this module already exposed the same way is deduped silently (a differing lifetime
	///     across the overlap is AWT142, first scan wins). Returns the number of factories contributed (0 or 1).
	/// </summary>
	private static int RegisterModuleScanMatch(
		INamedTypeSymbol type,
		ScanMatch match,
		INamedTypeSymbol? marker,
		bool openMarker,
		INamedTypeSymbol? markerDefinition,
		INamedTypeSymbol module,
		Compilation compilation,
		List<ModuleFactory> factories,
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

		// Two [Scan]s on one module can match the same type under the same exposure. A container dedups such
		// overlaps to one collection member (per-implementation identity); factories are per-match identities on
		// the consumer, so dedup here instead, keeping the first. A differing lifetime across the overlapping
		// scans is AWT142, mirroring the container, and the first scan's lifetime wins consistently.
		ModuleFactory? overlapping = factories.Find(factory => factory.ImplementationType == typeName);
		Lifetime lifetime = overlapping?.Lifetime ?? match.Lifetime;
		if (overlapping is not null)
		{
			if (overlapping.Lifetime != match.Lifetime)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ScanLifetimeConflict,
					LocationInfo.From(match.Location),
					new EquatableArray<string>([
						Display(typeName),
						overlapping.Lifetime.ToString(),
						match.Lifetime.ToString(),
					])));
			}

			if (factories.Exists(factory => factory.ImplementationType == typeName && factory.ServiceType == serviceName))
			{
				return 0;
			}
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

			parameters.Add(new FactoryParameter(parameter.Type.ToDisplayString(FullyQualified), parameter.Name));
		}

		factories.Add(new ModuleFactory(
			ModuleFactoryName(type, factories.Count),
			serviceName,
			typeName,
			lifetime,
			new EquatableArray<FactoryParameter>(parameters.ToArray())));
		return 1;
	}

	/// <summary>
	///     A deterministic, unique factory method name for one match: prefixed to avoid clashing with the module's
	///     own members, suffixed with the running index so two matches of the same simple name never collide (which
	///     would surface as an ambiguous factory on the consumer).
	/// </summary>
	private static string ModuleFactoryName(INamedTypeSymbol type, int index)
		=> $"Awaiten__Scan_{index}_{type.Name}";

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
