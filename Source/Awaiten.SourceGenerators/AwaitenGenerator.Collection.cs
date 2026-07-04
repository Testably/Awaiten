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
		bool importServices,
		List<DiagnosticInfo> diagnostics)
	{
		List<RawRegistration> result = new();

		// Closed services an open registration was required to produce but could not, because the closed type
		// arguments violate the implementation's constraints (AWT126). The emitter suppresses the AWT101 these
		// would otherwise also raise on the consumer's parameter, so one root cause is reported once.
		HashSet<string> constraintRejected = new(StringComparer.Ordinal);

		// Open generic registrations are kept apart: they are not instances themselves, but templates
		// expanded into concrete closed registrations on demand (see ExpandOpenGenerics).
		List<OpenRegistration> open = new();

		CollectLifetimeRegistrations(containerSymbol, result, open, diagnostics, origin: null, fallbackLocation: null);

		// [Import(typeof(Module))] pulls a module's registrations in after the container's own, so the
		// container wins ties and a module's overridable defaults (Default/TryAdd) only fill the gaps it
		// leaves. Resolved one level deep - a module's own [Import] is not followed.
		foreach (ImportedModule module in modules)
		{
			CollectLifetimeRegistrations(module.Symbol, result, open, diagnostics, origin: module.Symbol, fallbackLocation: module.ImportLocation);
		}

		// Assembly scanning contributes overridable registrations for every concrete type assignable to a
		// [Scan] marker. Appended before open generic expansion so scanned implementations seed it - their
		// constructors may require closed generics only an open registration can provide.
		List<RawRegistration> scans = CollectScans(containerSymbol, compilation, diagnostics);
		result.AddRange(scans);

		// Expand open generic registrations: for every closed generic service required from the graph
		// whose open form is registered but which has no concrete registration, synthesize the closed
		// implementation (iterating to a fixpoint over its own generic dependencies).
		if (open.Count > 0)
		{
			ExpandOpenGenerics(result, open, containerSymbol, importServices, diagnostics, constraintRejected);
		}

		// ...then moved back to the end: coalescing is first-wins per service, so the explicit registrations
		// and the closed registrations expansion synthesized from them must precede the overridable scan ones.
		if (scans.Count > 0)
		{
			result.RemoveAll(registration => registration.IsScan);
			result.AddRange(scans);
		}

		return (result, constraintRejected);
	}

	/// <summary>
	///     Reads the <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c> lifetime registrations declared on
	///     a symbol (a container or an imported module) into <paramref name="result" />, and the open generic
	///     <c>typeof</c>-form ones into <paramref name="open" /> for later expansion. A module carries the same
	///     attributes as a container, so a single reader serves both; the <c>Default</c>/<c>TryAdd</c> named
	///     flags mark a registration as an overridable module default (<see cref="RawRegistration.Weak" />).
	///     <paramref name="origin" /> is the imported module being read (<see langword="null" /> for the
	///     container itself), recorded on each registration so a module's <c>Factory</c>/<c>Instance</c>
	///     member resolves against the module rather than the container.
	///     <paramref name="fallbackLocation" /> is the container's <c>[Import]</c> location, used for a
	///     module compiled into a referenced assembly whose attributes have no syntax to point at - its
	///     diagnostics then point at the import instead of having no location at all.
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

			// The non-generic Type-ctor form - [Transient(typeof(Repository<>), typeof(IRepository<>))] -
			// carries open generics that cannot be type arguments. Recorded as an open registration to be
			// expanded into concrete closed registrations on demand.
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

			// Default and TryAdd both mark an overridable default that only fills a gap; Default additionally
			// opts into the AWT148 warning when two Defaults collide with nothing stronger to resolve them.
			bool isDefault = NamedFlag(attribute, "Default");
			bool weak = isDefault || NamedFlag(attribute, "TryAdd");

			Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallbackLocation;
			result.Add(new RawRegistration(
				service.ToDisplayString(FullyQualified),
				implementation.ToDisplayString(FullyQualified),
				lifetime.Value,
				(INamedTypeSymbol)implementation,
				location,
				production,
				productionMember,
				conflictingDirectives,
				NamedArgument(attribute, "Key"),
				service as INamedTypeSymbol,
				Weak: weak,
				IsDefault: isDefault,
				Origin: origin));
		}
	}

	/// <summary>
	///     Reads the modules a container pulls in with <c>[Import(typeof(Module))]</c>, in declaration order,
	///     validating each import as it goes: the target must be a <c>[Module]</c> (AWT149, else it is skipped),
	///     a module's own <c>[Import]</c> is reported as not-followed (AWT150, one level deep), and a module that
	///     declares no registrations is reported as contributing nothing (AWT151). Only the container's own
	///     imports are read; a module's imports are not followed.
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

			// [Import(typeof(Module))] names the module as the attribute's single constructor argument. There
			// is deliberately no generic [Import<TModule>] form: a module must be a static class, and C#
			// forbids a static class as a generic type argument (CS0718).
			INamedTypeSymbol? module = attribute.ConstructorArguments.Length == 1
				? attribute.ConstructorArguments[0].Value as INamedTypeSymbol
				: null;
			if (module is null)
			{
				continue;
			}

			// A module imported more than once contributes its registrations, decorators and composites only
			// once: lifetime registrations coalesce away, but decorators would otherwise double-wrap (the chain
			// builder does not dedup). Skip the redundant import silently, before validation, so its diagnostics
			// are not reported twice either.
			if (!seen.Add(module))
			{
				continue;
			}

			// Every module diagnostic points at the container's [Import] - the line the author actually wrote -
			// rather than at the module declaration, which may live in another file (or, for a module compiled
			// into a referenced assembly, in no source at all).
			Location? importLocation = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
			if (ValidateImportedModule(module, importLocation, diagnostics))
			{
				modules.Add(new ImportedModule(module, importLocation));
			}
		}

		return modules;
	}

	/// <summary>
	///     Validates one <c>[Import(typeof(Module))]</c> target, reporting AWT149-154, and returns whether the
	///     target is importable. A non-<c>[Module]</c> target (AWT149) contributes nothing, so the caller skips
	///     it (<see langword="false" />); the other faults - non-static (AWT152), a nested <c>[Import]</c>
	///     (AWT150), a module-declared <c>[Scan]</c> (AWT154), or no registrations (AWT151) - are reported but
	///     the module is still imported (<see langword="true" />). Every diagnostic defaults to the container's
	///     <c>[Import]</c> location for a module whose attributes carry no syntax of their own.
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
		// skipped and the mistake is surfaced here rather than as a later cascade of missing dependencies.
		if (!HasAwaitenAttribute(moduleAttributes, "ModuleAttribute"))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ImportNotAModule, location, new EquatableArray<string>([moduleName,])));
			return false;
		}

		// AWT152: like a container, a module is a pure definition (registrations plus static factory and
		// instance members) and is never instantiated, so it must be a static class - mirroring AWT116.
		// The module is still imported, so its registrations do not additionally cascade as missing.
		if (!module.IsStatic)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NonStaticModule, location, new EquatableArray<string>([moduleName,])));
		}

		// AWT150: a module's own [Import] is not followed, so it is an error that the nested module's
		// registrations are not pulled in transitively - the container must import the nested module directly.
		if (HasAwaitenAttribute(moduleAttributes, "ImportAttribute"))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NestedModuleImport, location, new EquatableArray<string>([moduleName,])));
		}

		// AWT154: [Scan] sweeps an assembly relative to the container and is not collected from modules,
		// so a module-declared scan would be silently ignored; reject it instead. Reported at the module's
		// own [Scan] attribute when it is in source, else at the container's [Import].
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
	///     <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c> lifetime registration (in either the generic
	///     or the open <c>typeof</c> form), a <c>[Decorate]</c>, a <c>[Composite]</c>, or
	///     <c>[ImportServices]</c>. Used to detect a module that declares nothing to import (AWT151); a
	///     module-declared <c>[Scan]</c> does not count - it is not collected and is its own error (AWT154).
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
	///     The attributes of the container followed by those of its imported modules, in import order - the
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
	///     Reads the <c>[Decorate&lt;TDecorator, TService&gt;]</c> registrations declared on a container and
	///     its imported modules, in declaration order (container first, then modules in import order, so the
	///     container's decorators keep the lower declaration indices). Each carries its declaration index so
	///     equal <c>Order</c> values fall back to declaration order when the chain is built. Collected apart
	///     from the lifetime registrations because a decorator wraps an existing registration after coalescing
	///     rather than introducing a new service.
	/// </summary>
	private static List<DecorateRegistration> CollectDecorators(INamedTypeSymbol containerSymbol, List<ImportedModule> modules)
	{
		List<DecorateRegistration> result = new();
		foreach ((AttributeData attribute, Location? fallbackLocation) in AttributesOf(containerSymbol, modules))
		{
			if (attribute.AttributeClass is not { Name: "DecorateAttribute", IsGenericType: true, TypeArguments.Length: 2, } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace
			    || attributeClass.TypeArguments[0] is not INamedTypeSymbol decorator
			    || attributeClass.TypeArguments[1] is not INamedTypeSymbol service)
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

			result.Add(new DecorateRegistration(
				service.ToDisplayString(FullyQualified),
				service,
				decorator,
				order,
				result.Count,
				attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallbackLocation));
		}

		return result;
	}

	/// <summary>
	///     Reads the <c>[Composite&lt;TComposite, TService&gt;]</c> registrations declared on a container. Each
	///     carries the composed service, the composite implementation and the composite's chosen lifetime
	///     (defaulting to <see cref="Lifetime.Transient" />). Collected apart from the lifetime registrations
	///     because a composite fronts an existing service after coalescing rather than introducing a new one; the
	///     type parameters are ordered composite-first to match <c>[Decorate&lt;TDecorator, TService&gt;]</c> and
	///     the lifetime attributes.
	/// </summary>
	private static List<CompositeRegistration> CollectComposites(INamedTypeSymbol containerSymbol, List<ImportedModule> modules)
	{
		List<CompositeRegistration> result = new();
		foreach ((AttributeData attribute, Location? fallbackLocation) in AttributesOf(containerSymbol, modules))
		{
			if (attribute.AttributeClass is not { Name: "CompositeAttribute", IsGenericType: true, TypeArguments.Length: 2, } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace
			    || attributeClass.TypeArguments[0] is not INamedTypeSymbol composite
			    || attributeClass.TypeArguments[1] is not INamedTypeSymbol service)
			{
				continue;
			}

			// The Lifetime named argument is an AwaitenLifetime enum, whose members line up with the internal
			// Lifetime enum (Singleton, Transient, Scoped); a boxed enum surfaces as its underlying int. Absent,
			// the composite defaults to transient so it re-materializes its member array per resolve.
			Lifetime lifetime = Lifetime.Transient;
			foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
			{
				if (argument.Key == "Lifetime" && argument.Value.Value is int value)
				{
					lifetime = (Lifetime)value;
				}
			}

			result.Add(new CompositeRegistration(
				service.ToDisplayString(FullyQualified),
				service,
				composite,
				lifetime,
				attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallbackLocation));
		}

		return result;
	}

	/// <summary>
	///     Resolves how a registration produces its instance. A <c>Factory</c> argument names a container
	///     method that produces it; an <c>Instance</c> argument names a pre-built container member to
	///     expose. Setting both is contradictory (the returned flag drives AWT110); when only one is set it
	///     selects the production, otherwise the instance is constructed. An explicit empty string is kept
	///     (not treated as absent) so it surfaces as AWT108/AWT109 rather than silently downgrading to a
	///     constructor.
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
