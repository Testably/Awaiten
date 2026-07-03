using Awaiten.SourceGenerators.Entities;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	private static (List<RawRegistration> Raw, HashSet<string> ConstraintRejectedServices) Collect(
		INamedTypeSymbol containerSymbol,
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

		CollectLifetimeRegistrations(containerSymbol, result, open, diagnostics);

		// [Import(typeof(Module))] pulls a module's registrations in after the container's own, so the
		// container wins ties and a module's overridable defaults (Default/TryAdd) only fill the gaps it
		// leaves. Resolved one level deep - a module's own [Import] is not followed.
		foreach (INamedTypeSymbol module in CollectImportedModules(containerSymbol))
		{
			CollectLifetimeRegistrations(module, result, open, diagnostics);
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
	/// </summary>
	private static void CollectLifetimeRegistrations(
		INamedTypeSymbol symbol,
		List<RawRegistration> result,
		List<OpenRegistration> open,
		List<DiagnosticInfo> diagnostics)
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
				CollectOpenRegistration(attribute, lifetime.Value, open, diagnostics);
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

			Location? location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
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
				IsDefault: isDefault));
		}
	}

	/// <summary>
	///     Reads the module types a container pulls in with <c>[Import(typeof(Module))]</c>, in declaration
	///     order. Only the container's own imports are read (one level deep); a module's own
	///     <c>[Import]</c> is not followed.
	/// </summary>
	private static List<INamedTypeSymbol> CollectImportedModules(INamedTypeSymbol containerSymbol)
	{
		List<INamedTypeSymbol> modules = new();
		foreach (AttributeData attribute in containerSymbol.GetAttributes())
		{
			if (attribute.AttributeClass is { Name: "ImportAttribute", } attributeClass
			    && attributeClass.ContainingNamespace?.ToDisplayString() == AttributeNamespace
			    && attribute.ConstructorArguments.Length == 1
			    && attribute.ConstructorArguments[0].Value is INamedTypeSymbol module)
			{
				modules.Add(module);
			}
		}

		return modules;
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
	///     Reads the <c>[Decorate&lt;TDecorator, TService&gt;]</c> registrations declared on a container, in
	///     declaration order. Each carries its declaration index so equal <c>Order</c> values fall back to
	///     declaration order when the chain is built. Collected apart from the lifetime registrations because a
	///     decorator wraps an existing registration after coalescing rather than introducing a new service.
	/// </summary>
	private static List<DecorateRegistration> CollectDecorators(INamedTypeSymbol containerSymbol)
	{
		List<DecorateRegistration> result = new();
		foreach (AttributeData attribute in containerSymbol.GetAttributes())
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
				attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()));
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
	private static List<CompositeRegistration> CollectComposites(INamedTypeSymbol containerSymbol)
	{
		List<CompositeRegistration> result = new();
		foreach (AttributeData attribute in containerSymbol.GetAttributes())
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
				attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()));
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
