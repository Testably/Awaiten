using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     The deepest open generic expansion nests a closed type argument before it is treated as an unbounded
	///     recursion (AWT129) rather than a real dependency. Well beyond any hand-written generic graph, but far
	///     below the point where the synthesized registrations would exhaust memory.
	/// </summary>
	private const int MaxExpansionDepth = 100;

	/// <summary>
	///     A hard ceiling on the total number of closed implementations expansion may synthesize. The depth limit
	///     bounds a linear recursion (<c>Node&lt;T&gt;</c> -&gt; <c>Node&lt;List&lt;T&gt;&gt;</c> -&gt; ...); this also
	///     bounds a branching one that would otherwise explode across breadth before ever reaching that depth.
	/// </summary>
	private const int MaxExpansionCount = 10_000;

	/// <summary>
	///     Reads the <c>typeof</c> arguments of a non-generic lifetime attribute: the implementation and the service,
	///     which defaults to the implementation. Returns <see langword="false" /> when an argument is missing or does
	///     not bind, which the compiler already reports.
	/// </summary>
	private static bool TryReadTypeofPair(AttributeData attribute, out INamedTypeSymbol implementation, out INamedTypeSymbol service)
	{
		implementation = null!;
		service = null!;
		if (attribute.ConstructorArguments.Length < 1
		    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol { TypeKind: not TypeKind.Error, } declaredImplementation)
		{
			return false;
		}

		implementation = declaredImplementation;
		if (attribute.ConstructorArguments.Length < 2)
		{
			service = implementation;
			return true;
		}

		if (attribute.ConstructorArguments[1].Value is not INamedTypeSymbol { TypeKind: not TypeKind.Error, } declaredService)
		{
			return false;
		}

		service = declaredService;
		return true;
	}

	/// <summary>
	///     Whether a closed <c>typeof</c> pair satisfies what the generic form's <c>where TImplementation : class, TService</c>
	///     constraint enforces: a reference type with an identity or implicit reference conversion to the service.
	/// </summary>
	private static bool IsAssignableRegistration(INamedTypeSymbol implementation, INamedTypeSymbol service, Compilation compilation)
	{
		if (!implementation.IsReferenceType)
		{
			return false;
		}

		Conversion conversion = ((CSharpCompilation)compilation).ClassifyConversion(implementation, service);
		return conversion.IsIdentity || (conversion.IsImplicit && conversion.IsReference);
	}

	/// <summary>
	///     Reads an open <c>[Singleton(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c> registration into an
	///     <see cref="OpenRegistration" /> carrying every option the generic form has that can hold per closing. Reports
	///     AWT125/AWT127/AWT128 for a malformed pair, AWT207 for <c>Eager</c> or <c>Instance</c> (each needs a closed
	///     type, and the option is ignored), and AWT208 when the owner declares a method named by <c>Factory</c> but none
	///     with the service's arity (the registration is dropped).
	/// </summary>
	private static void CollectOpenRegistration(
		AttributeData attribute,
		Lifetime lifetime,
		INamedTypeSymbol implementation,
		INamedTypeSymbol service,
		LifetimeCollectionContext context)
	{
		List<DiagnosticInfo> diagnostics = context.Diagnostics;
		Location? liveLocation = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? context.FallbackLocation;
		LocationInfo? location = LocationInfo.From(liveLocation);

		if (TryReadOpenGenericPair(implementation, service, location, diagnostics) is not { } pair)
		{
			return;
		}

		string display = Display(pair.Mapped.ToDisplayString(FullyQualified));
		if (NamedFlag(attribute, "Eager"))
		{
			ReportNeedsClosedType("Eager");
		}

		if (NamedArgument(attribute, "Instance") is not null)
		{
			ReportNeedsClosedType("Instance");
		}

		string? factory = NamedArgument(attribute, "Factory");
		List<IMethodSymbol> factoryMethods = new();
		if (factory is not null)
		{
			List<IMethodSymbol> named = context.Owner.GetMembers(factory)
				.OfType<IMethodSymbol>()
				.Where(method => method.MethodKind == MethodKind.Ordinary)
				.ToList();
			factoryMethods = named.Where(method => method.Arity == pair.Service.Arity).ToList();
			if (named.Count > 0 && factoryMethods.Count == 0)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.GenericFactoryArityMismatch,
					location,
					new EquatableArray<string>([display, factory, DescribeOwner(context.Origin), pair.Service.Arity.ToString(),])));
				return;
			}
		}

		string? key = NamedKeyArgument(attribute);
		string? whenInjectedInto = NamedTypeArgument(attribute, "WhenInjectedInto");
		ReportContextualBindingWithKey(key, whenInjectedInto, pair.Mapped, liveLocation, diagnostics);
		ReportUnsupportedRegistrationKey(attribute, location, display, diagnostics);

		int fallback = NamedFallback(attribute);
		context.Open.Add(new OpenRegistration(pair.Service, pair.Mapped, lifetime, key, location, context.Origin)
		{
			Factory = factory,
			FactoryMethods = factoryMethods,
			Weak = fallback != 0,
			IsDefault = fallback == 1,
			WhenInjectedInto = whenInjectedInto,
			OnActivated = NamedArgument(attribute, "OnActivated"),
			OnRelease = NamedArgument(attribute, "OnRelease"),
			SuppressDisposal = NamedFlag(attribute, "SuppressDisposal"),
		});

		void ReportNeedsClosedType(string option)
			=> diagnostics.Add(new DiagnosticInfo(
				Diagnostics.OpenGenericNeedsClosedType,
				location,
				new EquatableArray<string>([option, display,])));
	}

	/// <summary>
	///     Validates and unbinds an open generic <c>typeof</c> pair shared by the open lifetime, decorator and
	///     composite registrations: the mapped type (the implementation, decorator or composite) and the service it
	///     maps onto. Both must be unbound generics (AWT127); after reducing to their unbound definitions their arity
	///     must match (AWT125); and the mapped type must expose the service with its type parameters in declaration
	///     order (AWT128), so a closed service's type arguments apply onto the mapped type by position. Returns the
	///     two unbound definitions, or <see langword="null" /> when any check fails (having reported it).
	/// </summary>
	private static (INamedTypeSymbol Mapped, INamedTypeSymbol Service)? TryReadOpenGenericPair(
		INamedTypeSymbol mapped,
		INamedTypeSymbol service,
		LocationInfo? location,
		List<DiagnosticInfo> diagnostics)
	{
		// AWT127: the typeof-ctor form exists for open generics and must receive unbound generics
		// (typeof(Repository<>)). A closed generic (typeof(Repository<int>)) would otherwise be silently reduced
		// to its open definition by ConstructedFrom below, dropping the type arguments, and a non-generic type
		// would match no closed service, so reject both and point at the generic attribute form.
		if (!mapped.IsUnboundGenericType || !service.IsUnboundGenericType)
		{
			INamedTypeSymbol offending = mapped.IsUnboundGenericType ? service : mapped;
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.OpenGenericNotUnbound,
				location,
				new EquatableArray<string>([
					AwaitenGenerator.Display(offending.ToDisplayString(FullyQualified)),
				])));
			return null;
		}

		// The original, unbound definition (typeof(Repository<>) is the unbound generic).
		mapped = mapped.ConstructedFrom;
		service = service.ConstructedFrom;

		// AWT125: the mapped type's type parameters must line up with the service's, so a closed service
		// can be re-mapped onto it. v1 matches the open form exactly, so the arities must be equal.
		if (mapped.Arity != service.Arity)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.OpenGenericArityMismatch,
				location,
				new EquatableArray<string>([
					AwaitenGenerator.Display(mapped.ToDisplayString(FullyQualified)),
					AwaitenGenerator.Display(service.ToDisplayString(FullyQualified)),
					mapped.Arity.ToString(),
					service.Arity.ToString(),
				])));
			return null;
		}

		// AWT128: expansion maps the closed service's type arguments onto the mapped type's type parameters
		// positionally, which is only correct when the mapped type exposes the service with its own type
		// parameters in declaration order (Repository<T> : IRepository<T>). A reordered or remapped
		// type (Repository<TKey, TValue> : IRepository<TValue, TKey>) would construct a closed type
		// that does not satisfy the requested service, so reject it rather than emit a broken registration.
		if (!SymbolEqualityComparer.Default.Equals(service, mapped)
		    && !ExposesServiceInOrder(mapped, service))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.OpenGenericServiceRemapped,
				location,
				new EquatableArray<string>([
					AwaitenGenerator.Display(mapped.ToDisplayString(FullyQualified)),
					AwaitenGenerator.Display(service.ToDisplayString(FullyQualified)),
				])));
			return null;
		}

		return (mapped, service);
	}

	/// <summary>
	///     True when <paramref name="implementation" /> declares <paramref name="service" /> (as an interface or
	///     a base type) with the implementation's own type parameters as the service's type arguments, in
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
	///     Expands open generic registrations to a fixpoint. Seeds from every closed generic service required by a
	///     known implementation's constructor, by the injected members of a constructed one (<c>[Inject]</c> and
	///     <c>[InjectProperty]</c>) and by the parameters of its lifecycle hooks, each including a collection element.
	///     For each, it expands every matching open registration in declaration order, constructs the closed
	///     implementation through Roslyn, verifies its constraints (AWT126), and synthesizes a concrete
	///     <see cref="RawRegistration" />. Synthesized registrations coalesce like hand-written duplicates, and each
	///     synthesized impl's own dependencies expand in turn.
	/// </summary>
	private static void ExpandOpenGenerics(
		List<RawRegistration> raw,
		List<OpenRegistration> open,
		ExpansionInputs inputs,
		List<DiagnosticInfo> diagnostics,
		HashSet<string> constraintRejected)
	{
		// Closed services already expanded from the open registrations, expanded once since a single visit
		// synthesizes every matching open registration. An explicitly-registered concrete closed service is
		// not blocked here: its open-expanded siblings coexist as additional collection members, joining the
		// explicit one through ordinary coalescing.
		HashSet<string> expandedServices = new(StringComparer.Ordinal);

		// The open service definitions, so constructor selection recognizes a parameter whose closed generic is
		// expandable on demand as satisfiable. Otherwise the seed would pass over the constructor the emitted
		// container actually resolves (its closed generic is not yet in raw) and never expand that dependency.
		HashSet<INamedTypeSymbol> openServices = new(SymbolEqualityComparer.Default);
		foreach (OpenRegistration registration in open)
		{
			openServices.Add(registration.Service);
		}

		// An overridable default (Fallback.Warn/Silent) whose service key a stronger (or earlier) registration claims
		// is dropped in full by coalescing (not built, not a collection member), so it must not seed the
		// expansion either; otherwise an overridden default's constructor would synthesize (and the container
		// would emit) closed registrations nothing in the surviving graph needs. The drop set is computed by the
		// same precedence encoding coalescing runs on (see DroppedOverridableDefaults); a losing strong or scan
		// registration still seeds, since it stays a collection member and is built.
		HashSet<int> droppedWeak = DroppedOverridableDefaults(raw);

		// The constructors, members, factories and hooks to scan for closed generic dependencies, each carried with
		// the expansion depth that reached it. Seeded from every known surviving registration at depth 0; grows as
		// closed impls are synthesized, each one depth deeper than the closed service that produced it.
		Queue<ExpansionWork> worklist = new();
		ExpansionContext context = new(raw, open, worklist, new HashSet<ISymbol>(SymbolEqualityComparer.Default), inputs, diagnostics, constraintRejected);
		SeedExpansionWorklist(droppedWeak, context);

		while (worklist.Count > 0)
		{
			ExpansionWork work = worklist.Dequeue();
			INamedTypeSymbol impl = work.Implementation;
			int depth = work.Depth;

			// AWT129: a self-growing registration (Node<T> depending on Node<List<T>>) synthesizes an
			// ever-larger closed implementation at every step and would never converge. Once the total count
			// runs away (a branching recursion), abandon expansion entirely; when a single chain is merely too
			// deep, skip that branch but let shallower ones continue, so the generator terminates rather than
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

			foreach (ITypeSymbol required in ScannedServiceTypes(work, openServices, context))
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
	///     Seeds the expansion worklist at depth 0 from every surviving registration, deduped: an overridable
	///     default that coalescing drops in full (see <see cref="DroppedOverridableDefaults" />) is skipped, so
	///     its constructor, members and hooks never synthesize closed registrations the surviving graph does not need.
	///     Only a constructed implementation is property-injected, so only its members are scanned.
	/// </summary>
	private static void SeedExpansionWorklist(HashSet<int> droppedWeak, ExpansionContext context)
	{
		for (int index = 0; index < context.Raw.Count; index++)
		{
			if (droppedWeak.Contains(index))
			{
				continue;
			}

			RawRegistration registration = context.Raw[index];
			ExpansionScan scan = registration.Production == ProductionKind.Constructor
				? ExpansionScan.ConstructedInstance
				: ExpansionScan.Constructor;
			Enqueue(new ExpansionWork(registration.Implementation, null, 0, scan), context);
			EnqueueHooks(registration, 0, context);
		}
	}

	/// <summary>Enqueues <paramref name="work" /> unless its constructor, factory or hook was already enqueued.</summary>
	private static void Enqueue(ExpansionWork work, ExpansionContext context)
	{
		if (context.Seen.Add(work.Method ?? (ISymbol)work.Implementation))
		{
			context.Worklist.Enqueue(work);
		}
	}

	/// <summary>
	///     Enqueues the lifecycle hooks <paramref name="registration" /> names, so their parameters after the instance
	///     seed expansion like a constructor's. Only the overload <see cref="ResolveHook" /> dispatches is scanned: an
	///     unusable or ambiguous hook is an error (AWT164, AWT190, AWT198) that needs no closing.
	/// </summary>
	private static void EnqueueHooks(RawRegistration registration, int depth, ExpansionContext context)
	{
		EnqueueHook(registration, registration.OnActivated, registration.OnActivatedMarkers, depth, context);
		EnqueueHook(registration, registration.OnRelease, registration.OnReleaseMarkers, depth, context);
	}

	private static void EnqueueHook(
		RawRegistration registration,
		string? hookName,
		IReadOnlyList<INamedTypeSymbol>? closedMarkers,
		int depth,
		ExpansionContext context)
	{
		if (hookName is null)
		{
			return;
		}

		(List<IMethodSymbol> matches, List<IReadOnlyList<INamedTypeSymbol>> ambiguousMethods) = CollectHookOverloads(
			registration.Implementation,
			hookName,
			registration.Origin,
			closedMarkers ?? [],
			context.Inputs.ContainerSymbol,
			context.Inputs.Compilation);
		if (matches.Count == 1 && ambiguousMethods.Count == 0)
		{
			Enqueue(new ExpansionWork(registration.Implementation, matches[0], depth, ExpansionScan.Hook), context);
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

		// Dedup by (implementation, key, consumer): two open registrations that produce the same closed implementation
		// under different keys (or contextual consumers) must both survive as distinct registrations.
		HashSet<string> synthesized = new(StringComparer.Ordinal);

		// Strong templates first (OrderBy is stable, so declaration order holds within each group), so a Fallback
		// template can tell below whether a stronger registration already claims this closing.
		foreach (OpenRegistration candidate in context.Open
			         .Where(c => SymbolEqualityComparer.Default.Equals(c.Service, openForm))
			         .OrderBy(c => c.Weak))
		{
			// Map the closed service's type arguments onto the implementation's type parameters and
			// construct the closed implementation (e.g. Repository<> + [Order] -> Repository<Order>).
			INamedTypeSymbol closedImpl = candidate.Implementation.Construct(typeArguments);
			string closedImplName = closedImpl.ToDisplayString(FullyQualified);

			if (!TryAcceptClosing(candidate, closed, closedService, closedImplName, context, out List<IMethodSymbol> factories))
			{
				continue;
			}

			if (!synthesized.Add(closedImplName + "\0" + candidate.Key + "\0" + candidate.WhenInjectedInto))
			{
				continue;
			}

			// A Fallback closing that a stronger (or earlier) registration already claims is dropped by coalescing,
			// so it must not seed expansion either (see DroppedOverridableDefaults for the explicit ones).
			bool dropped = candidate.Weak && IsClaimedByStrongerRegistration(candidate, closedService, context.Raw);

			RawRegistration registration = SynthesizeClosedRegistration(candidate, closed, closedService, closedImpl, closedImplName);
			context.Raw.Add(registration);
			context.Synthesized++;

			if (dropped)
			{
				continue;
			}

			if (candidate.Factory is null)
			{
				Enqueue(new ExpansionWork(closedImpl, null, depth + 1, ExpansionScan.ConstructedInstance), context);
			}

			foreach (IMethodSymbol factory in factories)
			{
				Enqueue(new ExpansionWork(closedImpl, factory.Construct(closed.TypeArguments.ToArray()), depth + 1, ExpansionScan.Factory), context);
			}

			EnqueueHooks(registration, depth + 1, context);
		}
	}

	/// <summary>
	///     Checks that <paramref name="closed" />'s type arguments satisfy the constraints of the candidate's
	///     implementation (AWT126) and of at least one of its generic factories (AWT209), returning the factories that
	///     accept them. A rejected closing records its service so the emitter can suppress the AWT101 the consumer's
	///     now-unregistered parameter would otherwise also raise.
	/// </summary>
	private static bool TryAcceptClosing(
		OpenRegistration candidate,
		INamedTypeSymbol closed,
		string closedService,
		string closedImplName,
		ExpansionContext context,
		out List<IMethodSymbol> factories)
	{
		factories = [];
		if (!ConstraintsSatisfied(candidate.Implementation, closed.TypeArguments.ToArray()))
		{
			context.ConstraintRejectedServices.Add(closedService);
			ReportConstraintViolation(candidate, closedImplName, context);
			return false;
		}

		// A generic factory is called with the closing's type arguments, so they must satisfy its constraints too.
		factories = candidate.FactoryMethods
			.Where(method => SatisfiesConstraints(method, closed.TypeArguments, context.Inputs.Compilation))
			.ToList();
		if (candidate.FactoryMethods.Count > 0 && factories.Count == 0)
		{
			context.ConstraintRejectedServices.Add(closedService);
			ReportFactoryConstraintViolation(candidate, closedService, context);
			return false;
		}

		return true;
	}

	private static bool IsClaimedByStrongerRegistration(OpenRegistration candidate, string closedService, List<RawRegistration> raw)
		=> raw.Any(registration =>
			!registration.IsScan
			&& registration.ServiceType == closedService
			&& registration.Key == candidate.Key
			&& registration.WhenInjectedInto == candidate.WhenInjectedInto);

	private static RawRegistration SynthesizeClosedRegistration(
		OpenRegistration candidate,
		INamedTypeSymbol closed,
		string closedService,
		INamedTypeSymbol closedImpl,
		string closedImplName)
	{
		IReadOnlyList<INamedTypeSymbol> markers = [closed,];
		return new RawRegistration(
			closedService,
			closedImplName,
			candidate.Lifetime,
			closedImpl,
			candidate.Location?.ToLocation(),
			candidate.Factory is null ? ProductionKind.Constructor : ProductionKind.Factory,
			candidate.Factory,
			Key: candidate.Key,
			ServiceSymbol: closed,
			Weak: candidate.Weak,
			IsDefault: candidate.IsDefault,
			Origin: candidate.Origin,
			IsSynthesized: true,
			OnActivated: candidate.OnActivated,
			OnRelease: candidate.OnRelease,
			SuppressDisposal: candidate.SuppressDisposal,
			WhenInjectedInto: candidate.WhenInjectedInto,
			OnActivatedMarkers: candidate.OnActivated is null ? null : markers,
			OnReleaseMarkers: candidate.OnRelease is null ? null : markers,
			GenericFactory: candidate.Factory is not null);
	}

	private static void ReportFactoryConstraintViolation(OpenRegistration candidate, string closedService, ExpansionContext context)
	{
		if (!context.ReportedConstraints.Add("factory:" + closedService + "\0" + candidate.Factory))
		{
			return;
		}

		context.Diagnostics.Add(new DiagnosticInfo(
			Diagnostics.GenericFactoryConstraintViolation,
			candidate.Location,
			new EquatableArray<string>([AwaitenGenerator.Display(closedService), candidate.Factory!,])));
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
	///     The service types one unit of expansion work depends on: a factory's parameters, a hook's parameters after
	///     the instance, or an implementation's constructor, plus its injected members when the container constructs it.
	/// </summary>
	private static IEnumerable<ITypeSymbol> ScannedServiceTypes(ExpansionWork work, HashSet<INamedTypeSymbol> openServices, ExpansionContext context)
	{
		ExpansionInputs inputs = context.Inputs;
		switch (work.Scan)
		{
			case ExpansionScan.Factory:
				return work.Method!.Parameters.Select(parameter => RequiredServiceType(parameter.Type));
			case ExpansionScan.Hook:
				return work.Method!.Parameters.Skip(1).Select(parameter => RequiredServiceType(parameter.Type));
			default:
				IEnumerable<ITypeSymbol> constructorTypes = RequiredServiceTypes(
					work.Implementation, inputs.ContainerSymbol, inputs.Compilation, context.Raw, openServices, inputs.External);
				return work.Scan == ExpansionScan.ConstructedInstance
					? constructorTypes.Concat(InjectedMemberServiceTypes(work.Implementation, inputs.InjectProperties))
					: constructorTypes;
		}
	}

	/// <summary>
	///     The service types the injected members of a container-constructed implementation depend on: its
	///     <c>[Inject]</c> properties and the <c>[InjectProperty]</c> entries targeting it or its generic definition,
	///     unwrapped like constructor parameters. A property named by both is scanned once, and an entry naming no
	///     settable property (AWT177) contributes nothing.
	/// </summary>
	private static IEnumerable<ITypeSymbol> InjectedMemberServiceTypes(
		INamedTypeSymbol implementation,
		Dictionary<string, List<InjectPropertyEntry>> injectProperties)
	{
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (IPropertySymbol property in InjectedProperties(implementation))
		{
			names.Add(property.Name);
			yield return RequiredServiceType(property.Type);
		}

		foreach (string propertyName in InjectPropertyNames(implementation, injectProperties))
		{
			if (names.Add(propertyName)
			    && ResolveNamedInstanceMember(implementation, propertyName) is IPropertySymbol { IsIndexer: false, SetMethod: not null, } property)
			{
				yield return RequiredServiceType(property.Type);
			}
		}
	}

	/// <summary>
	///     The property names of the <c>[InjectProperty]</c> entries that apply to <paramref name="implementation" />:
	///     those keyed by its own type and, for a closing, those keyed by its generic definition.
	/// </summary>
	private static IEnumerable<string> InjectPropertyNames(
		INamedTypeSymbol implementation,
		Dictionary<string, List<InjectPropertyEntry>> injectProperties)
		=> new[] { implementation.ToDisplayString(FullyQualified), implementation.OriginalDefinition.ToDisplayString(FullyQualified), }
			.Distinct(StringComparer.Ordinal)
			.SelectMany(key => injectProperties.TryGetValue(key, out List<InjectPropertyEntry>? entries) ? entries : [])
			.Select(entry => entry.PropertyName);

	/// <summary>
	///     The service types an implementation's constructor depends on, unwrapping relationship and collection
	///     shapes so a closed generic reached through them still seeds expansion. Uses the same
	///     <see cref="AwaitenGenerator.SelectConstructor" /> the container builds through, treating an
	///     open-generic-satisfiable parameter as satisfiable before expansion, so the seed scans exactly the
	///     parameters the emitted container resolves.
	/// </summary>
	private static IEnumerable<ITypeSymbol> RequiredServiceTypes(
		INamedTypeSymbol implementation,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		List<RawRegistration> raw,
		HashSet<INamedTypeSymbol> openServices,
		ExternalSurface external)
	{
		IMethodSymbol? constructor = AwaitenGenerator.SelectConstructor(
			implementation,
			containerSymbol,
			compilation,
			raw.Select(r => r.ServiceType),
			external,
			p => IsOpenGenericSatisfiable(p.Type, openServices));

		return constructor is null
			? []
			: constructor.Parameters.Select(p => RequiredServiceType(p.Type));
	}

	/// <summary>
	///     The single service type a constructor parameter resolves, unwrapping one <c>Task</c>/<c>ValueTask</c>
	///     layer, a collection element (<c>T[]</c> / <c>IEnumerable&lt;T&gt;</c> and friends), and a
	///     <c>Lazy&lt;T&gt;</c> / <c>Func&lt;TArg…, T&gt;</c> relationship, so a closed generic reached through
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
	///     Verifies that <paramref name="typeArguments" /> satisfy the type-parameter constraints of open generic
	///     <paramref name="definition" /> (reference/value-type/unmanaged kinds, <c>new()</c>, and each base/interface
	///     constraint), so the closed construction is legal C#. A constraint mentioning a type parameter is checked
	///     after substitution; one that cannot be represented after substitution is skipped rather than failed.
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
	///     <paramref name="substitution" />, so <c>IComparable&lt;T&gt;</c> becomes <c>IComparable&lt;Order&gt;</c>
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
	///     True when <paramref name="argument" /> is the constraint type itself, derives from it, or implements
	///     it, as a base/interface type-parameter constraint requires.
	/// </summary>
	private static bool IsAssignableTo(ITypeSymbol argument, ITypeSymbol constraintType)
		=> SymbolEqualityComparer.Default.Equals(argument, constraintType)
		   || BaseTypes(argument).Any(baseType => SymbolEqualityComparer.Default.Equals(baseType, constraintType))
		   || argument.AllInterfaces.Any(@interface => SymbolEqualityComparer.Default.Equals(@interface, constraintType));

	/// <summary>
	///     An open generic registration template
	///     (<c>[Transient(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c>) holding the unbound
	///     service and implementation definitions. Not an instance itself; expanded into concrete closed
	///     <see cref="RawRegistration" />s on demand by <see cref="ExpandOpenGenerics" />. <see cref="Origin" />
	///     is the imported module that declared the template (<see langword="null" /> for the container's own),
	///     stamped onto every closed registration expanded from it, like the per-closing options below.
	/// </summary>
	private sealed record OpenRegistration(INamedTypeSymbol Service, INamedTypeSymbol Implementation, Lifetime Lifetime, string? Key, LocationInfo? Location, INamedTypeSymbol? Origin)
	{
		/// <summary>The name of the generic factory method closed per closing, or <see langword="null" /> to construct.</summary>
		public string? Factory { get; init; }

		/// <summary>
		///     The owner's methods named <see cref="Factory" /> with the service's arity, checked against each closing's
		///     type arguments (AWT209) before the closing is synthesized. Empty when no such method exists, which
		///     <c>ResolveFactory</c> then reports per closing (AWT108).
		/// </summary>
		public IReadOnlyList<IMethodSymbol> FactoryMethods { get; init; } = [];

		/// <summary>An overridable default (<c>Fallback.Warn</c>/<c>Fallback.Silent</c>), applying per closing.</summary>
		public bool Weak { get; init; }

		/// <summary><c>Fallback.Warn</c>, which reports AWT148 when two defaults collide on a closing.</summary>
		public bool IsDefault { get; init; }

		public string? WhenInjectedInto { get; init; }

		public string? OnActivated { get; init; }

		public string? OnRelease { get; init; }

		public bool SuppressDisposal { get; init; }
	}

	/// <summary>What one unit of expansion work scans for the closed generic services it requires.</summary>
	private enum ExpansionScan
	{
		/// <summary>The implementation's constructor.</summary>
		Constructor,

		/// <summary>The constructor and the injected members of an implementation the container constructs.</summary>
		ConstructedInstance,

		/// <summary>The parameters of the closed generic factory that produces the implementation.</summary>
		Factory,

		/// <summary>The parameters after the instance of a lifecycle hook.</summary>
		Hook,
	}

	/// <summary>
	///     One unit of expansion work: the implementation it belongs to, the closed factory or hook method that
	///     <see cref="Scan" /> names (<see langword="null" /> for a constructor), and the expansion depth that reached it.
	/// </summary>
	private readonly record struct ExpansionWork(INamedTypeSymbol Implementation, IMethodSymbol? Method, int Depth, ExpansionScan Scan);

	/// <summary>
	///     The container-wide inputs open generic expansion reads but never changes: the container, the compilation,
	///     the external-resolution surface constructor selection honours, and the <c>[InjectProperty]</c> entries whose
	///     properties seed expansion like constructor parameters.
	/// </summary>
	private sealed record ExpansionInputs(
		INamedTypeSymbol ContainerSymbol,
		Compilation Compilation,
		ExternalSurface External,
		Dictionary<string, List<InjectPropertyEntry>> InjectProperties);

	/// <summary>
	///     The mutable state threaded through open generic expansion: the growing registration list and worklist,
	///     the constructors, factories and hooks already enqueued, and the diagnostics sink.
	///     <see cref="ReportedConstraints" /> also dedups the depth-limit report (AWT129) under a <c>depth:</c> prefix
	///     so each cause is reported once.
	/// </summary>
	private sealed record ExpansionContext(
		List<RawRegistration> Raw,
		List<OpenRegistration> Open,
		Queue<ExpansionWork> Worklist,
		HashSet<ISymbol> Seen,
		ExpansionInputs Inputs,
		List<DiagnosticInfo> Diagnostics,
		HashSet<string> ConstraintRejectedServices)
	{
		public HashSet<string> ReportedConstraints { get; } = new(StringComparer.Ordinal);

		/// <summary>The number of closed implementations synthesized so far, bounded by <c>MaxExpansionCount</c>.</summary>
		public int Synthesized { get; set; }
	}
}
