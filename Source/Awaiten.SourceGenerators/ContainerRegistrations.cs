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
			ExpandOpenGenerics(result, open, containerSymbol, diagnostics);
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

		LocationInfo? location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());

		// AWT127: the typeof-ctor form exists for open generics and must receive unbound generics
		// (typeof(Repository<>)). A closed generic (typeof(Repository<int>)) would otherwise be silently reduced
		// to its open definition by ConstructedFrom below - dropping the type arguments - and a non-generic type
		// would match no closed service, so reject both and point at the generic attribute form.
		if (!implementation.IsUnboundGenericType || !service.IsUnboundGenericType)
		{
			INamedTypeSymbol offending = implementation.IsUnboundGenericType ? service : implementation;
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.OpenGenericNotUnbound,
				location,
				new EquatableArray<string>([
					AwaitenGenerator.Display(offending.ToDisplayString(FullyQualified)),
				])));
			return;
		}

		// The original, unbound definition (typeof(Repository<>) is the unbound generic).
		implementation = implementation.ConstructedFrom;
		service = service.ConstructedFrom;

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

		// AWT128: expansion maps the closed service's type arguments onto the implementation's type parameters
		// positionally, which is only correct when the implementation exposes the service with its own type
		// parameters in declaration order (Repository<T> : IRepository<T>). A reordered or remapped
		// implementation (Repository<TKey, TValue> : IRepository<TValue, TKey>) would construct a closed type
		// that does not satisfy the requested service, so reject it rather than emit a broken registration.
		if (!SymbolEqualityComparer.Default.Equals(service, implementation)
		    && !ExposesServiceInOrder(implementation, service))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.OpenGenericServiceRemapped,
				location,
				new EquatableArray<string>([
					AwaitenGenerator.Display(implementation.ToDisplayString(FullyQualified)),
					AwaitenGenerator.Display(service.ToDisplayString(FullyQualified)),
				])));
			return;
		}

		open.Add(new OpenRegistration(service, implementation, lifetime, location));
	}

	/// <summary>
	///     True when <paramref name="implementation" /> declares <paramref name="service" /> - as an interface or
	///     a base type - with the implementation's own type parameters as the service's type arguments, in
	///     declaration order (<c>Repository&lt;T&gt; : IRepository&lt;T&gt;</c>). This is the shape v1's positional
	///     type-argument mapping requires; a reordered, partially-closed, or otherwise remapped declaration
	///     returns <see langword="false" />.
	/// </summary>
	private static bool ExposesServiceInOrder(INamedTypeSymbol implementation, INamedTypeSymbol service)
	{
		foreach (INamedTypeSymbol @interface in implementation.AllInterfaces)
		{
			if (MapsInOrder(@interface, service, implementation.TypeParameters))
			{
				return true;
			}
		}

		for (INamedTypeSymbol? current = implementation.BaseType; current is not null; current = current.BaseType)
		{
			if (MapsInOrder(current, service, implementation.TypeParameters))
			{
				return true;
			}
		}

		return false;
	}

	private static bool MapsInOrder(
		INamedTypeSymbol candidate,
		INamedTypeSymbol service,
		ImmutableArray<ITypeParameterSymbol> parameters)
	{
		if (!SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, service.OriginalDefinition))
		{
			return false;
		}

		ImmutableArray<ITypeSymbol> arguments = candidate.TypeArguments;
		if (arguments.Length != parameters.Length)
		{
			return false;
		}

		for (int i = 0; i < arguments.Length; i++)
		{
			if (!SymbolEqualityComparer.Default.Equals(arguments[i], parameters[i]))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	///     The open generic expansion worklist (extended for open-generic collections). Seeds from every
	///     closed generic service required by an already-known implementation's constructor - including the
	///     element of a collection parameter (<c>IEnumerable&lt;IHandler&lt;OrderPlaced&gt;&gt;</c> seeds the
	///     closed service <c>IHandler&lt;OrderPlaced&gt;</c>). For each such service it expands <em>every</em>
	///     open registration whose open form matches (not just the first), so a collection of a closed generic
	///     receives one closed implementation per matching open registration in declaration order; constructs
	///     each closed implementation through Roslyn symbol construction, verifies its type-parameter
	///     constraints (AWT126), and synthesizes a concrete <see cref="RawRegistration" /> per match. Synthesized
	///     registrations are ordinary unkeyed registrations from coalescing onward, so the first wins single
	///     dispatch and all of them become collection members exactly like hand-written duplicates. The
	///     synthesized implementations' own constructors may reference further closed generics, so the worklist
	///     iterates to a fixpoint.
	/// </summary>
	private static void ExpandOpenGenerics(
		List<RawRegistration> raw,
		List<OpenRegistration> open,
		INamedTypeSymbol containerSymbol,
		List<DiagnosticInfo> diagnostics)
	{
		// Closed services already expanded from the open registrations - expanded once, since a single visit
		// synthesizes every matching open registration. An explicitly-registered concrete closed service is
		// not blocked here: its open-expanded siblings coexist as additional collection members, joining the
		// explicit one through ordinary coalescing.
		HashSet<string> expandedServices = new(StringComparer.Ordinal);

		// The open service definitions, so constructor selection recognizes a parameter whose closed generic is
		// expandable on demand as satisfiable - otherwise the seed would pass over the constructor the emitted
		// container actually resolves (its closed generic is not yet in raw) and never expand that dependency.
		HashSet<INamedTypeSymbol> openServices = new(SymbolEqualityComparer.Default);
		foreach (OpenRegistration registration in open)
		{
			openServices.Add(registration.Service);
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
			foreach (ITypeSymbol required in RequiredServiceTypes(impl, containerSymbol, raw, openServices))
			{
				if (required is not INamedTypeSymbol { IsGenericType: true, } closed
				    || closed.IsUnboundGenericType)
				{
					continue;
				}

				string closedService = closed.ToDisplayString(FullyQualified);
				if (!expandedServices.Add(closedService))
				{
					continue;
				}

				// Expand every open registration whose open form matches the closed service's open form
				// (exact arity, v1), in declaration order, deduped by closed implementation. A single match
				// yields one closed registration (the single-dispatch case); several matches yield the
				// collection members for a closed-generic collection.
				INamedTypeSymbol openForm = closed.ConstructedFrom;
				ITypeSymbol[] typeArguments = closed.TypeArguments.ToArray();
				HashSet<string> synthesizedImpls = new(StringComparer.Ordinal);
				foreach (OpenRegistration candidate in open)
				{
					if (!SymbolEqualityComparer.Default.Equals(candidate.Service, openForm))
					{
						continue;
					}

					// Map the closed service's type arguments onto the implementation's type parameters and
					// construct the closed implementation (e.g. Repository<> + [Order] -> Repository<Order>).
					INamedTypeSymbol closedImpl = candidate.Implementation.Construct(typeArguments);
					string closedImplName = closedImpl.ToDisplayString(FullyQualified);

					// AWT126: the closed type arguments must satisfy the implementation's constraints
					// (e.g. Repository<int> against where T : class).
					if (!ConstraintsSatisfied(candidate.Implementation, typeArguments))
					{
						if (reportedConstraints.Add(closedImplName))
						{
							diagnostics.Add(new DiagnosticInfo(
								Diagnostics.OpenGenericConstraintViolation,
								candidate.Location,
								new EquatableArray<string>([
									AwaitenGenerator.Display(closedImplName),
									AwaitenGenerator.Display(candidate.Implementation.ToDisplayString(FullyQualified)),
								])));
						}

						continue;
					}

					if (!synthesizedImpls.Add(closedImplName))
					{
						continue;
					}

					raw.Add(new RawRegistration(
						closedService,
						closedImplName,
						candidate.Lifetime,
						closedImpl,
						candidate.Location?.ToLocation()));

					if (seen.Add(closedImpl))
					{
						worklist.Enqueue(closedImpl);
					}
				}
			}
		}
	}

	/// <summary>
	///     The service types an implementation's constructor depends on, unwrapping the relationship and
	///     collection shapes so a closed generic reached through <c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>,
	///     <c>IEnumerable&lt;T&gt;</c> or an awaited collection
	///     (<c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> / <c>ValueTask&lt;T[]&gt;</c>) still seeds open generic
	///     expansion. The constructor is chosen by the same <see cref="AwaitenGenerator.SelectConstructor" /> the
	///     container uses to build the implementation, so the seed scans exactly the parameters the emitted
	///     container resolves - matching its accessible-constructor and resolvable-preference rules rather than a
	///     divergent greediest-public heuristic. A parameter whose closed generic is expandable from an open
	///     registration counts as satisfiable for that selection even before it is expanded, so the seed does not
	///     pass over the constructor the container resolves and fail to expand its dependency.
	/// </summary>
	private static IEnumerable<ITypeSymbol> RequiredServiceTypes(
		INamedTypeSymbol implementation,
		INamedTypeSymbol containerSymbol,
		List<RawRegistration> raw,
		HashSet<INamedTypeSymbol> openServices)
	{
		IMethodSymbol? constructor = AwaitenGenerator.SelectConstructor(
			implementation,
			containerSymbol,
			raw.Select(r => r.ServiceType),
			p => IsOpenGenericSatisfiable(p.Type, openServices));

		return constructor is null
			? []
			: constructor.Parameters.Select(p => RequiredServiceType(p.Type));
	}

	/// <summary>
	///     The single service type a constructor parameter resolves, unwrapping one <c>Task</c>/<c>ValueTask</c>
	///     layer, a collection element (<c>T[]</c> / <c>IEnumerable&lt;T&gt;</c> and friends), and a
	///     <c>Lazy&lt;T&gt;</c> / <c>Func&lt;TArg…, T&gt;</c> relationship - so a closed generic reached through
	///     any of them still seeds open generic expansion, and constructor selection sees the same underlying
	///     service the seed scans.
	/// </summary>
	private static ITypeSymbol RequiredServiceType(ITypeSymbol type)
	{
		// Unwrap one Task<…>/ValueTask<…> layer first, so an awaited single service (Task<IHandler<T>>)
		// or awaited collection (Task<IReadOnlyList<IHandler<T>>>) seeds expansion through the inner type.
		if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } awaitable
		    && awaitable.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks"
		    && awaitable.Name is "Task" or "ValueTask")
		{
			type = awaitable.TypeArguments[0];
		}

		// Unwrap a collection element (T[] or IEnumerable<T> and friends).
		if (type is IArrayTypeSymbol array)
		{
			return array.ElementType;
		}

		if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } generic)
		{
			string? container = generic.ContainingNamespace?.ToDisplayString();
			if (container == "System.Collections.Generic"
			    && generic.Name is "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection")
			{
				return generic.TypeArguments[0];
			}

			// Unwrap a relationship type (Func<T> / Lazy<T>).
			if (container == "System" && generic.Name == "Lazy")
			{
				return generic.TypeArguments[0];
			}
		}

		// A Func<TArg…, T> resolves its last type argument.
		if (type is INamedTypeSymbol { IsGenericType: true, Name: "Func", } func
		    && func.ContainingNamespace?.ToDisplayString() == "System"
		    && func.TypeArguments.Length >= 1)
		{
			return func.TypeArguments[func.TypeArguments.Length - 1];
		}

		return type;
	}

	/// <summary>
	///     Whether a constructor parameter is satisfied by an open generic registration: its underlying service
	///     (through the same unwrapping the seed scans) is a closed generic whose open form is registered, so the
	///     container can construct it on demand. Lets constructor selection prefer the constructor the container
	///     resolves before the closed generic has been expanded into <c>raw</c>.
	/// </summary>
	private static bool IsOpenGenericSatisfiable(ITypeSymbol parameterType, HashSet<INamedTypeSymbol> openServices)
		=> RequiredServiceType(parameterType) is INamedTypeSymbol { IsGenericType: true, IsUnboundGenericType: false, } closed
		   && openServices.Contains(closed.ConstructedFrom);

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
				// A constraint that mentions a type parameter - a bare `where T : U`, or a constructed constraint
				// such as `where T : IEquatable<T>` / `where T : IComparable<T>` - would need type-parameter
				// substitution to check faithfully, which v1 does not perform. Skip it rather than reject a valid
				// argument on a spurious mismatch (the unsubstituted IEquatable<T> never equals IEquatable<Order>).
				if (ReferencesTypeParameter(constraintType))
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

	/// <summary>
	///     True when <paramref name="type" /> is a type parameter or a constructed type that mentions one
	///     (<c>IEquatable&lt;T&gt;</c>, <c>T[]</c>, <c>IDictionary&lt;string, T&gt;</c>). Such a constraint cannot
	///     be checked without substituting the closed type argument, which v1 does not do.
	/// </summary>
	private static bool ReferencesTypeParameter(ITypeSymbol type)
	{
		switch (type)
		{
			case ITypeParameterSymbol:
				return true;
			case IArrayTypeSymbol array:
				return ReferencesTypeParameter(array.ElementType);
			case INamedTypeSymbol named:
				foreach (ITypeSymbol argument in named.TypeArguments)
				{
					if (ReferencesTypeParameter(argument))
					{
						return true;
					}
				}

				return false;
			default:
				return false;
		}
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
