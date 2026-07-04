using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	// The deepest open generic expansion nests a closed type argument before it is treated as an unbounded
	// recursion (AWT129) rather than a real dependency. Well beyond any hand-written generic graph, but far
	// below the point where the synthesized registrations would exhaust memory.
	private const int MaxExpansionDepth = 100;

	// A hard ceiling on the total number of closed implementations expansion may synthesize. The depth limit
	// bounds a linear recursion (Node<T> -> Node<List<T>> -> ...); this also bounds a branching one that would
	// otherwise explode across breadth before ever reaching that depth.
	private const int MaxExpansionCount = 10_000;

	/// <summary>
	///     Reads a non-generic <c>[Singleton(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c>
	///     registration into an <see cref="OpenRegistration" />. Reports AWT125 when the implementation and
	///     service have mismatched arity, since no closed service can then be mapped onto the implementation.
	///     <paramref name="origin" /> and <paramref name="fallbackLocation" /> mirror
	///     <see cref="CollectLifetimeRegistrations" />: the imported module declaring the registration (so
	///     the closed registrations expanded from it keep their module identity, e.g. for AWT155), and the
	///     container's <c>[Import]</c> location for a module read from a referenced assembly whose
	///     attributes carry no syntax of their own.
	/// </summary>
	private static void CollectOpenRegistration(
		AttributeData attribute,
		Lifetime lifetime,
		List<OpenRegistration> open,
		List<DiagnosticInfo> diagnostics,
		INamedTypeSymbol? origin,
		Location? fallbackLocation)
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

		LocationInfo? location = LocationInfo.From(
			attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallbackLocation);

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

		open.Add(new OpenRegistration(service, implementation, lifetime, NamedArgument(attribute, "Key"), location, origin));
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
		ImmutableArray<ITypeParameterSymbol> parameters = implementation.TypeParameters;
		return implementation.AllInterfaces.Any(@interface => MapsInOrder(@interface, service, parameters))
		       || BaseTypes(implementation).Any(baseType => MapsInOrder(baseType, service, parameters));
	}

	/// <summary>The base types of <paramref name="type" />, from its direct base up to (but excluding) <c>object</c>.</summary>
	private static IEnumerable<INamedTypeSymbol> BaseTypes(ITypeSymbol type)
	{
		for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
		{
			yield return current;
		}
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
		bool importServices,
		List<DiagnosticInfo> diagnostics,
		HashSet<string> constraintRejected)
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

		// An overridable default (Default/TryAdd) whose service key a stronger (or earlier) registration
		// claims is dropped in full by coalescing - not built and not a collection member - so it must not
		// seed the expansion either; otherwise an overridden default's constructor would synthesize (and the
		// container would emit) closed registrations nothing in the surviving graph needs. The drop set is
		// computed by the same precedence encoding coalescing runs on (see DroppedOverridableDefaults); a
		// losing strong or scan registration still seeds, since it stays a collection member and is built.
		HashSet<int> droppedWeak = DroppedOverridableDefaults(raw);

		// The constructor parameters to scan for closed generic dependencies, each carried with the expansion
		// depth that reached it. Seeded from every known surviving implementation at depth 0; grows as closed
		// impls are synthesized, each one depth deeper than the closed service that produced it.
		Queue<(INamedTypeSymbol Impl, int Depth)> worklist = new();
		HashSet<INamedTypeSymbol> seen = new(SymbolEqualityComparer.Default);
		SeedExpansionWorklist(raw, droppedWeak, worklist, seen);

		ExpansionContext context = new(raw, open, worklist, seen, diagnostics, constraintRejected);

		while (worklist.Count > 0)
		{
			(INamedTypeSymbol impl, int depth) = worklist.Dequeue();

			// AWT129: a self-growing registration (Node<T> depending on Node<List<T>>) synthesizes an
			// ever-larger closed implementation at every step and would never converge. Once the total count
			// runs away (a branching recursion), abandon expansion entirely; when a single chain is merely too
			// deep, skip that branch but let shallower ones continue - so the generator terminates rather than
			// looping until it exhausts memory.
			if (context.Synthesized > MaxExpansionCount)
			{
				ReportExpansionTooDeep(impl, depth, context);
				break;
			}

			if (depth > MaxExpansionDepth)
			{
				ReportExpansionTooDeep(impl, depth, context);
				continue;
			}

			foreach (ITypeSymbol required in RequiredServiceTypes(impl, containerSymbol, raw, openServices, importServices))
			{
				if (required is INamedTypeSymbol { IsGenericType: true, IsUnboundGenericType: false, } closed
				    && expandedServices.Add(closed.ToDisplayString(FullyQualified)))
				{
					ExpandClosedService(closed, depth, context);
				}
			}
		}
	}

	/// <summary>
	///     Seeds the expansion worklist at depth 0 from every surviving implementation, deduped: an overridable
	///     default that coalescing drops in full (see <see cref="DroppedOverridableDefaults" />) is skipped, so
	///     its constructor never synthesizes closed registrations the surviving graph does not need.
	/// </summary>
	private static void SeedExpansionWorklist(
		List<RawRegistration> raw,
		HashSet<int> droppedWeak,
		Queue<(INamedTypeSymbol Impl, int Depth)> worklist,
		HashSet<INamedTypeSymbol> seen)
	{
		for (int index = 0; index < raw.Count; index++)
		{
			if (!droppedWeak.Contains(index) && seen.Add(raw[index].Implementation))
			{
				worklist.Enqueue((raw[index].Implementation, 0));
			}
		}
	}

	/// <summary>
	///     Synthesizes a closed <see cref="RawRegistration" /> for every open registration whose open form matches
	///     <paramref name="closed" />'s open form (exact arity, v1), in declaration order, deduped by (closed
	///     implementation, key). A single match yields the single-dispatch registration; several yield the members
	///     of a closed-generic collection. Each synthesized implementation is enqueued one depth deeper so its own
	///     closed generic dependencies expand in turn.
	/// </summary>
	private static void ExpandClosedService(INamedTypeSymbol closed, int depth, ExpansionContext context)
	{
		INamedTypeSymbol openForm = closed.ConstructedFrom;
		string closedService = closed.ToDisplayString(FullyQualified);
		ITypeSymbol[] typeArguments = closed.TypeArguments.ToArray();

		// Dedup by (implementation, key): two open registrations that produce the same closed implementation
		// under different keys must both survive as distinct keyed registrations.
		HashSet<string> synthesized = new(StringComparer.Ordinal);

		foreach (OpenRegistration candidate in context.Open.Where(c => SymbolEqualityComparer.Default.Equals(c.Service, openForm)))
		{
			// Map the closed service's type arguments onto the implementation's type parameters and
			// construct the closed implementation (e.g. Repository<> + [Order] -> Repository<Order>).
			INamedTypeSymbol closedImpl = candidate.Implementation.Construct(typeArguments);
			string closedImplName = closedImpl.ToDisplayString(FullyQualified);

			// AWT126: the closed type arguments must satisfy the implementation's constraints
			// (e.g. Repository<int> against where T : class). Record the closed service so the emitter can
			// suppress the AWT101 the consumer's now-unregistered parameter would otherwise also raise.
			if (!ConstraintsSatisfied(candidate.Implementation, typeArguments))
			{
				context.ConstraintRejectedServices.Add(closedService);
				ReportConstraintViolation(candidate, closedImplName, context);
				continue;
			}

			if (!synthesized.Add(closedImplName + "\0" + candidate.Key))
			{
				continue;
			}

			context.Raw.Add(new RawRegistration(
				closedService,
				closedImplName,
				candidate.Lifetime,
				closedImpl,
				candidate.Location?.ToLocation(),
				Key: candidate.Key,
				ServiceSymbol: closed,
				Origin: candidate.Origin,
				IsSynthesized: true));
			context.Synthesized++;

			if (context.Seen.Add(closedImpl))
			{
				context.Worklist.Enqueue((closedImpl, depth + 1));
			}
		}
	}

	private static void ReportConstraintViolation(OpenRegistration candidate, string closedImplName, ExpansionContext context)
	{
		if (!context.ReportedConstraints.Add(closedImplName))
		{
			return;
		}

		context.Diagnostics.Add(new DiagnosticInfo(
			Diagnostics.OpenGenericConstraintViolation,
			candidate.Location,
			new EquatableArray<string>([
				AwaitenGenerator.Display(closedImplName),
				AwaitenGenerator.Display(candidate.Implementation.ToDisplayString(FullyQualified)),
			])));
	}

	private static void ReportExpansionTooDeep(INamedTypeSymbol impl, int depth, ExpansionContext context)
	{
		string implName = impl.ToDisplayString(FullyQualified);
		if (!context.ReportedConstraints.Add("depth:" + implName))
		{
			return;
		}

		context.Diagnostics.Add(new DiagnosticInfo(
			Diagnostics.OpenGenericExpansionTooDeep,
			null,
			new EquatableArray<string>([
				AwaitenGenerator.Display(implName),
				depth.ToString(),
			])));
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
		HashSet<INamedTypeSymbol> openServices,
		bool importServices)
	{
		IMethodSymbol? constructor = AwaitenGenerator.SelectConstructor(
			implementation,
			containerSymbol,
			raw.Select(r => r.ServiceType),
			p => IsOpenGenericSatisfiable(p.Type, openServices),
			importServices);

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
		=> UnwrapElementOrRelationship(UnwrapAwaitable(type));

	/// <summary>
	///     Unwraps one <c>Task&lt;…&gt;</c>/<c>ValueTask&lt;…&gt;</c> layer, so an awaited single service
	///     (<c>Task&lt;IHandler&lt;T&gt;&gt;</c>) or awaited collection (<c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c>)
	///     seeds expansion through the inner type.
	/// </summary>
	private static ITypeSymbol UnwrapAwaitable(ITypeSymbol type)
		=> type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } awaitable
		   && awaitable.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks"
		   && awaitable.Name is "Task" or "ValueTask"
			? awaitable.TypeArguments[0]
			: type;

	/// <summary>
	///     Unwraps a collection element (<c>T[]</c> / <c>IEnumerable&lt;T&gt;</c> and friends) or a
	///     <c>Lazy&lt;T&gt;</c> / <c>Func&lt;TArg…, T&gt;</c> relationship, so a closed generic reached through
	///     any of them still seeds open generic expansion.
	/// </summary>
	private static ITypeSymbol UnwrapElementOrRelationship(ITypeSymbol type)
	{
		if (type is IArrayTypeSymbol array)
		{
			return array.ElementType;
		}

		if (type is not INamedTypeSymbol { IsGenericType: true, } generic)
		{
			return type;
		}

		string? container = generic.ContainingNamespace?.ToDisplayString();
		if (container == "System.Collections.Generic"
		    && generic.TypeArguments.Length == 1
		    && generic.Name is "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection")
		{
			return generic.TypeArguments[0];
		}

		if (container == "System" && generic.Name == "Lazy" && generic.TypeArguments.Length == 1)
		{
			return generic.TypeArguments[0];
		}

		// A Func<TArg…, T> resolves its last type argument.
		if (container == "System" && generic.Name == "Func" && generic.TypeArguments.Length >= 1)
		{
			return generic.TypeArguments[generic.TypeArguments.Length - 1];
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
	///     <c>new()</c>, and each declared base/interface constraint - so the closed construction is legal C#.
	///     A constraint that mentions a type parameter (<c>where T : IComparable&lt;T&gt;</c>, <c>where T : U</c>)
	///     is checked after substituting the closed type arguments into it; a constraint that cannot be
	///     represented after substitution (e.g. one nesting an array as a type argument) is skipped rather than
	///     treated as a violation.
	/// </summary>
	private static bool ConstraintsSatisfied(INamedTypeSymbol definition, ITypeSymbol[] typeArguments)
	{
		ImmutableArray<ITypeParameterSymbol> parameters = definition.TypeParameters;
		int count = Math.Min(parameters.Length, typeArguments.Length);

		Dictionary<ITypeParameterSymbol, ITypeSymbol> substitution = new(SymbolEqualityComparer.Default);
		for (int i = 0; i < count; i++)
		{
			substitution[parameters[i]] = typeArguments[i];
		}

		for (int i = 0; i < count; i++)
		{
			if (!ParameterConstraintSatisfied(parameters[i], typeArguments[i], substitution))
			{
				return false;
			}
		}

		return true;
	}

	private static bool ParameterConstraintSatisfied(
		ITypeParameterSymbol parameter,
		ITypeSymbol argument,
		Dictionary<ITypeParameterSymbol, ITypeSymbol> substitution)
	{
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

		return parameter.ConstraintTypes
			.Select(constraintType => SubstituteConstraint(constraintType, substitution))
			.All(substituted => substituted is null || IsAssignableTo(argument, substituted));
	}

	/// <summary>
	///     Rewrites a constraint type by replacing each type parameter with the closed type argument mapped in
	///     <paramref name="substitution" /> - so <c>IComparable&lt;T&gt;</c> becomes <c>IComparable&lt;Order&gt;</c>
	///     and a bare <c>U</c> becomes its argument. Returns <see langword="null" /> when the constraint cannot be
	///     reconstructed from symbols alone (an unmapped type parameter, or an array/pointer type argument), so the
	///     caller skips that constraint rather than checking a wrong type.
	/// </summary>
	private static ITypeSymbol? SubstituteConstraint(ITypeSymbol type, Dictionary<ITypeParameterSymbol, ITypeSymbol> substitution)
	{
		switch (type)
		{
			case ITypeParameterSymbol parameter:
				return substitution.TryGetValue(parameter, out ITypeSymbol? argument) ? argument : null;

			case INamedTypeSymbol { IsGenericType: true, } generic:
			{
				INamedTypeSymbol definition = generic.OriginalDefinition;
				if (definition.Arity != generic.TypeArguments.Length)
				{
					return null;
				}

				ITypeSymbol[] arguments = new ITypeSymbol[generic.TypeArguments.Length];
				for (int i = 0; i < arguments.Length; i++)
				{
					ITypeSymbol? substituted = SubstituteConstraint(generic.TypeArguments[i], substitution);
					if (substituted is null)
					{
						return null;
					}

					arguments[i] = substituted;
				}

				return definition.Construct(arguments);
			}

			case INamedTypeSymbol named:
				return named;

			default:
				return null;
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

		return named.InstanceConstructors.Any(constructor =>
			constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public);
	}

	/// <summary>
	///     True when <paramref name="argument" /> is the constraint type itself, derives from it, or
	///     implements it - the assignment a base/interface type-parameter constraint requires.
	/// </summary>
	private static bool IsAssignableTo(ITypeSymbol argument, ITypeSymbol constraintType)
		=> SymbolEqualityComparer.Default.Equals(argument, constraintType)
		   || BaseTypes(argument).Any(baseType => SymbolEqualityComparer.Default.Equals(baseType, constraintType))
		   || argument.AllInterfaces.Any(@interface => SymbolEqualityComparer.Default.Equals(@interface, constraintType));

	/// <summary>
	///     An open generic registration template - <c>[Transient(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c>
	///     - holding the unbound service and implementation definitions. Not an instance itself; expanded
	///     into concrete closed <see cref="RawRegistration" />s on demand by <see cref="ExpandOpenGenerics" />.
	///     <see cref="Origin" /> is the imported module that declared the template (<see langword="null" />
	///     for the container's own), stamped onto every closed registration expanded from it.
	/// </summary>
	private sealed record OpenRegistration(INamedTypeSymbol Service, INamedTypeSymbol Implementation, Lifetime Lifetime, string? Key, LocationInfo? Location, INamedTypeSymbol? Origin);

	/// <summary>
	///     The mutable state threaded through open generic expansion: the growing registration list and worklist,
	///     the implementations already enqueued, and the diagnostics sink. <see cref="ReportedConstraints" /> also
	///     dedups the depth-limit report (AWT129) under a <c>depth:</c> prefix so each cause is reported once.
	/// </summary>
	private sealed record ExpansionContext(
		List<RawRegistration> Raw,
		List<OpenRegistration> Open,
		Queue<(INamedTypeSymbol Impl, int Depth)> Worklist,
		HashSet<INamedTypeSymbol> Seen,
		List<DiagnosticInfo> Diagnostics,
		HashSet<string> ConstraintRejectedServices)
	{
		public HashSet<string> ReportedConstraints { get; } = new(StringComparer.Ordinal);

		/// <summary>The number of closed implementations synthesized so far, bounded by <c>MaxExpansionCount</c>.</summary>
		public int Synthesized { get; set; }
	}
}
