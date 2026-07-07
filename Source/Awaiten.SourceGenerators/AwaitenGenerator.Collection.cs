using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	private static (List<RawRegistration> Raw, HashSet<string> ConstraintRejectedServices) Collect(
		INamedTypeSymbol containerSymbol,
		List<ImportedModule> modules,
		Compilation compilation,
		ExternalSurface external,
		List<DiagnosticInfo> diagnostics)
	{
		List<RawRegistration> result = new();

		// Closed services an open registration could not produce because the closed type arguments violate the
		// implementation's constraints (AWT126). The emitter suppresses the AWT101 these would also raise.
		HashSet<string> constraintRejected = new(StringComparer.Ordinal);

		// Open generic registrations are kept apart: not instances themselves, but templates expanded into
		// concrete closed registrations on demand (see ExpandOpenGenerics).
		List<OpenRegistration> open = new();

		CollectLifetimeRegistrations(containerSymbol, result, open, diagnostics, origin: null, fallbackLocation: null);

		// [Import(typeof(Module))] pulls a module's registrations in after the container's own, so the container
		// wins ties and a module's overridable defaults (Fallback.Warn/Silent) only fill the gaps it leaves. Resolved
		// one level deep; a module's own [Import] is not followed.
		foreach (ImportedModule module in modules)
		{
			CollectLifetimeRegistrations(module.Symbol, result, open, diagnostics, origin: module.Symbol, fallbackLocation: module.ImportLocation);
		}

		// Assembly scanning contributes overridable registrations for every concrete type assignable to a
		// [Scan] marker. Appended before open generic expansion so scanned implementations seed it: their
		// constructors may require closed generics only an open registration can provide.
		List<RawRegistration> scans = CollectScans(containerSymbol, compilation, diagnostics);
		result.AddRange(scans);

		// Expand open generic registrations: for every closed generic service required from the graph
		// whose open form is registered but which has no concrete registration, synthesize the closed
		// implementation (iterating to a fixpoint over its own generic dependencies).
		if (open.Count > 0)
		{
			ExpandOpenGenerics(result, open, containerSymbol, external, diagnostics, constraintRejected);
		}

		// ...then moved back to the end: coalescing is first-wins per service, so the explicit registrations and
		// the closed registrations expanded from them must precede the overridable scan ones.
		if (scans.Count > 0)
		{
			result.RemoveAll(registration => registration.IsScan);
			result.AddRange(scans);
		}

		return (result, constraintRejected);
	}

	/// <summary>
	///     Reads the <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c> registrations declared on a symbol (a
	///     container or imported module) into <paramref name="result" />, and the open generic <c>typeof</c>-form
	///     ones into <paramref name="open" />. <paramref name="origin" /> is the imported module being read
	///     (<see langword="null" /> for the container), recorded so a module's <c>Factory</c>/<c>Instance</c> member
	///     resolves against the module. <paramref name="fallbackLocation" /> is the container's <c>[Import]</c>
	///     location, used for a referenced-assembly module whose attributes have no syntax.
	/// </summary>
	private static void CollectLifetimeRegistrations(
		INamedTypeSymbol symbol,
		List<RawRegistration> result,
		List<OpenRegistration> open,
		List<DiagnosticInfo> diagnostics,
		INamedTypeSymbol? origin,
		Location? fallbackLocation)
	{
		foreach (AttributeData attribute in symbol.GetAttributes())
		{
			if (attribute.AttributeClass is not { } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
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

			// The non-generic Type-ctor form, [Transient(typeof(Repository<>), typeof(IRepository<>))], carries
			// open generics that cannot be type arguments. Recorded as an open registration to be expanded into
			// concrete closed registrations on demand.
			if (!attributeClass.IsGenericType)
			{
				CollectOpenRegistration(attribute, lifetime.Value, open, diagnostics, origin, fallbackLocation);
				continue;
			}

			ImmutableArrayGuard(attributeClass.TypeArguments, out ITypeSymbol? implementation, out ITypeSymbol? service);
			if (implementation is null)
			{
				continue;
			}

			service ??= implementation;

			(ProductionKind production, string? productionMember, bool conflictingDirectives) =
				ReadProduction(attribute);

			// Both overridable-default modes only fill a gap, so both are weak and yield to a stronger or earlier
			// registration. The Warn mode additionally opts into the AWT148 ambiguity warning when two collide
			// with nothing stronger to resolve them; None is a normal strong registration.
			int fallback = NamedFallback(attribute);
			bool weak = fallback != 0;
			bool isDefault = fallback == 1;

			string? key = NamedKeyArgument(attribute);

			// A [Singleton<…>(WhenInjectedInto = typeof(Consumer))] contextual binding; absent (null) on the
			// open-generic Type-ctor form, which exposes no such property. Resolved to the consumer's context key.
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
				// Eager is exposed on [Singleton<…>] alone; a Transient/Scoped attribute has no such property, so
				// this reads false there. BuildInstance honors it only for a singleton lifetime.
				Eager: NamedFlag(attribute, "Eager"),
				OnActivated: NamedArgument(attribute, "OnActivated"),
				OnRelease: NamedArgument(attribute, "OnRelease"),
				WhenInjectedInto: whenInjectedInto));
		}
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
			if (ValidateImportedModule(module, importLocation, diagnostics))
			{
				modules.Add(new ImportedModule(module, importLocation));
			}
		}

		return modules;
	}

	/// <summary>
	///     Validates one <c>[Import(typeof(Module))]</c> target, reporting AWT149-154, and returns whether it is
	///     importable. A non-<c>[Module]</c> target (AWT149) is skipped (<see langword="false" />); the other faults
	///     are reported but still imported (<see langword="true" />): non-static (AWT152), a nested <c>[Import]</c>
	///     (AWT150), a module-declared <c>[Scan]</c> (AWT154), or no registrations (AWT151).
	/// </summary>
	private static bool ValidateImportedModule(
		INamedTypeSymbol module,
		Location? importLocation,
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

		// AWT154: [Scan] sweeps an assembly relative to the container and is not collected from modules, so a
		// module-declared scan would be silently ignored; reject it instead. Reported at the module's own [Scan]
		// when in source, else at the container's [Import].
		if (TryGetAwaitenAttribute(moduleAttributes, "ScanAttribute", out AttributeData? scan))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ScanOnModule,
				LocationInfo.From(scan?.ApplicationSyntaxReference?.GetSyntax().GetLocation()) ?? location,
				new EquatableArray<string>([moduleName,])));
		}

		// AWT151: a module that declares no lifetime registrations imports nothing useful.
		if (!DeclaresAnyRegistration(moduleAttributes))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.EmptyModule, location, new EquatableArray<string>([moduleName,])));
		}

		return true;
	}

	/// <summary>
	///     Whether an attribute list carries anything a module contributes to an importing container: a
	///     <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c> lifetime registration (generic or open
	///     <c>typeof</c> form), a <c>[Decorate]</c>, a <c>[Composite]</c>, or <c>[ImportServices]</c>. Used to
	///     detect a module that declares nothing to import (AWT151); a module-declared <c>[Scan]</c> does not
	///     count, being uncollected and its own error (AWT154).
	/// </summary>
	private static bool DeclaresAnyRegistration(ImmutableArray<AttributeData> attributes)
	{
		foreach (AttributeData attribute in attributes)
		{
			if (attribute.AttributeClass is { } attributeClass
			    && attributeClass.ContainingNamespace?.ToDisplayString() == AttributeNamespace
			    && attributeClass.Name is "SingletonAttribute" or "TransientAttribute" or "ScopedAttribute"
				    or "DecorateAttribute" or "CompositeAttribute" or "ImportServicesAttribute")
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

			if (attributeClass.IsGenericType)
			{
				if (attributeClass.TypeArguments.Length == 2
				    && attributeClass.TypeArguments[0] is INamedTypeSymbol decorator
				    && attributeClass.TypeArguments[1] is INamedTypeSymbol service)
				{
					closed.Add(new DecorateRegistration(
						service.ToDisplayString(FullyQualified), service, decorator, order, declarationOrder, location));
				}
			}
			else if (attribute.ConstructorArguments.Length == 2
			         && attribute.ConstructorArguments[0].Value is INamedTypeSymbol openDecorator
			         && attribute.ConstructorArguments[1].Value is INamedTypeSymbol openService
			         && TryReadOpenGenericPair(openDecorator, openService, LocationInfo.From(location), diagnostics) is { } pair)
			{
				open.Add(new OpenDecorateRegistration(pair.Service, pair.Mapped, order, declarationOrder, location));
			}

			// One slot per [Decorate] attribute (well-formed or not), so open and closed decorators keep their
			// relative source order regardless of how many closings the open form later expands to.
			declarationOrder++;
		}

		return (closed, open);
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

			// The Lifetime named argument is an AwaitenLifetime enum whose members line up with the internal
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

			if (attributeClass.IsGenericType)
			{
				if (attributeClass.TypeArguments.Length == 2
				    && attributeClass.TypeArguments[0] is INamedTypeSymbol composite
				    && attributeClass.TypeArguments[1] is INamedTypeSymbol service)
				{
					closed.Add(new CompositeRegistration(
						service.ToDisplayString(FullyQualified), service, composite, lifetime, location));
				}
			}
			else if (attribute.ConstructorArguments.Length == 2
			         && attribute.ConstructorArguments[0].Value is INamedTypeSymbol openComposite
			         && attribute.ConstructorArguments[1].Value is INamedTypeSymbol openService
			         && TryReadOpenGenericPair(openComposite, openService, LocationInfo.From(location), diagnostics) is { } pair)
			{
				open.Add(new OpenCompositeRegistration(pair.Service, pair.Mapped, lifetime, location));
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
	///     Reads the container's <c>[InjectProperty&lt;TImplementation&gt;(name)]</c> entries into a map from the
	///     implementation's fully-qualified type to its properties to fill, so <see cref="BuildInstance" /> can look
	///     them up by <c>info.ImplementationType</c> - reaching every construction site of that implementation,
	///     including one brought in by a <c>[Scan]</c>. Reports <see cref="Diagnostics.DuplicateInjectProperty">AWT179</see>
	///     for a second entry naming the same property of the same implementation, keeping only the first.
	/// </summary>
	private static Dictionary<string, List<InjectPropertyEntry>> CollectInjectProperties(
		INamedTypeSymbol containerSymbol, List<DiagnosticInfo> diagnostics)
	{
		Dictionary<string, List<InjectPropertyEntry>> map = new(StringComparer.Ordinal);
		foreach (AttributeData attribute in containerSymbol.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "InjectPropertyAttribute", IsGenericType: true, } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace
			    || attributeClass.TypeArguments.Length != 1
			    || attributeClass.TypeArguments[0] is not INamedTypeSymbol implementation
			    || attribute.ConstructorArguments.Length != 1
			    || attribute.ConstructorArguments[0].Value is not string propertyName)
			{
				continue;
			}

			string implementationType = implementation.ToDisplayString(FullyQualified);
			LocationInfo? location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());

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
	///     XML doc.)
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
