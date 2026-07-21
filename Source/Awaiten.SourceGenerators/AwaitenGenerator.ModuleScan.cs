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
		ModuleScanContext context = new(module, compilation, diagnostics, new HashSet<string>(StringComparer.Ordinal));

		foreach (AttributeData attribute in module.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "ScanAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
			{
				continue;
			}

			if (ScanMarker(attribute, attributeClass) is { } marker)
			{
				ExpandModuleScan(attribute, marker, context, factories, cancellationToken);
			}
			else if (IsMarkerlessScan(attribute, attributeClass))
			{
				ExpandModuleScan(attribute, null, context, factories, cancellationToken);
			}
		}

		return factories;
	}

	/// <summary>
	///     One expanded module-scan match: the equatable <see cref="ModuleFactory" /> the module's partial emits,
	///     plus the live symbols behind it, which the model never carries (they would break incremental caching)
	///     but a same-compilation container needs to register the match directly (see <c>Collect</c>). The hook
	///     fields carry what a same-compilation container needs to wire the match's lifecycle hooks directly against
	///     the module (no assembly boundary, so it binds the module's own - possibly internal - hook rather than the
	///     generated wrapper it cannot see): the user hook names and, per slot, the closed marker forms contributed
	///     for it - kept apart because two overlapping scans can fill the two slots with differently-markered hooks
	///     (see <see cref="ResolveOverlap" />). A slot's name and markers are kept even when its hook failed to
	///     resolve (the wrapper on the factory is what marks a valid hook), so a later overlapping scan merges
	///     against the claimed slot - a different name still conflicts, and a restated name re-resolves over the
	///     accumulated union - rather than re-claiming it fresh from a narrower marker set; a consumer wires a hook
	///     only when its wrapper exists (see <c>Collect</c>).
	/// </summary>
	private sealed record ModuleScanExpansion(
		ModuleFactory Factory,
		INamedTypeSymbol Service,
		INamedTypeSymbol Implementation,
		Location? Location,
		string? OnActivated = null,
		string? OnRelease = null,
		IReadOnlyList<INamedTypeSymbol>? OnActivatedMarkers = null,
		IReadOnlyList<INamedTypeSymbol>? OnReleaseMarkers = null);

	/// <summary>
	///     The per-module state the scan expansion shares (the module that owns the hooks, the compilation, and the
	///     diagnostics sink), bundled so each helper takes one value rather than three. <see cref="FailedHooks" />
	///     records every hook attempt that failed - as <c>match\0slot\0name</c>, across all of the module's scans -
	///     so an overlapping scan re-naming the same failed hook re-resolves it (the slot merge needs the outcome)
	///     without repeating the first scan's diagnostic (see <see cref="ResolveModuleHook" />).
	/// </summary>
	private readonly record struct ModuleScanContext(
		INamedTypeSymbol Module,
		Compilation Compilation,
		List<DiagnosticInfo> Diagnostics,
		HashSet<string> FailedHooks);

	/// <summary>
	///     Expands one module <c>[Scan]</c> over its candidates, mirroring <see cref="ExpandScan" /> but emitting a
	///     generated factory per match instead of a <see cref="RawRegistration" />. A module scans its own assembly
	///     only (AWT202 rejects <c>InAssembliesOf</c>), so an <c>internal</c> match is legitimate.
	/// </summary>
	private static void ExpandModuleScan(
		AttributeData attribute,
		INamedTypeSymbol? marker,
		ModuleScanContext context,
		List<ModuleScanExpansion> factories,
		CancellationToken cancellationToken)
	{
		List<DiagnosticInfo> diagnostics = context.Diagnostics;
		Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

		// v1: a module scans its own assembly only. An InAssembliesOf sweep from a module would see just the
		// target's public types, which a container [Scan] already covers, so it is rejected (AWT202) rather
		// than silently doing less than the container form.
		if (ScanAssemblies(attribute) is not null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ModuleScanForeignAssemblies,
				LocationInfo.From(location),
				new EquatableArray<string>([Display(context.Module.ToDisplayString(FullyQualified)),])));
			return;
		}

		// A module [Scan]'s OnActivated/OnRelease hooks are resolved and validated in the module's own build (so
		// diagnostics land at the library source), then emitted as public wrappers a consumer runs through the
		// ordinary hook pipeline - see RegisterModuleScanMatch.
		ScanMatch match = new(ScanExposureOf(attribute), ScanLifetime(attribute), location, ScanSkipsUnconstructable(attribute),
			ScanHook(attribute, "OnActivated"), ScanHook(attribute, "OnRelease"));
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
		foreach (ScanCandidate candidate in ScanCandidates(assemblies: null, context.Compilation, marker, location, diagnostics))
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

			produced += RegisterModuleScanMatch(candidate.Type, match, markerInfo, context, factories);
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
	///     another scan of this module already exposed the same way is deduped to the first factory, merging the
	///     overlap's lifecycle hooks per slot like two container scans' (see <see cref="ResolveOverlap" />; a
	///     differing lifetime across the overlap is AWT142, first scan wins); one another scan exposed under a
	///     <em>different</em> interface is AWT197, since two factories would split the shared instance. Returns the
	///     number of factories contributed (0 or 1).
	/// </summary>
	private static int RegisterModuleScanMatch(
		INamedTypeSymbol type,
		ScanMatch match,
		ScanMarkerInfo markerInfo,
		ModuleScanContext context,
		List<ModuleScanExpansion> factories)
	{
		List<DiagnosticInfo> diagnostics = context.Diagnostics;
		string typeName = type.ToDisplayString(FullyQualified);

		List<INamedTypeSymbol> distinct = AccessibleExposures(type, match, markerInfo, context.Compilation);

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

		int overlap = factories.FindIndex(expansion => expansion.Factory.ImplementationType == typeName);
		if (overlap >= 0)
		{
			factories[overlap] = ResolveOverlap(factories[overlap], typeName, serviceName, type, match, markerInfo, context);
			return 0;
		}

		if (!TryMirrorFactoryParameters(type, context.Module, context.Compilation, match, typeName, diagnostics, out EquatableArray<FactoryParameter> parameters))
		{
			return 0;
		}

		// The scan's hooks, resolved and (for a generic hook) closed in the module's own build. Each becomes a
		// public wrapper the module emits beside the factory, so a cross-assembly consumer runs it without naming the
		// module's (possibly internal) hook; the closed marker forms let a same-compilation container bind that hook
		// directly. A hook named but not resolvable is reported here and dropped (no wrapper on the factory, unnamed
		// on the attribute); the expansion still records the claimed name and markers so overlapping scans merge
		// against the failed slot instead of re-claiming it (see MergeModuleScanHookSlot).
		IReadOnlyList<INamedTypeSymbol>? hookMarkers = ScanHookMarkers(type, match, markerInfo.Open, markerInfo.Definition);
		ResolvedModuleHook? onActivated = ResolveModuleHook(type, match, release: false, hookMarkers, context);
		ResolvedModuleHook? onRelease = ResolveModuleHook(type, match, release: true, hookMarkers, context);

		factories.Add(new ModuleScanExpansion(
			new ModuleFactory(
				ModuleFactoryName(type),
				serviceName,
				typeName,
				match.Lifetime,
				match.SkipUnconstructable,
				parameters,
				onActivated?.Wrapper,
				onRelease?.Wrapper),
			service,
			type,
			match.Location,
			match.OnActivated,
			match.OnRelease,
			match.OnActivated is null ? null : hookMarkers,
			match.OnRelease is null ? null : hookMarkers));
		return 1;
	}

	/// <summary>
	///     One module-scan hook resolved in the module's own build: the module's own <see cref="UserHookName" /> (for
	///     a same-compilation container to bind directly, no boundary in the way) and the <see cref="ModuleHook" />
	///     the module emits and a cross-assembly consumer runs.
	/// </summary>
	private sealed record ResolvedModuleHook(string UserHookName, ModuleHook Wrapper);

	/// <summary>
	///     Resolves and validates one <c>OnActivated</c>/<c>OnRelease</c> hook a module <c>[Scan]</c> named, in the
	///     module's own build, or reports why it cannot and returns <see langword="null" /> (the hook is then dropped:
	///     no wrapper, unnamed on the registration). Mirrors the container's <see cref="ResolveHook" /> against the
	///     module as owner: a hook is a <c>static void M(TMatch, …)</c> on the module (<c>internal</c> or wider, so a
	///     same-compilation container can bind it directly as a cross-assembly one binds the wrapper), and a generic hook binds
	///     its type arguments from the match's closed marker forms (exactly one - none is AWT164, several is AWT198).
	///     No usable method is <see cref="Diagnostics.InvalidLifecycleHook">AWT164</see>, more than one is
	///     <see cref="Diagnostics.AmbiguousLifecycleHook">AWT190</see>. The instance (first) parameter accepts the
	///     internal match itself; the parameters after it are mirrored onto the public wrapper and resolved from the
	///     consumer's graph, so they carry the same restrictions a container hook's do (AWT189/AWT191) plus the
	///     mirroring rules a factory parameter gets: externally accessible (AWT203), no per-dependency metadata the
	///     bare wrapper signature would drop (AWT204).
	/// </summary>
	private static ResolvedModuleHook? ResolveModuleHook(
		INamedTypeSymbol matchType,
		ScanMatch match,
		bool release,
		IReadOnlyList<INamedTypeSymbol>? closedMarkers,
		ModuleScanContext context)
	{
		string? hookName = release ? match.OnRelease : match.OnActivated;
		if (hookName is null)
		{
			return null;
		}

		// One failure report per (match, slot, name): an overlapping scan re-naming a hook that already failed
		// still resolves it (the slot merge needs the outcome) but into a discarded sink, so the first scan's
		// diagnostic is not repeated. A fresh failure - a new name, or a restated hook newly ambiguous over
		// widened markers - has no recorded attempt and reports normally.
		string attempt = matchType.ToDisplayString(FullyQualified) + "\0" + (release ? "OnRelease" : "OnActivated") + "\0" + hookName;
		ResolvedModuleHook? resolved = ResolveModuleHookCore(matchType, hookName, match.Location, release, closedMarkers,
			context.FailedHooks.Contains(attempt) ? context with { Diagnostics = new List<DiagnosticInfo>(), } : context);
		if (resolved is null)
		{
			context.FailedHooks.Add(attempt);
		}

		return resolved;
	}

	/// <summary>
	///     The resolution behind <see cref="ResolveModuleHook" />, reporting every failure into the context's sink;
	///     the wrapper dedupes repeated failures of one already-reported attempt across overlapping scans.
	/// </summary>
	private static ResolvedModuleHook? ResolveModuleHookCore(
		INamedTypeSymbol matchType,
		string hookName,
		Location? location,
		bool release,
		IReadOnlyList<INamedTypeSymbol>? closedMarkers,
		ModuleScanContext context)
	{
		string typeName = matchType.ToDisplayString(FullyQualified);
		(List<IMethodSymbol> matches, List<IReadOnlyList<INamedTypeSymbol>> ambiguous) =
			CollectModuleHookOverloads(matchType, hookName, closedMarkers, context);

		// AWT198: a generic hook whose only obstacle is an ambiguous closed marker, with no other usable overload -
		// its type arguments cannot be chosen. Mirrors ResolveHook.
		if (matches.Count == 0 && ambiguous.Count == 1)
		{
			context.Diagnostics.Add(new DiagnosticInfo(
				Diagnostics.GenericHookAmbiguousMarker,
				LocationInfo.From(location),
				new EquatableArray<string>([
					Display(typeName),
					hookName,
					string.Join(", ", ambiguous[0].Select(form => Display(form.ToDisplayString(FullyQualified)))),
				])));
			return null;
		}

		// AWT164 (no usable method) or AWT190 (an overload the module cannot pick between). Mirrors ResolveHook,
		// naming the module as the owner the hook was looked up on.
		if (matches.Count != 1 || ambiguous.Count > 0)
		{
			context.Diagnostics.Add(new DiagnosticInfo(
				matches.Count + ambiguous.Count == 0 ? Diagnostics.InvalidLifecycleHook : Diagnostics.AmbiguousLifecycleHook,
				LocationInfo.From(location),
				new EquatableArray<string>([Display(typeName), hookName, $"the module '{Display(context.Module.ToDisplayString(FullyQualified))}'",])));
			return null;
		}

		IMethodSymbol hook = matches[0];
		if (!TryMirrorHookParameters(typeName, hook, release, location, context.Diagnostics, out EquatableArray<FactoryParameter> parameters))
		{
			return null;
		}

		// The wrapper casts the exposure interface it receives to the hook's first-parameter type (the internal
		// concrete match, or the closed marker form of a generic hook) and forwards it to the user hook, carrying its
		// bound type arguments for a generic one - all legal because the wrapper compiles inside the module.
		string instanceCast = hook.Parameters[0].Type.ToDisplayString(FullyQualified);
		string target = hook.TypeArguments.Length == 0
			? hookName
			: $"{hookName}<{string.Join(", ", hook.TypeArguments.Select(argument => argument.ToDisplayString(FullyQualified)))}>";
		return new ResolvedModuleHook(hookName, new ModuleHook(ModuleHookWrapperName(matchType, release), target, instanceCast, parameters));
	}

	/// <summary>
	///     The usable overloads of a module hook (see <see cref="ResolveModuleHook" />): the module's own
	///     <c>static void</c> methods of that name whose first parameter accepts the match (a non-generic one
	///     directly, a generic one bound to exactly one closed marker form), plus separately the closed-form sets of
	///     any generic overload several forms could bind (AWT198 when it is the only candidate). A hook must be
	///     accessible from a separate type in the module's assembly (<c>internal</c> or wider): the wrapper could
	///     call even a <c>private</c> hook, but a same-compilation container binding the hook directly could not, so a
	///     <c>private</c> method is not a usable hook here either and falls through to AWT164. A method with a
	///     by-ref (<c>ref</c>/<c>in</c>/<c>out</c>) parameter is not usable either: the wrapper mirrors bare
	///     by-value parameters and the container's hook invocation supplies plain graph values, so forwarding such a
	///     method could not compile; it too falls through to AWT164.
	/// </summary>
	private static (List<IMethodSymbol> Matches, List<IReadOnlyList<INamedTypeSymbol>> Ambiguous) CollectModuleHookOverloads(
		INamedTypeSymbol matchType,
		string hookName,
		IReadOnlyList<INamedTypeSymbol>? closedMarkers,
		ModuleScanContext context)
	{
		Compilation compilation = context.Compilation;
		List<IMethodSymbol> matches = new();
		List<IReadOnlyList<INamedTypeSymbol>> ambiguous = new();
		foreach (ISymbol member in AccessibleMembers(context.Module, hookName, compilation))
		{
			// A hook must be reachable from a separate type in the module's assembly - internal or wider. The wrapper
			// sits in the module class and could call even a private hook, but a same-compilation container binds the
			// hook directly, so a private hook usable one way and AWT164 the other would be inconsistent; a private
			// method is not a usable hook, falling through to AWT164 in both.
			if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: true, ReturnsVoid: true, Parameters.Length: >= 1, } method
			    || !compilation.IsSymbolAccessibleWithin(method, context.Module.ContainingAssembly)
			    || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
			{
				continue;
			}

			if (method.Arity == 0)
			{
				if (compilation.HasImplicitConversion(matchType, method.Parameters[0].Type))
				{
					matches.Add(method);
				}

				continue;
			}

			List<(INamedTypeSymbol Form, IMethodSymbol Constructed)> bindable =
				BindableHookConstructions(method, closedMarkers ?? Array.Empty<INamedTypeSymbol>(), matchType, context.Module, compilation);
			if (bindable.Count == 1)
			{
				matches.Add(bindable[0].Constructed);
			}
			else if (bindable.Count > 1)
			{
				ambiguous.Add(bindable.Select(candidate => candidate.Form).ToList());
			}
		}

		return (matches, ambiguous);
	}

	/// <summary>
	///     Mirrors a module hook's parameters after the instance onto the public wrapper (see
	///     <see cref="ResolveModuleHook" />), or reports why it cannot and returns <see langword="false" /> (the hook
	///     is dropped). Each parameter is resolved from the consumer's graph like a factory's, so a runtime
	///     <c>[Arg]</c> is <see cref="Diagnostics.HookParameterIsArg">AWT189</see>, a <c>Func</c>/<c>Lazy</c> on a
	///     release hook is <see cref="Diagnostics.ReleaseHookDeferredParameter">AWT191</see>, a type not nameable
	///     outside the assembly is <see cref="Diagnostics.ModuleScanHookParameterInaccessible">AWT203</see> (the
	///     hook-parameter twin of the factory-parameter AWT195), and <c>[FromKey]</c>/<c>[Inject]</c> metadata the
	///     bare wrapper signature would silently drop is
	///     <see cref="Diagnostics.ModuleScanHookInjectionMetadata">AWT204</see> (the twin of AWT200). The instance
	///     parameter is never checked here: it is the accessible exposure interface, cast to the internal match
	///     inside the module.
	/// </summary>
	private static bool TryMirrorHookParameters(
		string typeName,
		IMethodSymbol hook,
		bool release,
		Location? location,
		List<DiagnosticInfo> diagnostics,
		out EquatableArray<FactoryParameter> parameters)
	{
		parameters = default;
		List<FactoryParameter> mirrored = new();
		foreach (IParameterSymbol parameter in hook.Parameters.Skip(1))
		{
			if (HookParameterMirrorError(typeName, parameter, release, location) is { } error)
			{
				diagnostics.Add(error);
				return false;
			}

			mirrored.Add(new FactoryParameter(parameter.Type.ToDisplayString(FullyQualifiedWithNullability), parameter.Name));
		}

		parameters = new EquatableArray<FactoryParameter>(mirrored.ToArray());
		return true;
	}

	/// <summary>
	///     Why one hook parameter cannot be mirrored onto the public wrapper (see
	///     <see cref="TryMirrorHookParameters" />), or <see langword="null" /> when it can.
	/// </summary>
	private static DiagnosticInfo? HookParameterMirrorError(string typeName, IParameterSymbol parameter, bool release, Location? location)
	{
		ImmutableArray<AttributeData> attributes = parameter.GetAttributes();
		if (HasArgAttribute(attributes))
		{
			return new DiagnosticInfo(
				Diagnostics.HookParameterIsArg,
				LocationInfo.From(location),
				new EquatableArray<string>([parameter.Name, Display(typeName),]));
		}

		// [FromKey]/[Inject] carry per-dependency semantics (a key, optionality, deferral) the wrapper mirrors
		// away: its bare (type, name) signature would make a cross-assembly consumer resolve the plain type
		// while a same-compilation container, binding the hook directly, honored the attribute. Rejected rather
		// than silently degraded - the hook-parameter twin of the factory parameter's AWT200.
		if (HasAwaitenAttribute(attributes, "FromKeyAttribute") || HasInject(attributes))
		{
			return new DiagnosticInfo(
				Diagnostics.ModuleScanHookInjectionMetadata,
				LocationInfo.From(location),
				new EquatableArray<string>([
					Display(typeName),
					release ? "OnRelease" : "OnActivated",
					parameter.Name,
					HasInject(attributes) ? "[Inject]" : "[FromKey]",
				]));
		}

		if (release
		    && ClassifyParameter(parameter, asyncFactory: false, new HashSet<string>(StringComparer.Ordinal)).Kind
			    is DependencyKind.Func or DependencyKind.Lazy or DependencyKind.FuncTask or DependencyKind.LazyTask)
		{
			return new DiagnosticInfo(
				Diagnostics.ReleaseHookDeferredParameter,
				LocationInfo.From(location),
				new EquatableArray<string>([parameter.Name, Display(typeName),]));
		}

		if (!IsExternallyAccessible(parameter.Type))
		{
			return new DiagnosticInfo(
				Diagnostics.ModuleScanHookParameterInaccessible,
				LocationInfo.From(location),
				new EquatableArray<string>([Display(typeName), release ? "OnRelease" : "OnActivated", Display(parameter.Type.ToDisplayString(FullyQualified)),]));
		}

		return null;
	}

	/// <summary>
	///     A deterministic wrapper name for one match's hook slot, stable across library versions and scan order the
	///     way <see cref="ModuleFactoryName" /> is: the slot and the match's simple name for readability, plus an
	///     FNV-1a hash of the match's fully-qualified name so two same-named matches stay distinct.
	/// </summary>
	private static string ModuleHookWrapperName(INamedTypeSymbol type, bool release)
	{
		uint hash = 2166136261;
		foreach (char character in type.ToDisplayString(FullyQualified))
		{
			hash = unchecked((hash ^ character) * 16777619);
		}

		return $"Awaiten__ScanHook_{(release ? "OnRelease" : "OnActivated")}_{type.Name}_{hash:x8}";
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
	///     Handles a match an earlier scan of this module already exposed, returning the (possibly hook-updated)
	///     expansion that stands for both. Two <c>[Scan]</c>s on one module can match the same type: under the same
	///     exposure the overlap is deduped to the first factory (AWT142 when the lifetimes differ, the first scan's
	///     lifetime winning consistently, mirroring the container's per-implementation dedup), and the scans'
	///     lifecycle hooks merge per slot exactly like two container scans' (<c>MergeScanHooks</c>): this scan's
	///     hook fills a slot no earlier scan claimed, a restated hook contributes this scan's closed marker forms
	///     (re-resolved over the union, so a closing that leaves a generic hook ambiguous is AWT198 at the module
	///     build, just as a consuming container would see it), and naming a different method for a claimed slot is
	///     AWT199, the first scan winning. Under a different exposure the overlap is AWT197, because each factory
	///     constructs its own instance, so the single shared instance a container scan gives one implementation
	///     across interfaces cannot be expressed and emitting both would silently split it.
	/// </summary>
	private static ModuleScanExpansion ResolveOverlap(
		ModuleScanExpansion overlapping,
		string typeName,
		string serviceName,
		INamedTypeSymbol type,
		ScanMatch match,
		ScanMarkerInfo markerInfo,
		ModuleScanContext context)
	{
		if (overlapping.Factory.ServiceType != serviceName)
		{
			context.Diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ModuleScanMultipleExposures,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([
					Display(typeName),
					$"{Display(overlapping.Factory.ServiceType)}, {Display(serviceName)}",
				])));
			return overlapping;
		}

		if (overlapping.Factory.Lifetime != match.Lifetime)
		{
			context.Diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanLifetimeConflict,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([
					Display(typeName),
					overlapping.Factory.Lifetime.ToString(),
					match.Lifetime.ToString(),
				])));
		}

		if (match.OnActivated is null && match.OnRelease is null)
		{
			return overlapping;
		}

		IReadOnlyList<INamedTypeSymbol>? markers = ScanHookMarkers(type, match, markerInfo.Open, markerInfo.Definition);
		ModuleHookSlot onActivated = MergeModuleScanHookSlot(
			new ModuleHookSlot(overlapping.OnActivated, overlapping.Factory.OnActivated, overlapping.OnActivatedMarkers),
			type, match, release: false, markers, context);
		ModuleHookSlot onRelease = MergeModuleScanHookSlot(
			new ModuleHookSlot(overlapping.OnRelease, overlapping.Factory.OnRelease, overlapping.OnReleaseMarkers),
			type, match, release: true, markers, context);

		return overlapping with
		{
			Factory = overlapping.Factory with { OnActivated = onActivated.Wrapper, OnRelease = onRelease.Wrapper, },
			OnActivated = onActivated.Name,
			OnRelease = onRelease.Name,
			OnActivatedMarkers = onActivated.Markers,
			OnReleaseMarkers = onRelease.Markers,
		};
	}

	/// <summary>
	///     One hook slot of a module-scan expansion while overlapping scans merge (see <see cref="ResolveOverlap" />):
	///     the user hook's name, its generated wrapper (absent when the hook failed to resolve, which keeps the
	///     claim without wiring anything), and the closed marker forms contributed for it.
	/// </summary>
	private readonly record struct ModuleHookSlot(string? Name, ModuleHook? Wrapper, IReadOnlyList<INamedTypeSymbol>? Markers);

	/// <summary>
	///     Merges one hook slot of an overlapped module-scan match (see <see cref="ResolveOverlap" />), the module
	///     build's counterpart of the container's <c>MergeScanHookSlot</c>: this scan's hook fills an unclaimed slot
	///     (resolved with this scan's closed marker forms), a restated name contributes new marker forms and
	///     re-resolves the slot over the union (a closing that leaves a generic hook ambiguous is AWT198 and drops
	///     the hook, exactly what a same-compilation consumer resolving over the union would report), and a
	///     different name for a claimed slot is AWT199, the first scan winning. A failed resolution keeps the slot
	///     claimed - name and accumulated markers intact, only the wrapper absent - so a later scan restating the
	///     name re-resolves over the full union rather than quietly succeeding on its own narrower marker set (which
	///     would wire the hook to a scan-order-dependent closing beside the already-reported failure), and a later
	///     different name still conflicts.
	/// </summary>
	private static ModuleHookSlot MergeModuleScanHookSlot(
		ModuleHookSlot current,
		INamedTypeSymbol type,
		ScanMatch match,
		bool release,
		IReadOnlyList<INamedTypeSymbol>? markers,
		ModuleScanContext context)
	{
		string? name = release ? match.OnRelease : match.OnActivated;
		if (name is null)
		{
			return current;
		}

		if (current.Name is null)
		{
			ResolvedModuleHook? resolved = ResolveModuleHook(type, match, release, markers, context);
			return new ModuleHookSlot(name, resolved?.Wrapper, markers);
		}

		if (!string.Equals(current.Name, name, StringComparison.Ordinal))
		{
			context.Diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanHookConflict,
				LocationInfo.From(match.Location),
				new EquatableArray<string>([
					Display(type.ToDisplayString(FullyQualified)),
					release ? "OnRelease" : "OnActivated",
					current.Name,
					name,
				])));
			return current;
		}

		IReadOnlyList<INamedTypeSymbol>? union = UnionMarkers(current.Markers, markers);
		if (ReferenceEquals(union, current.Markers))
		{
			return current;
		}

		ResolvedModuleHook? reresolved = ResolveModuleHook(type, match, release, union, context);
		return new ModuleHookSlot(name, reresolved?.Wrapper, union);
	}

	/// <summary>
	///     The union of two closed-marker-form sets, deduped by symbol; returns <paramref name="current" /> itself
	///     (reference-equal) when <paramref name="added" /> contributes nothing new, so the caller can skip
	///     re-resolving an unchanged slot.
	/// </summary>
	private static IReadOnlyList<INamedTypeSymbol>? UnionMarkers(
		IReadOnlyList<INamedTypeSymbol>? current,
		IReadOnlyList<INamedTypeSymbol>? added)
	{
		if (added is null || added.Count == 0)
		{
			return current;
		}

		if (current is null || current.Count == 0)
		{
			return added;
		}

		List<INamedTypeSymbol> union = new(current);
		union.AddRange(added.Where(marker => !current.Any(seen => SymbolEqualityComparer.Default.Equals(seen, marker))));
		return union.Count == current.Count ? current : union;
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
