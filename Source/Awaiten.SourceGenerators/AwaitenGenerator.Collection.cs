using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     The marker inside a self-compiled module-scan match's synthetic implementation identity
	///     (<c>&lt;service&gt;@scan:&lt;qualified factory&gt;</c>, see <see cref="CollectGeneratedScanRegistration" />).
	///     <c>DisplayInstance</c> keys off it to trim the synthetic suffix from diagnostics.
	/// </summary>
	private const string ScanKeyMarker = "@scan:";

	private static (List<RawRegistration> Raw, HashSet<string> ConstraintRejectedServices) Collect(
		INamedTypeSymbol containerSymbol,
		List<ImportedModule> modules,
		Compilation compilation,
		ExternalSurface external,
		Dictionary<string, List<InjectPropertyEntry>> injectProperties,
		List<DiagnosticInfo> diagnostics)
	{
		List<RawRegistration> result = new();

		// Closed services an open registration could not produce because the closed type arguments violate the
		// implementation's constraints (AWT126). The emitter suppresses the AWT101 these would also raise.
		HashSet<string> constraintRejected = new(StringComparer.Ordinal);

		// Open generic registrations are kept apart: not instances themselves, but templates expanded into
		// concrete closed registrations on demand (see ExpandOpenGenerics).
		List<OpenRegistration> open = new();

		CollectLifetimeRegistrations(new LifetimeCollectionContext(containerSymbol, null, null, compilation, open, diagnostics), result);

		// [Import(typeof(Module))] pulls a module's registrations in after the container's own, so the container
		// wins ties and a module's overridable defaults (Fallback.Warn/Silent) only fill the gaps it leaves. Resolved
		// one level deep; a module's own [Import] is not followed. A same-compilation module's
		// [GeneratedScanRegistration] attributes are skipped: the generator itself never sees them (its own output),
		// but an analyzer running over the post-generation compilation does, and collecting them alongside the
		// direct scan expansion below would double every match in its graph.
		foreach (ImportedModule module in modules)
		{
			bool sameCompilation = SymbolEqualityComparer.Default.Equals(module.Symbol.ContainingAssembly, containerSymbol.ContainingAssembly);
			CollectLifetimeRegistrations(
				new LifetimeCollectionContext(module.Symbol, module.Symbol, module.ImportLocation, compilation, open, diagnostics),
				result,
				skipGeneratedScanRegistrations: sameCompilation);
		}

		// The container's own scan matches must outrank a module's within the scan tier (the container wins ties
		// there like everywhere else), so the module-collected [GeneratedScanRegistration] matches are pulled out
		// and re-added after the container's scan expansion; the same-compilation expansions below append later
		// still, giving both module forms the same rank relative to the container's own scans.
		List<RawRegistration> moduleScanRegistrations = result.FindAll(registration => registration.IsScan);
		result.RemoveAll(registration => registration.IsScan);

		// Assembly scanning contributes overridable registrations for every concrete type assignable to a
		// [Scan] marker. Appended before open generic expansion so scanned implementations seed it: their
		// constructors may require closed generics only an open registration can provide.
		List<RawRegistration> scans = CollectScans(containerSymbol, compilation, diagnostics);
		result.AddRange(scans);
		result.AddRange(moduleScanRegistrations);

		CollectSameCompilationModuleScans(containerSymbol, modules, compilation, result);

		// Expand open generic registrations: for every closed generic service required from the graph
		// whose open form is registered but which has no concrete registration, synthesize the closed
		// implementation (iterating to a fixpoint over its own generic dependencies).
		if (open.Count > 0)
		{
			ExpandOpenGenerics(result, open, new ExpansionInputs(containerSymbol, compilation, external, injectProperties), diagnostics, constraintRejected);
		}

		// ...then moved back to the end: coalescing is first-wins per service, so the explicit registrations and
		// the closed registrations expanded from them must precede the overridable scan ones — the container's own
		// [Scan] matches and a module's self-compiled [GeneratedScanRegistration] matches alike, both IsScan.
		List<RawRegistration> scanMatches = result.FindAll(registration => registration.IsScan);
		if (scanMatches.Count > 0)
		{
			result.RemoveAll(registration => registration.IsScan);
			result.AddRange(scanMatches);
		}

		return (result, constraintRejected);
	}

	/// <summary>
	///     Expands a same-compilation module's <c>[Scan]</c> into <paramref name="result" />: the generator cannot
	///     see its own output, so the module's self-compiled <c>[GeneratedScanRegistration]</c> attributes are
	///     invisible within the compilation that declares the module. The expansion runs the module pipeline's own
	///     logic (<see cref="CollectModuleScanFactories" />), so a scan means exactly the same thing wherever the
	///     module lives - one accessible exposure per match, matches the module build skipped (AWT196) stay skipped,
	///     and construction goes through the greediest accessible constructor, the one the generated factory mirrors -
	///     except that with no assembly boundary the container constructs the match directly instead of through the
	///     factory it cannot resolve. A referenced-assembly module is excluded: its metadata carries the self-compiled
	///     expansion instead, and re-running its scan here would double-register every match. The expansion's
	///     diagnostics are discarded: the module pipeline (BuildModuleModel) already reports on the same <c>[Scan]</c>
	///     at the same location, and reporting here too would double every scan-level warning.
	/// </summary>
	private static void CollectSameCompilationModuleScans(
		INamedTypeSymbol containerSymbol,
		List<ImportedModule> modules,
		Compilation compilation,
		List<RawRegistration> result)
	{
		foreach (INamedTypeSymbol moduleSymbol in modules.Select(module => module.Symbol))
		{
			if (!SymbolEqualityComparer.Default.Equals(moduleSymbol.ContainingAssembly, containerSymbol.ContainingAssembly))
			{
				continue;
			}

			foreach (ModuleScanExpansion expansion in CollectModuleScanFactories(moduleSymbol, compilation, new List<DiagnosticInfo>(), CancellationToken.None))
			{
				result.Add(SameCompilationScanRegistration(expansion, moduleSymbol));
			}
		}
	}

	/// <summary>
	///     The registration one same-compilation module-scan expansion contributes (see
	///     <see cref="CollectSameCompilationModuleScans" />). With no assembly boundary the container binds the
	///     module's own (possibly internal) hook directly rather than the generated wrapper it cannot see, so each
	///     hook resolves against the module - the coalescer keeps the origin per hook slot - through the ordinary
	///     pipeline. The user hook names travel on the expansion, but a slot whose hook failed to resolve keeps its
	///     claimed name with no wrapper on the factory (see <c>MergeModuleScanHookSlot</c>), so wiring is gated on
	///     the wrapper: an invalid hook - already reported at the module's build - is not wired and re-reported
	///     here. Each wired slot's closed marker forms bind a generic hook's type argument.
	/// </summary>
	private static RawRegistration SameCompilationScanRegistration(ModuleScanExpansion expansion, INamedTypeSymbol moduleSymbol)
		=> new(
			expansion.Factory.ServiceType,
			expansion.Factory.ImplementationType,
			expansion.Factory.Lifetime,
			expansion.Implementation,
			expansion.Location,
			ServiceSymbol: expansion.Service,
			IsScan: true,
			ScanSkipsUnconstructable: expansion.Factory.SkipUnconstructable,
			GreedyConstructor: true,
			Origin: moduleSymbol,
			OnActivated: expansion.Factory.OnActivated is null ? null : expansion.OnActivated,
			OnRelease: expansion.Factory.OnRelease is null ? null : expansion.OnRelease,
			OnActivatedMarkers: expansion.Factory.OnActivated is null ? null : expansion.OnActivatedMarkers,
			OnReleaseMarkers: expansion.Factory.OnRelease is null ? null : expansion.OnReleaseMarkers);

	/// <summary>
	///     The state shared while reading the lifetime registrations of one container or imported module.
	///     <see cref="Owner" /> declares the attributes. <see cref="Origin" /> is the imported module being read
	///     (<see langword="null" /> for the container), recorded so a module's <c>Factory</c>/<c>Instance</c> member
	///     resolves against the module. <see cref="FallbackLocation" /> is the container's <c>[Import]</c> location,
	///     used for a referenced-assembly module whose attributes have no syntax. The open generic <c>typeof</c>-form
	///     registrations go into <see cref="Open" />.
	/// </summary>
	private readonly record struct LifetimeCollectionContext(
		INamedTypeSymbol Owner,
		INamedTypeSymbol? Origin,
		Location? FallbackLocation,
		Compilation Compilation,
		List<OpenRegistration> Open,
		List<DiagnosticInfo> Diagnostics);

	/// <summary>
	///     Reads the <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c> registrations declared on the context's
	///     owner (a container or imported module) into <paramref name="result" />, and the open generic
	///     <c>typeof</c>-form ones into the context's open registrations; a closed <c>typeof</c> pair registers like
	///     the generic form.
	/// </summary>
	private static void CollectLifetimeRegistrations(
		LifetimeCollectionContext context,
		List<RawRegistration> result,
		bool skipGeneratedScanRegistrations = false)
	{
		INamedTypeSymbol? origin = context.Origin;
		Location? fallbackLocation = context.FallbackLocation;
		List<DiagnosticInfo> diagnostics = context.Diagnostics;
		foreach (AttributeData attribute in context.Owner.GetAttributes())
		{
			if (attribute.AttributeClass is not { } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
			{
				continue;
			}

			// A [GeneratedScanRegistration<TService>(factory, Lifetime = …)] is what a [Module] self-compiled from
			// its own [Scan] (one per match), read back and consumed there.
			if (CollectGeneratedScanRegistration(attribute, attributeClass, result, origin, fallbackLocation, skipGeneratedScanRegistrations))
			{
				continue;
			}

			Lifetime? lifetime = attributeClass.Name switch
			{
				"SingletonAttribute" => Lifetime.Singleton,
				"TransientAttribute" => Lifetime.Transient,
				"ScopedAttribute" => Lifetime.Scoped,
				_ => null,
			};
			if (lifetime is null)
			{
				continue;
			}

			if (!TryReadRegistrationTypes(attribute, attributeClass, lifetime.Value, context, out ITypeSymbol? implementation, out ITypeSymbol? service))
			{
				continue;
			}

			(ProductionKind production, string? productionMember, bool conflictingDirectives) =
				ReadProduction(attribute);

			// Both overridable-default modes only fill a gap, so both are weak and yield to a stronger or earlier
			// registration. The Warn mode additionally opts into the AWT148 ambiguity warning when two collide
			// with nothing stronger to resolve them; None is a normal strong registration.
			int fallback = NamedFallback(attribute);
			bool weak = fallback != 0;
			bool isDefault = fallback == 1;

			string? key = NamedKeyArgument(attribute);

			// A [Singleton<…>(WhenInjectedInto = typeof(Consumer))] contextual binding, resolved to the consumer's
			// context key.
			string? whenInjectedInto = NamedTypeArgument(attribute, "WhenInjectedInto");

			Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallbackLocation;

			ReportContextualBindingWithKey(key, whenInjectedInto, implementation, location, diagnostics);
			ReportUnsupportedRegistrationKey(attribute, LocationInfo.From(location), Display(implementation.ToDisplayString(FullyQualified)), diagnostics);

			result.Add(new RawRegistration(
				service.ToDisplayString(FullyQualified),
				implementation.ToDisplayString(FullyQualified),
				lifetime.Value,
				(INamedTypeSymbol)implementation,
				location,
				production,
				productionMember,
				conflictingDirectives,
				key,
				service as INamedTypeSymbol,
				Weak: weak,
				IsDefault: isDefault,
				Origin: origin,
				// Eager is exposed on [Singleton] alone; a Transient/Scoped attribute has no such property, so
				// this reads false there. BuildInstance honors it only for a singleton lifetime.
				Eager: NamedFlag(attribute, "Eager"),
				OnActivated: NamedArgument(attribute, "OnActivated"),
				OnRelease: NamedArgument(attribute, "OnRelease"),
				SuppressDisposal: NamedFlag(attribute, "SuppressDisposal"),
				WhenInjectedInto: whenInjectedInto));
		}
	}

	/// <summary>
	///     Reads the implementation and service of a closed lifetime registration, from the generic form's type
	///     arguments or the <c>typeof</c> form's constructor arguments. Returns <see langword="false" /> when there is
	///     nothing to register: an argument does not bind, an open <c>typeof</c> pair was recorded as an open
	///     registration instead, or a closed pair is not assignable (reported).
	/// </summary>
	private static bool TryReadRegistrationTypes(
		AttributeData attribute,
		INamedTypeSymbol attributeClass,
		Lifetime lifetime,
		LifetimeCollectionContext context,
		[NotNullWhen(true)] out ITypeSymbol? implementation,
		[NotNullWhen(true)] out ITypeSymbol? service)
	{
		if (attributeClass.IsGenericType)
		{
			ImmutableArrayGuard(attributeClass.TypeArguments, out implementation, out service);
			service ??= implementation;
			return implementation is not null;
		}

		implementation = null;
		service = null;

		// The non-generic Type-ctor form, [Transient(typeof(Repository<>), typeof(IRepository<>))], is the only
		// way to name an open generic, which cannot be a type argument. An open pair is recorded as a template
		// expanded into concrete closed registrations on demand; a closed pair registers like the generic form.
		if (!TryReadTypeofPair(attribute, out INamedTypeSymbol typeofImplementation, out INamedTypeSymbol typeofService))
		{
			return false;
		}

		if (typeofImplementation.IsUnboundGenericType || typeofService.IsUnboundGenericType)
		{
			CollectOpenRegistration(attribute, lifetime, typeofImplementation, typeofService, context);
			return false;
		}

		if (!IsAssignableRegistration(typeofImplementation, typeofService, context.Compilation))
		{
			context.Diagnostics.Add(new DiagnosticInfo(
				Diagnostics.TypeofImplementationNotAssignable,
				LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? context.FallbackLocation),
				new EquatableArray<string>([
					Display(typeofImplementation.ToDisplayString(FullyQualified)),
					Display(typeofService.ToDisplayString(FullyQualified)),
				])));
			return false;
		}

		implementation = typeofImplementation;
		service = typeofService;
		return true;
	}

	/// <summary>
	///     Reads one <c>[GeneratedScanRegistration&lt;TService&gt;(factory, Lifetime = …)]</c> - what a
	///     <c>[Module]</c> self-compiled from its own <c>[Scan]</c>, one per match - into an <c>IsScan</c> factory
	///     registration, so the consuming container treats it exactly like a container <c>[Scan]</c> match:
	///     collection-eligible (several matches under one interface resolve as an <c>IEnumerable</c>) and overridable
	///     by an explicit registration (scans rank last). Each match gets a synthetic implementation identity keyed
	///     by its module-qualified factory name, distinct from the service's own type so the emitter still constructs
	///     through the accessible service; without it two matches of one interface would collapse into one
	///     implementation and collide as conflicting factories. The module qualification matters: factory names are
	///     only unique per module, so two imported modules whose matches share a simple type name would otherwise
	///     collapse into one member, silently dropping the later module's match from the collection. Returns whether
	///     the attribute was a <c>[GeneratedScanRegistration]</c> (so the caller skips its lifetime handling), even
	///     when <paramref name="skip" /> suppressed the collection for a same-compilation module whose <c>[Scan]</c>
	///     the container expands directly (see <see cref="Collect" />).
	/// </summary>
	private static bool CollectGeneratedScanRegistration(
		AttributeData attribute,
		INamedTypeSymbol attributeClass,
		List<RawRegistration> result,
		INamedTypeSymbol? origin,
		Location? fallbackLocation,
		bool skip)
	{
		if (attributeClass is not { Name: "GeneratedScanRegistrationAttribute", IsGenericType: true, })
		{
			return false;
		}

		if (skip
		    || attributeClass.TypeArguments.Length != 1
		    || attributeClass.TypeArguments[0] is not INamedTypeSymbol service
		    || attribute.ConstructorArguments.Length != 1
		    || attribute.ConstructorArguments[0].Value is not string factory)
		{
			return true;
		}

		Lifetime lifetime = Lifetime.Transient;
		bool skipUnconstructable = false;
		string? onActivated = null;
		string? onRelease = null;
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == "Lifetime" && argument.Value.Value is int value)
			{
				lifetime = (Lifetime)value;
			}
			else if (argument.Key == "SkipUnconstructable" && argument.Value.Value is bool flag)
			{
				skipUnconstructable = flag;
			}
			else if (argument.Key == "OnActivated" && argument.Value.Value is string activation)
			{
				onActivated = activation;
			}
			else if (argument.Key == "OnRelease" && argument.Value.Value is string release)
			{
				onRelease = release;
			}
		}

		string serviceType = service.ToDisplayString(FullyQualified);
		Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallbackLocation;
		string qualifiedFactory = origin is null ? factory : $"{origin.ToDisplayString(FullyQualified)}.{factory}";

		result.Add(new RawRegistration(
			serviceType,
			$"{serviceType}{ScanKeyMarker}{qualifiedFactory}",
			lifetime,
			service,
			location,
			ProductionKind.Factory,
			factory,
			ServiceSymbol: service,
			IsScan: true,
			ScanSkipsUnconstructable: skipUnconstructable,
			Origin: origin,
			// The hook wrapper names, resolved by the ordinary hook pipeline against the module (Origin): the
			// wrapper is a plain public static method, so ResolveHook binds it directly and ClassifyHookParameters
			// resolves its post-instance parameters from this container's graph.
			OnActivated: onActivated,
			OnRelease: onRelease));
		return true;
	}

	/// <summary>
	///     AWT168: a contextual binding is stored under a synthetic per-consumer key (see <c>ContextKey</c>), which
	///     overrides an explicit <c>Key</c> entirely, so the two together silently drop the <c>Key</c>. Report it
	///     rather than let a <c>[FromKey]</c> the author expects to select this registration quietly never match.
	/// </summary>
	private static void ReportContextualBindingWithKey(
		string? key,
		string? whenInjectedInto,
		ITypeSymbol implementation,
		Location? location,
		List<DiagnosticInfo> diagnostics)
	{
		if (key is null || whenInjectedInto is null)
		{
			return;
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.ContextualBindingWithKey,
			LocationInfo.From(location),
			new EquatableArray<string>([Display(implementation.ToDisplayString(FullyQualified)),])));
	}

	/// <summary>
	///     Reads the modules a container pulls in with <c>[Import(typeof(Module))]</c>, in declaration order,
	///     validating each as it goes: the target must be a <c>[Module]</c> (AWT149, else skipped), a module's
	///     own <c>[Import]</c> is reported as not-followed (AWT150), and a module declaring no registrations is
	///     reported as contributing nothing (AWT151). Only the container's own imports are read.
	/// </summary>
	private static List<ImportedModule> CollectImportedModules(INamedTypeSymbol containerSymbol, List<DiagnosticInfo> diagnostics)
	{
		List<ImportedModule> modules = new();
		HashSet<INamedTypeSymbol> seen = new(SymbolEqualityComparer.Default);
		foreach (AttributeData attribute in containerSymbol.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "ImportAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
			{
				continue;
			}

			// [Import(typeof(Module))] names the module as the single constructor argument. There is deliberately
			// no generic [Import<TModule>] form: a module must be a static class, and C# forbids a static class
			// as a generic type argument (CS0718).
			INamedTypeSymbol? module = attribute.ConstructorArguments.Length == 1
				? attribute.ConstructorArguments[0].Value as INamedTypeSymbol
				: null;
			if (module is null)
			{
				continue;
			}

			// A module imported more than once contributes only once: lifetime registrations coalesce away, but
			// decorators would otherwise double-wrap (the chain builder does not dedup). Skip the redundant
			// import silently, before validation, so its diagnostics are not reported twice either.
			if (!seen.Add(module))
			{
				continue;
			}

			// Every module diagnostic points at the container's [Import], the line the author actually wrote,
			// not the module declaration, which may live in another file (or, for a module compiled into a
			// referenced assembly, in no source at all).
			Location? importLocation = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
			bool sameCompilation = SymbolEqualityComparer.Default.Equals(module.ContainingAssembly, containerSymbol.ContainingAssembly);
			if (ValidateImportedModule(module, importLocation, sameCompilation, diagnostics))
			{
				modules.Add(new ImportedModule(module, importLocation));
			}
		}

		return modules;
	}

	/// <summary>
	///     Validates one <c>[Import(typeof(Module))]</c> target, reporting AWT149-152, and returns whether it is
	///     importable. A non-<c>[Module]</c> target (AWT149) is skipped (<see langword="false" />); the other faults
	///     are reported but still imported (<see langword="true" />): non-static (AWT152), a nested <c>[Import]</c>
	///     (AWT150), or no registrations (AWT151). A module-declared <c>[Scan]</c> is accepted (self-compiled in the
	///     module's own build, AWT154 retired) rather than rejected. <paramref name="sameCompilation" /> marks a
	///     module declared in the container's own compilation, whose <c>[Scan]</c> the container expands directly
	///     (its self-compiled attributes are invisible here), so the scan counts as a registration for AWT151.
	/// </summary>
	private static bool ValidateImportedModule(
		INamedTypeSymbol module,
		Location? importLocation,
		bool sameCompilation,
		List<DiagnosticInfo> diagnostics)
	{
		LocationInfo? location = LocationInfo.From(importLocation);
		ImmutableArray<AttributeData> moduleAttributes = module.GetAttributes();
		string moduleName = Display(module.ToDisplayString(FullyQualified));

		// AWT149: only [Module] types can be imported. A non-module target contributes nothing, so it is
		// skipped and surfaced here rather than as a later cascade of missing dependencies.
		if (!HasAwaitenAttribute(moduleAttributes, "ModuleAttribute"))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ImportNotAModule, location, new EquatableArray<string>([moduleName,])));
			return false;
		}

		// AWT152: like a container, a module is a pure definition (registrations plus static factory and
		// instance members), never instantiated, so it must be a static class (mirroring AWT116). Still
		// imported, so its registrations do not additionally cascade as missing.
		if (!module.IsStatic)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NonStaticModule, location, new EquatableArray<string>([moduleName,])));
		}

		// AWT150: a module's own [Import] is not followed, so the nested module's registrations are not pulled
		// in transitively. The container must import the nested module directly.
		if (HasAwaitenAttribute(moduleAttributes, "ImportAttribute"))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NestedModuleImport, location, new EquatableArray<string>([moduleName,])));
		}

		// A referenced-assembly module's [Scan] is self-compiled in the module's own build (the module emits a
		// generated factory and lifetime registration per match, plus a [GeneratedScanExpansion] marker), which
		// this collection reads like any other module registration. A same-compilation module's [Scan] is instead
		// expanded by the container itself (see Collect); the generator cannot see the module's generated marker
		// there (its own output), so the skew check below excludes it.
		bool declaresScan = HasAwaitenAttribute(moduleAttributes, "ScanAttribute");

		// AWT154: the metadata carries a [Scan] but no expansion marker, so the module assembly was built without
		// the Awaiten generator (or a version predating self-compiled scans) and the scan would silently
		// contribute nothing. The marker is emitted even for a scan that matched nothing, so its absence is
		// conclusive, not a maybe.
		if (!sameCompilation && declaresScan && !HasAwaitenAttribute(moduleAttributes, "GeneratedScanExpansionAttribute"))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ModuleScanNotExpanded, location, new EquatableArray<string>([moduleName,])));
		}

		// AWT151: a module that declares nothing to contribute imports nothing useful. A module with a [Scan]
		// is exempt regardless of what the scan produced: the module's own build reports an empty scan at its
		// declaration (AWT138/AWT184), and an unexpanded one is AWT154 above, so repeating the blame at the
		// consumer's [Import] would point at code the consumer does not own.
		if (!DeclaresAnyContribution(moduleAttributes) && !declaresScan)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.EmptyModule, location, new EquatableArray<string>([moduleName,])));
		}

		return true;
	}

	/// <summary>
	///     Whether an attribute list carries anything a module contributes to an importing container: a
	///     <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c> lifetime registration (generic or open
	///     <c>typeof</c> form), a <c>[Decorate]</c>, a <c>[Composite]</c>, <c>[ImportServices]</c> or an
	///     <c>[ImportService]</c> (generic or <c>typeof</c> form). Used to detect a module that declares nothing to
	///     import (AWT151). A module-declared <c>[Scan]</c> does not count here on its own: its matches are
	///     self-compiled into <c>[GeneratedScanRegistration]</c> attributes (which do count), so a scan-only module
	///     whose scan matched something carries those generated attributes.
	/// </summary>
	private static bool DeclaresAnyContribution(ImmutableArray<AttributeData> attributes)
	{
		foreach (AttributeData attribute in attributes)
		{
			if (attribute.AttributeClass is { } attributeClass
			    && attributeClass.ContainingNamespace?.ToDisplayString() == AttributeNamespace
			    && attributeClass.Name is "SingletonAttribute" or "TransientAttribute" or "ScopedAttribute"
				    or "DecorateAttribute" or "CompositeAttribute" or "ImportServicesAttribute"
				    or "ImportServiceAttribute" or "GeneratedScanRegistrationAttribute")
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	///     The attributes of the container followed by those of its imported modules, in import order: the
	///     shared enumeration for readers that accept module contributions (<c>[Decorate]</c>,
	///     <c>[Composite]</c>), so the container's declarations always precede a module's and an earlier
	///     import's precede a later one's. Each attribute is paired with a fallback location (the module's
	///     <c>[Import]</c>) for attributes read from a referenced assembly, which have no syntax of their own.
	/// </summary>
	private static IEnumerable<(AttributeData Attribute, Location? FallbackLocation)> AttributesOf(
		INamedTypeSymbol containerSymbol, List<ImportedModule> modules)
	{
		foreach (AttributeData attribute in containerSymbol.GetAttributes())
		{
			yield return (attribute, null);
		}

		foreach (ImportedModule module in modules)
		{
			foreach (AttributeData attribute in module.Symbol.GetAttributes())
			{
				yield return (attribute, module.ImportLocation);
			}
		}
	}

	/// <summary>
	///     Reads a boolean named argument off an attribute, treating anything but an explicit <c>true</c>
	///     (including its absence) as <see langword="false" />.
	/// </summary>
	private static bool NamedFlag(AttributeData attribute, string name)
	{
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == name && argument.Value.Value is true)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	///     Reads the <c>Fallback</c> enum named argument off a lifetime attribute as its underlying int
	///     (0 = None, 1 = Warn, 2 = Silent), defaulting to 0 (None, a normal registration) when unset. These
	///     values must stay in step with the <c>Fallback</c> enum, which the generator cannot reference directly.
	/// </summary>
	private static int NamedFallback(AttributeData attribute)
	{
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == "Fallback" && argument.Value.Value is int value)
			{
				return value;
			}
		}

		return 0;
	}

	/// <summary>
	///     Reads the <c>[Decorate]</c> registrations on a container and its modules, in declaration order (container
	///     first): the closed <c>[Decorate&lt;TDecorator, TService&gt;]</c> form and the open generic
	///     <c>[Decorate(typeof(D&lt;&gt;), typeof(IService&lt;&gt;))]</c> form. Both share one declaration counter so
	///     that, once the open form is expanded per closing, open and closed decorators of the same closed service
	///     interleave by equal <c>Order</c> then declaration order. Collected apart from lifetime registrations
	///     because a decorator wraps an existing registration after coalescing.
	/// </summary>
	private static (List<DecorateRegistration> Closed, List<OpenDecorateRegistration> Open) CollectDecorators(
		INamedTypeSymbol containerSymbol, List<ImportedModule> modules, List<DiagnosticInfo> diagnostics)
	{
		List<DecorateRegistration> closed = new();
		List<OpenDecorateRegistration> open = new();
		int declarationOrder = 0;
		foreach ((AttributeData attribute, Location? fallbackLocation) in AttributesOf(containerSymbol, modules))
		{
			if (attribute.AttributeClass is not { Name: "DecorateAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
			{
				continue;
			}

			int order = 0;
			foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
			{
				if (argument.Key == "Order" && argument.Value.Value is int value)
				{
					order = value;
				}
			}

			Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallbackLocation;
			if (!TryReadDecoratorKeys(attribute, attributeClass, LocationInfo.From(location), diagnostics, out IReadOnlyList<string?>? keys))
			{
				declarationOrder++;
				continue;
			}

			if (attributeClass.IsGenericType)
			{
				if (attributeClass.TypeArguments.Length == 2
				    && attributeClass.TypeArguments[0] is INamedTypeSymbol decorator
				    && attributeClass.TypeArguments[1] is INamedTypeSymbol service)
				{
					closed.Add(new DecorateRegistration(
						service.ToDisplayString(FullyQualified), service, decorator, order, declarationOrder, location, keys));
				}
			}
			else if (attribute.ConstructorArguments.Length == 2
			         && attribute.ConstructorArguments[0].Value is INamedTypeSymbol openDecorator
			         && attribute.ConstructorArguments[1].Value is INamedTypeSymbol openService
			         && TryReadOpenGenericPair(openDecorator, openService, LocationInfo.From(location), diagnostics) is { } pair)
			{
				open.Add(new OpenDecorateRegistration(pair.Service, pair.Mapped, order, declarationOrder, location, keys));
			}

			// One slot per [Decorate] attribute (well-formed or not), so open and closed decorators keep their
			// relative source order regardless of how many closings the open form later expands to.
			declarationOrder++;
		}

		return (closed, open);
	}

	/// <summary>
	///     Reads the <c>Keys</c> and <c>AllKeys</c> named arguments of a <c>[Decorate]</c> as the selected canonical
	///     keys: unset yields the unkeyed registration only (<c>[null]</c>), <c>AllKeys</c> yields
	///     <see langword="null" />. Returns <see langword="false" /> (having reported AWT211) when both are set, so the
	///     decorator is skipped. An entry of an unsupported key type is reported as AWT170 and left out.
	/// </summary>
	private static bool TryReadDecoratorKeys(
		AttributeData attribute,
		INamedTypeSymbol attributeClass,
		LocationInfo? location,
		List<DiagnosticInfo> diagnostics,
		out IReadOnlyList<string?>? keys)
	{
		bool allKeys = NamedFlag(attribute, "AllKeys");
		TypedConstant? selected = null;
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == "Keys" && !argument.Value.IsNull)
			{
				selected = argument.Value;
			}
		}

		if (selected is null)
		{
			keys = allKeys ? null : new string?[] { null, };
			return true;
		}

		if (allKeys)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.DecoratorKeysWithAllKeys,
				location,
				new EquatableArray<string>([AttributeTypeArgument(attribute, attributeClass, 0), AttributeTypeArgument(attribute, attributeClass, 1),])));
			keys = null;
			return false;
		}

		List<string?> list = new();
		foreach (TypedConstant element in selected.Value.Values)
		{
			if (IsUnsupportedKeyConstant(element))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.UnsupportedKeyType, location,
					new EquatableArray<string>([AttributeTypeArgument(attribute, attributeClass, 0), KeyConstantTypeDisplay(element),])));
				continue;
			}

			string? key = EncodeKeyConstant(element);
			if (!list.Contains(key))
			{
				list.Add(key);
			}
		}

		keys = list;
		return true;
	}

	/// <summary>
	///     The display of a <c>[Decorate]</c>/<c>[Composite]</c> type at <paramref name="index" /> (0 the decorator or
	///     composite, 1 the service), read from the generic form's type arguments or the <c>typeof</c> form's
	///     constructor arguments.
	/// </summary>
	private static string AttributeTypeArgument(AttributeData attribute, INamedTypeSymbol attributeClass, int index)
	{
		ITypeSymbol? type = null;
		if (attributeClass.IsGenericType)
		{
			type = attributeClass.TypeArguments[index];
		}
		else if (attribute.ConstructorArguments.Length == 2)
		{
			type = attribute.ConstructorArguments[index].Value as ITypeSymbol;
		}

		return type is null ? "?" : Display(type.ToDisplayString(FullyQualified));
	}

	/// <summary>
	///     Reads the <c>[Composite]</c> registrations on a container and its modules: the closed
	///     <c>[Composite&lt;TComposite, TService&gt;]</c> form and the open generic
	///     <c>[Composite(typeof(C&lt;&gt;), typeof(IService&lt;&gt;))]</c> form, each carrying the composed service,
	///     the composite and its lifetime (default <see cref="Lifetime.Transient" />). Collected apart from lifetime
	///     registrations because a composite fronts an existing service after coalescing.
	/// </summary>
	private static (List<CompositeRegistration> Closed, List<OpenCompositeRegistration> Open) CollectComposites(
		INamedTypeSymbol containerSymbol, List<ImportedModule> modules, List<DiagnosticInfo> diagnostics)
	{
		List<CompositeRegistration> closed = new();
		List<OpenCompositeRegistration> open = new();
		foreach ((AttributeData attribute, Location? fallbackLocation) in AttributesOf(containerSymbol, modules))
		{
			if (attribute.AttributeClass is not { Name: "CompositeAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
			{
				continue;
			}

			// The Lifetime named argument is the public Awaiten.Lifetime enum whose members line up with the internal
			// Lifetime enum; a boxed enum surfaces as its underlying int. Absent, the composite defaults to
			// transient so it re-materializes its member array per resolve.
			Lifetime lifetime = Lifetime.Transient;
			foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
			{
				if (argument.Key == "Lifetime" && argument.Value.Value is int value)
				{
					lifetime = (Lifetime)value;
				}
			}

			Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallbackLocation;
			ReportUnsupportedRegistrationKey(attribute, LocationInfo.From(location), AttributeTypeArgument(attribute, attributeClass, 0), diagnostics);
			string? key = NamedKeyArgument(attribute);

			if (attributeClass.IsGenericType)
			{
				if (attributeClass.TypeArguments.Length == 2
				    && attributeClass.TypeArguments[0] is INamedTypeSymbol composite
				    && attributeClass.TypeArguments[1] is INamedTypeSymbol service)
				{
					closed.Add(new CompositeRegistration(
						service.ToDisplayString(FullyQualified), service, composite, lifetime, location, key));
				}
			}
			else if (attribute.ConstructorArguments.Length == 2
			         && attribute.ConstructorArguments[0].Value is INamedTypeSymbol openComposite
			         && attribute.ConstructorArguments[1].Value is INamedTypeSymbol openService
			         && TryReadOpenGenericPair(openComposite, openService, LocationInfo.From(location), diagnostics) is { } pair)
			{
				open.Add(new OpenCompositeRegistration(pair.Service, pair.Mapped, lifetime, location, key));
			}
		}

		return (closed, open);
	}

	/// <summary>
	///     One <c>[InjectProperty&lt;TImplementation&gt;(name)]</c> entry on the container: the property of the
	///     implementation to fill, plus the same per-property flags an <c>[Inject]</c> attribute carries. The
	///     container-side counterpart of an <c>[Inject]</c> property, matched to a registration by its
	///     implementation type (see <see cref="CollectInjectProperties" />).
	/// </summary>
	private sealed record InjectPropertyEntry(string PropertyName, bool Optional, bool Deferred, string? Key, LocationInfo? Location);

	/// <summary>
	///     Reads the container's <c>[InjectProperty&lt;TImplementation&gt;(name)]</c> and
	///     <c>[InjectProperty(typeof(...), name)]</c> entries into a map from the implementation's fully-qualified
	///     type to its properties to fill, so <see cref="BuildInstance" /> can look them up by
	///     <c>info.ImplementationType</c> - reaching every construction site of that implementation, including one
	///     brought in by a <c>[Scan]</c>. An unbound generic target is keyed by its definition
	///     (<c>Repository&lt;T&gt;</c>), which <see cref="OpenInjectPropertyKey" /> derives from each closing. Reports
	///     <see cref="Diagnostics.TypeofArgumentNotClosedOrOpen">AWT215</see> for a <c>typeof</c> target that is neither
	///     closed nor an unbound generic definition, and
	///     <see cref="Diagnostics.DuplicateInjectProperty">AWT179</see> for a second entry naming the same property of
	///     the same implementation, keeping only the first.
	/// </summary>
	private static Dictionary<string, List<InjectPropertyEntry>> CollectInjectProperties(
		INamedTypeSymbol containerSymbol, List<DiagnosticInfo> diagnostics)
	{
		Dictionary<string, List<InjectPropertyEntry>> map = new(StringComparer.Ordinal);
		foreach (AttributeData attribute in containerSymbol.GetAttributes())
		{
			if (!TryReadInjectPropertyTarget(attribute, diagnostics, out string? propertyName, out string? implementationType, out LocationInfo? location))
			{
				continue;
			}

			if (!map.TryGetValue(implementationType, out List<InjectPropertyEntry>? entries))
			{
				entries = new List<InjectPropertyEntry>();
				map[implementationType] = entries;
			}

			// AWT170: the Key must be a supported key constant (string, enum or typeof), guarded exactly as on a
			// registration's Key and a [FromKey]; an unsupported one would otherwise be silently dropped to no key.
			ReportUnsupportedRegistrationKey(attribute, location, DisplayInstance(implementationType), diagnostics);

			// AWT179: a property is injected once, so a second entry for it is a copy-paste whose flags would be
			// silently ignored; report it and keep the first.
			if (entries.Any(entry => entry.PropertyName == propertyName))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.DuplicateInjectProperty,
					location,
					new EquatableArray<string>([propertyName, DisplayInstance(implementationType),])));
				continue;
			}

			entries.Add(new InjectPropertyEntry(
				propertyName,
				NamedFlag(attribute, "Optional"),
				NamedFlag(attribute, "Deferred"),
				NamedKeyArgument(attribute),
				location));
		}

		return map;
	}

	/// <summary>
	///     Reads one <c>[InjectProperty]</c> entry and checks its target: the property name and the key of the
	///     implementation it targets, where an unbound generic target is keyed by its definition. Reports
	///     <see cref="Diagnostics.TypeofArgumentNotClosedOrOpen">AWT215</see> for a <c>typeof</c> target that is neither
	///     closed nor an unbound generic definition, and <see cref="Diagnostics.InjectPropertyNotFound">AWT177</see> for a
	///     name that is no settable property of a generic definition. Returns <see langword="false" /> for a rejected
	///     entry and for an attribute that is not an <c>[InjectProperty]</c>.
	/// </summary>
	private static bool TryReadInjectPropertyTarget(
		AttributeData attribute,
		List<DiagnosticInfo> diagnostics,
		[NotNullWhen(true)] out string? propertyName,
		[NotNullWhen(true)] out string? implementationType,
		out LocationInfo? location)
	{
		implementationType = null;
		location = null;
		if (!TryReadInjectProperty(attribute, out ITypeSymbol? target, out propertyName, out bool isTypeofForm))
		{
			return false;
		}

		location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());

		if (isTypeofForm && !IsClosedOrOpenDefinition(target, attribute, "implementation"))
		{
			diagnostics.Add(TypeofArgumentNotClosedOrOpen("InjectProperty", target, location));
			return false;
		}

		if (target is not INamedTypeSymbol implementation)
		{
			return false;
		}

		string type = implementation.IsUnboundGenericType
			? implementation.OriginalDefinition.ToDisplayString(FullyQualified)
			: implementation.ToDisplayString(FullyQualified);

		// AWT177 on the definition: every closing shares its members, so a bad name is reported once at the
		// attribute, even when no closing is constructed, instead of once per closing.
		if (implementation.IsUnboundGenericType
		    && !IsSettableProperty(ResolveNamedInstanceMember(implementation.OriginalDefinition, propertyName)))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.InjectPropertyNotFound,
				location,
				new EquatableArray<string>([propertyName, Display(type),])));
			return false;
		}

		implementationType = type;
		return true;
	}

	/// <summary>
	///     Reads the target type and property name of an <c>[InjectProperty&lt;TImplementation&gt;(name)]</c> or an
	///     <c>[InjectProperty(typeof(...), name)]</c>. The target is <see langword="null" /> when the <c>typeof</c>
	///     form passes <see langword="null" />; <paramref name="isTypeofForm" /> tells the forms apart.
	/// </summary>
	private static bool TryReadInjectProperty(
		AttributeData attribute,
		out ITypeSymbol? implementation,
		[NotNullWhen(true)] out string? propertyName,
		out bool isTypeofForm)
	{
		implementation = null;
		propertyName = null;
		isTypeofForm = false;
		if (attribute.AttributeClass is not { Name: "InjectPropertyAttribute", } attributeClass
		    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
		{
			return false;
		}

		if (attributeClass is { IsGenericType: true, TypeArguments.Length: 1, }
		    && attribute.ConstructorArguments.Length == 1)
		{
			implementation = attributeClass.TypeArguments[0];
			propertyName = attribute.ConstructorArguments[0].Value as string;
		}
		else if (!attributeClass.IsGenericType && attribute.ConstructorArguments.Length == 2)
		{
			implementation = attribute.ConstructorArguments[0].Value as ITypeSymbol;
			propertyName = attribute.ConstructorArguments[1].Value as string;
			isTypeofForm = true;
		}

		return propertyName is not null;
	}

	/// <summary>
	///     Whether the <c>typeof</c> argument for <paramref name="parameterName" /> mixes omitted and given type
	///     arguments, like <c>typeof(Outer&lt;int&gt;.Inner&lt;&gt;)</c> or <c>typeof(Outer&lt;&gt;.Inner&lt;int&gt;)</c>.
	///     Read from syntax because C# rejects such a type (CS7003) but Roslyn still binds it to a symbol that cannot be
	///     told apart from a closed type or an unbound definition reliably.
	/// </summary>
	private static bool IsPartiallyOpenTypeof(AttributeData attribute, string parameterName)
	{
		if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax { ArgumentList: { } arguments, }
		    || arguments.Arguments
			    .Where(argument => argument.NameEquals is null
			                       && (argument.NameColon is null || argument.NameColon.Name.Identifier.Text == parameterName))
			    .Select(argument => argument.Expression)
			    .FirstOrDefault() is not TypeOfExpressionSyntax typeOf)
		{
			return false;
		}

		List<TypeSyntax> typeArguments = typeOf.Type.DescendantNodesAndSelf()
			.OfType<TypeArgumentListSyntax>()
			.SelectMany(list => list.Arguments)
			.ToList();
		return typeArguments.Any(argument => argument is OmittedTypeArgumentSyntax)
		       && typeArguments.Any(argument => argument is not OmittedTypeArgumentSyntax);
	}

	/// <summary>
	///     Whether <paramref name="member" /> is a property <c>[InjectProperty]</c> can name: a non-indexer property
	///     with a set or init accessor. Whether the container can reach that accessor is checked per closing (AWT136).
	/// </summary>
	private static bool IsSettableProperty([NotNullWhen(true)] ISymbol? member)
		=> member is IPropertySymbol { IsIndexer: false, SetMethod: not null, };

	/// <summary>
	///     Reports <see cref="Diagnostics.InjectPropertyImplementationNotRegistered">AWT180</see> for every
	///     <c>[InjectProperty&lt;TImplementation&gt;]</c> entry whose implementation type matches no registration in
	///     <paramref name="implOrder" />, so the entry is never applied (an unregistered type, or an unexpanded
	///     closed generic). Run once every instance is built, the container-side analogue of
	///     <see cref="ReportUnappliedContextualBindings" />. A Factory/Instance-produced implementation is registered
	///     (present in <paramref name="implOrder" />) and is AWT178 at its <c>BuildInstance</c>, so it is not
	///     reported here. The registered set carries each impl's <em>real</em> type (<c>info.Symbol</c>) as well as
	///     its <c>ImplementationType</c>: a decorator chain link's <c>ImplementationType</c> is a synthetic
	///     <c>Type@__dec:…</c> identity, so keying only on that would misreport an entry targeting the decorator type
	///     as unmatched. (Property injection is not applied to a decorator wrapper; see the <c>[InjectProperty]</c>
	///     XML doc.) It also carries each impl's generic definition, the key of an open-generic entry, so such an
	///     entry is unmatched only when no closing of its definition is constructed.
	/// </summary>
	private static void ReportUnmatchedInjectProperties(
		Dictionary<string, List<InjectPropertyEntry>> injectProperties,
		List<ImplInfo> implOrder,
		List<DiagnosticInfo> diagnostics)
	{
		if (injectProperties.Count == 0)
		{
			return;
		}

		HashSet<string> registered = new(StringComparer.Ordinal);
		foreach (ImplInfo info in implOrder)
		{
			registered.Add(info.ImplementationType);
			registered.Add(info.Symbol.ToDisplayString(FullyQualified));
			registered.Add(info.Symbol.OriginalDefinition.ToDisplayString(FullyQualified));
		}

		foreach (KeyValuePair<string, List<InjectPropertyEntry>> implementation in injectProperties)
		{
			if (registered.Contains(implementation.Key))
			{
				continue;
			}

			foreach (InjectPropertyEntry entry in implementation.Value)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.InjectPropertyImplementationNotRegistered,
					entry.Location,
					new EquatableArray<string>([entry.PropertyName, DisplayInstance(implementation.Key),])));
			}
		}
	}

	/// <summary>
	///     Resolves how a registration produces its instance: a <c>Factory</c> method, a pre-built <c>Instance</c>
	///     member, or a constructor when neither is set. Setting both drives AWT110. An explicit empty string is
	///     kept (not treated as absent) so it surfaces as AWT108/AWT109.
	/// </summary>
	private static (ProductionKind Production, string? Member, bool Conflicting) ReadProduction(AttributeData attribute)
	{
		string? factory = NamedArgument(attribute, "Factory");
		string? instanceMember = NamedArgument(attribute, "Instance");
		if (factory is not null && instanceMember is not null)
		{
			return (ProductionKind.Factory, factory, true);
		}

		if (factory is not null)
		{
			return (ProductionKind.Factory, factory, false);
		}

		if (instanceMember is not null)
		{
			return (ProductionKind.Instance, instanceMember, false);
		}

		return (ProductionKind.Constructor, null, false);
	}
}
