using System.Collections.Immutable;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

/// <summary>
///     Reads the lifetime registrations declared by the Awaiten lifetime attributes on a container, used by
///     the <see cref="AwaitenGenerator" /> to build and emit the container.
/// </summary>
internal static class ContainerRegistrations
{
	public const string AttributeNamespace = "Awaiten";

	private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

	public static List<RawRegistration> Collect(INamedTypeSymbol containerSymbol, List<DiagnosticInfo> diagnostics)
	{
		List<RawRegistration> result = new();

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
				NamedArgument(attribute, "Key")));
		}

		// Expand open generic registrations: for every closed generic service required from the graph
		// whose open form is registered but which has no concrete registration, synthesize the closed
		// implementation (iterating to a fixpoint over its own generic dependencies).
		if (open.Count > 0)
		{
			ExpandOpenGenerics(result, open, diagnostics);
		}

		return result;
	}

	/// <summary>
	///     Reads the <c>[Decorate&lt;TDecorator, TService&gt;]</c> registrations declared on a container, in
	///     declaration order. Each carries its declaration index so equal <c>Order</c> values fall back to
	///     declaration order when the chain is built. Collected apart from the lifetime registrations because a
	///     decorator wraps an existing registration after coalescing rather than introducing a new service.
	/// </summary>
	public static List<DecorateRegistration> CollectDecorators(INamedTypeSymbol containerSymbol)
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
	///     Reads a non-generic <c>[Singleton(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c>
	///     registration into an <see cref="OpenRegistration" />. Reports AWT125 when the implementation and
	///     service have mismatched arity, since no closed service can then be mapped onto the implementation.
	/// </summary>
	private static void CollectOpenRegistration(
		AttributeData attribute,
		Lifetime lifetime,
		List<OpenRegistration> open,
		List<DiagnosticInfo> diagnostics)
	{
		if (attribute.ConstructorArguments.Length < 1
		    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol implementation)
		{
			return;
		}

		INamedTypeSymbol service = attribute.ConstructorArguments.Length > 1
		                           && attribute.ConstructorArguments[1].Value is INamedTypeSymbol declaredService
			? declaredService
			: implementation;

		// The original, unbound definition (typeof(Repository<>) is the unbound generic).
		implementation = implementation.ConstructedFrom;
		service = service.ConstructedFrom;
		LocationInfo? location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());

		// AWT125: the implementation's type parameters must line up with the service's, so a closed service
		// can be re-mapped onto the implementation. v1 matches the open form exactly, so the arities must be equal.
		if (implementation.Arity != service.Arity)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.OpenGenericArityMismatch,
				location,
				new EquatableArray<string>([
					AwaitenGenerator.Display(implementation.ToDisplayString(FullyQualified)),
					AwaitenGenerator.Display(service.ToDisplayString(FullyQualified)),
					implementation.Arity.ToString(),
					service.Arity.ToString(),
				])));
			return;
		}

		open.Add(new OpenRegistration(service, implementation, lifetime, location));
	}

	/// <summary>
	///     The open generic expansion worklist. Seeds from every closed generic service required by an
	///     already-known implementation's constructor whose open form is registered but which has no concrete
	///     registration; constructs the matching closed implementation through Roslyn symbol construction,
	///     verifies its type-parameter constraints (AWT126), and synthesizes a concrete <see cref="RawRegistration" />.
	///     The synthesized implementation's own constructor may reference further closed generics, so the
	///     worklist iterates to a fixpoint.
	/// </summary>
	private static void ExpandOpenGenerics(
		List<RawRegistration> raw,
		List<OpenRegistration> open,
		List<DiagnosticInfo> diagnostics)
	{
		// Already-satisfied closed services (concrete registrations) - never re-synthesized; the explicit
		// registration wins. Synthesized closed impls are also tracked so a service is expanded only once.
		HashSet<string> registeredServices = new(StringComparer.Ordinal);
		foreach (RawRegistration registration in raw)
		{
			registeredServices.Add(registration.ServiceType);
		}

		// The constructor parameters to scan for closed generic dependencies. Seeded from every known
		// implementation; grows as closed impls are synthesized.
		Queue<INamedTypeSymbol> worklist = new();
		HashSet<INamedTypeSymbol> seen = new(SymbolEqualityComparer.Default);
		foreach (RawRegistration registration in raw)
		{
			if (seen.Add(registration.Implementation))
			{
				worklist.Enqueue(registration.Implementation);
			}
		}

		HashSet<string> reportedConstraints = new(StringComparer.Ordinal);

		while (worklist.Count > 0)
		{
			INamedTypeSymbol impl = worklist.Dequeue();
			foreach (ITypeSymbol required in RequiredServiceTypes(impl))
			{
				if (required is not INamedTypeSymbol { IsGenericType: true, } closed
				    || closed.IsUnboundGenericType)
				{
					continue;
				}

				string closedService = closed.ToDisplayString(FullyQualified);
				if (registeredServices.Contains(closedService))
				{
					continue;
				}

				// Match the closed service's open form against an open registration (exact arity, v1).
				INamedTypeSymbol openForm = closed.ConstructedFrom;
				OpenRegistration? match = null;
				foreach (OpenRegistration candidate in open)
				{
					if (SymbolEqualityComparer.Default.Equals(candidate.Service, openForm))
					{
						match = candidate;
						break;
					}
				}

				if (match is null)
				{
					continue;
				}

				// Map the closed service's type arguments onto the implementation's type parameters and
				// construct the closed implementation (e.g. Repository<> + [Order] -> Repository<Order>).
				ITypeSymbol[] typeArguments = closed.TypeArguments.ToArray();
				INamedTypeSymbol closedImpl = match.Implementation.Construct(typeArguments);

				// AWT126: the closed type arguments must satisfy the implementation's constraints
				// (e.g. Repository<int> against where T : class).
				if (!ConstraintsSatisfied(match.Implementation, typeArguments))
				{
					if (reportedConstraints.Add(closedService))
					{
						diagnostics.Add(new DiagnosticInfo(
							Diagnostics.OpenGenericConstraintViolation,
							match.Location,
							new EquatableArray<string>([
								AwaitenGenerator.Display(closedImpl.ToDisplayString(FullyQualified)),
								AwaitenGenerator.Display(match.Implementation.ToDisplayString(FullyQualified)),
							])));
					}

					registeredServices.Add(closedService);
					continue;
				}

				registeredServices.Add(closedService);
				raw.Add(new RawRegistration(
					closedService,
					closedImpl.ToDisplayString(FullyQualified),
					match.Lifetime,
					closedImpl,
					match.Location?.ToLocation()));

				if (seen.Add(closedImpl))
				{
					worklist.Enqueue(closedImpl);
				}
			}
		}
	}

	/// <summary>
	///     The service types an implementation's (greediest public) constructor depends on, unwrapping the
	///     relationship and collection shapes so a closed generic reached through <c>Func&lt;T&gt;</c>,
	///     <c>Lazy&lt;T&gt;</c> or <c>IEnumerable&lt;T&gt;</c> still seeds open generic expansion.
	/// </summary>
	private static IEnumerable<ITypeSymbol> RequiredServiceTypes(INamedTypeSymbol implementation)
	{
		IMethodSymbol? constructor = implementation.InstanceConstructors
			.Where(c => c.DeclaredAccessibility == Accessibility.Public)
			.OrderByDescending(c => c.Parameters.Length)
			.FirstOrDefault();
		if (constructor is null)
		{
			yield break;
		}

		foreach (IParameterSymbol parameter in constructor.Parameters)
		{
			ITypeSymbol type = parameter.Type;

			// Unwrap a collection element (T[] or IEnumerable<T> and friends).
			if (type is IArrayTypeSymbol array)
			{
				yield return array.ElementType;
				continue;
			}

			if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } generic)
			{
				string? container = generic.ContainingNamespace?.ToDisplayString();
				if (container == "System.Collections.Generic"
				    && generic.Name is "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection")
				{
					yield return generic.TypeArguments[0];
					continue;
				}

				// Unwrap a relationship type (Func<T> / Lazy<T>).
				if (container == "System" && generic.Name == "Lazy")
				{
					yield return generic.TypeArguments[0];
					continue;
				}
			}

			// A Func<TArg…, T> resolves its last type argument.
			if (type is INamedTypeSymbol { IsGenericType: true, Name: "Func", } func
			    && func.ContainingNamespace?.ToDisplayString() == "System"
			    && func.TypeArguments.Length >= 1)
			{
				yield return func.TypeArguments[func.TypeArguments.Length - 1];
				continue;
			}

			yield return type;
		}
	}

	/// <summary>
	///     Verifies that <paramref name="typeArguments" /> satisfy the type-parameter constraints of an open
	///     generic <paramref name="definition" /> - the reference/value-type and unmanaged kind constraints,
	///     <c>new()</c>, and each declared base/interface constraint - so the closed construction is legal
	///     C#. v1 covers the constraints expressible without recursive type-parameter substitution.
	/// </summary>
	private static bool ConstraintsSatisfied(INamedTypeSymbol definition, ITypeSymbol[] typeArguments)
	{
		ImmutableArray<ITypeParameterSymbol> parameters = definition.TypeParameters;
		for (int i = 0; i < parameters.Length && i < typeArguments.Length; i++)
		{
			ITypeParameterSymbol parameter = parameters[i];
			ITypeSymbol argument = typeArguments[i];

			if (parameter.HasReferenceTypeConstraint && !argument.IsReferenceType)
			{
				return false;
			}

			if (parameter.HasValueTypeConstraint && (!argument.IsValueType || IsNullableValueType(argument)))
			{
				return false;
			}

			if (parameter.HasUnmanagedTypeConstraint && !argument.IsUnmanagedType)
			{
				return false;
			}

			if (parameter.HasConstructorConstraint && !HasAccessibleParameterlessConstructor(argument))
			{
				return false;
			}

			foreach (ITypeSymbol constraintType in parameter.ConstraintTypes)
			{
				// A constraint that is itself a type parameter (e.g. where T : U) is not checked in v1.
				if (constraintType is ITypeParameterSymbol)
				{
					continue;
				}

				if (!IsAssignableTo(argument, constraintType))
				{
					return false;
				}
			}
		}

		return true;
	}

	private static bool IsNullableValueType(ITypeSymbol type)
		=> type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, };

	private static bool HasAccessibleParameterlessConstructor(ITypeSymbol type)
	{
		if (type is not INamedTypeSymbol named)
		{
			return type.IsValueType;
		}

		if (named.IsValueType)
		{
			return true;
		}

		if (named.IsAbstract)
		{
			return false;
		}

		foreach (IMethodSymbol constructor in named.InstanceConstructors)
		{
			if (constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	///     True when <paramref name="argument" /> is the constraint type itself, derives from it, or
	///     implements it - the assignment a base/interface type-parameter constraint requires.
	/// </summary>
	private static bool IsAssignableTo(ITypeSymbol argument, ITypeSymbol constraintType)
	{
		for (ITypeSymbol? current = argument; current is not null; current = current.BaseType)
		{
			if (SymbolEqualityComparer.Default.Equals(current, constraintType))
			{
				return true;
			}
		}

		foreach (INamedTypeSymbol @interface in argument.AllInterfaces)
		{
			if (SymbolEqualityComparer.Default.Equals(@interface, constraintType))
			{
				return true;
			}
		}

		return false;
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

	private static string? NamedArgument(AttributeData attribute, string name)
	{
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == name && argument.Value.Value is string value)
			{
				return value;
			}
		}

		return null;
	}

	private static void ImmutableArrayGuard(
		ImmutableArray<ITypeSymbol> typeArguments,
		out ITypeSymbol? implementation,
		out ITypeSymbol? service)
	{
		implementation = typeArguments.Length > 0 ? typeArguments[0] : null;
		service = typeArguments.Length > 1 ? typeArguments[1] : null;
		if (implementation is not INamedTypeSymbol)
		{
			implementation = null;
		}
	}

	/// <summary>
	///     The members named <paramref name="name" /> that the generated container partial can reach: the
	///     container's own members (any accessibility, since a partial can use its own private members)
	///     plus members inherited from base types that a derived type can actually access (everything but
	///     private, with internal / private-protected restricted to the same assembly).
	/// </summary>
	public static IEnumerable<ISymbol> AccessibleMembers(INamedTypeSymbol container, string name)
	{
		foreach (ISymbol member in container.GetMembers(name))
		{
			yield return member;
		}

		for (INamedTypeSymbol? baseType = container.BaseType; baseType is not null; baseType = baseType.BaseType)
		{
			foreach (ISymbol member in baseType.GetMembers(name).Where(m => IsAccessibleFromDerived(m, container)))
			{
				yield return member;
			}
		}

		static bool IsAccessibleFromDerived(ISymbol member, INamedTypeSymbol container)
			=> member.DeclaredAccessibility switch
			{
				Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal => true,
				Accessibility.Internal or Accessibility.ProtectedAndInternal =>
					SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, container.ContainingAssembly),
				_ => false,
			};
	}

	/// <summary>
	///     The ordinary methods named <paramref name="name" /> on the container (or an accessible base
	///     type) whose return type produces <paramref name="serviceType" /> - the candidate factory methods
	///     for a <c>Factory</c> registration. A synchronous factory's return type is implicitly convertible to
	///     the service type; an asynchronous factory returns <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c>
	///     and is matched against the unwrapped <c>T</c> (the container awaits it). None means AWT108; more
	///     than one means an ambiguous factory (AWT112).
	/// </summary>
	public static List<IMethodSymbol> FindFactoryCandidates(
		INamedTypeSymbol container, string name, ITypeSymbol serviceType, Compilation compilation)
	{
		List<IMethodSymbol> candidates = new();
		foreach (ISymbol member in AccessibleMembers(container, name))
		{
			if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, } method
			    && compilation.HasImplicitConversion(ProducedType(method.ReturnType, compilation), serviceType))
			{
				candidates.Add(method);
			}
		}

		return candidates;
	}

	/// <summary>
	///     The service type a factory's return type produces: the awaited result <c>T</c> for an asynchronous
	///     factory returning <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c>, otherwise the return type
	///     itself. A non-generic <c>Task</c> / <c>ValueTask</c> (no result) is not unwrapped, so it is matched
	///     as-is and falls out as AWT108 (it produces no service).
	/// </summary>
	public static ITypeSymbol ProducedType(ITypeSymbol returnType, Compilation compilation)
		=> IsAsyncFactoryReturn(returnType, compilation, out ITypeSymbol produced) ? produced : returnType;

	/// <summary>
	///     Whether <paramref name="returnType" /> is an awaitable factory return - <c>Task&lt;T&gt;</c> or
	///     <c>ValueTask&lt;T&gt;</c> - yielding the produced result type <c>T</c>. Matched by the canonical
	///     metadata symbols so a user-defined <c>Task`1</c> in another namespace is not mistaken for one.
	///     <c>ValueTask&lt;T&gt;</c> is absent on netstandard2.0; <see cref="Compilation.GetTypeByMetadataName" />
	///     returns <see langword="null" /> there and that branch is simply skipped.
	/// </summary>
	public static bool IsAsyncFactoryReturn(ITypeSymbol returnType, Compilation compilation, out ITypeSymbol produced)
	{
		if (returnType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } named)
		{
			INamedTypeSymbol definition = named.ConstructedFrom;
			INamedTypeSymbol? task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
			INamedTypeSymbol? valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
			if (SymbolEqualityComparer.Default.Equals(definition, task)
			    || (valueTask is not null && SymbolEqualityComparer.Default.Equals(definition, valueTask)))
			{
				produced = named.TypeArguments[0];
				return true;
			}
		}

		produced = returnType;
		return false;
	}

	/// <summary>
	///     An open generic registration template - <c>[Transient(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c>
	///     - holding the unbound service and implementation definitions. Not an instance itself; expanded
	///     into concrete closed <see cref="RawRegistration" />s on demand by <see cref="ExpandOpenGenerics" />.
	/// </summary>
	private sealed record OpenRegistration(INamedTypeSymbol Service, INamedTypeSymbol Implementation, Lifetime Lifetime, LocationInfo? Location);
}
