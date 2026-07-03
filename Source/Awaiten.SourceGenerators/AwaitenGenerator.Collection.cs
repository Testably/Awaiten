using Awaiten.SourceGenerators.Entities;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	private static (List<RawRegistration> Raw, HashSet<string> ConstraintRejectedServices) Collect(
		INamedTypeSymbol containerSymbol,
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

		foreach (AttributeData attribute in containerSymbol.GetAttributes())
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
				service as INamedTypeSymbol));
		}

		// Expand open generic registrations: for every closed generic service required from the graph
		// whose open form is registered but which has no concrete registration, synthesize the closed
		// implementation (iterating to a fixpoint over its own generic dependencies).
		if (open.Count > 0)
		{
			ExpandOpenGenerics(result, open, containerSymbol, importServices, diagnostics, constraintRejected);
		}

		return (result, constraintRejected);
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
