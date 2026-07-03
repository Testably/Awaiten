using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     Rewrites a collection parameter to an ordinary direct dependency when its collection shape is claimed by
	///     an explicit registration. A collection type (<c>IEnumerable&lt;T&gt;</c> and friends, <c>T[]</c>, or
	///     <c>IAsyncEnumerable&lt;T&gt;</c>) is normally synthesized from the registrations of its element type under
	///     the parameter's key. But if that collection shape is itself registered as a service under the same key - a
	///     legitimate opaque value such as a <c>string[]</c> of command-line arguments, an
	///     <c>IReadOnlyList&lt;T&gt;</c> of config or an <c>IAsyncEnumerable&lt;T&gt;</c> channel - synthesis steps
	///     aside entirely (all-or-nothing): the registered shape resolves to that opaque value as an ordinary direct
	///     dependency, and an unregistered sibling shape is a plain missing dependency (AWT101) rather than a
	///     silently synthesized second collection that could disagree with the registered one. A registered
	///     synchronous shape claims the whole collection - including the <c>IAsyncEnumerable&lt;T&gt;</c> and awaited
	///     <c>Task&lt;C&gt;</c> views, so injecting either is AWT101 rather than a second collection synthesized
	///     behind the opaque one - mirroring the by-type SynthesisSuppressed gate; a registered
	///     <c>IAsyncEnumerable&lt;T&gt;</c> or <c>Task&lt;C&gt;</c> claims only its own exact shape.
	/// </summary>
	private static ParameterModel SuppressRegisteredCollectionSynthesis(
		ParameterModel parameterModel,
		IParameterSymbol parameter,
		Dictionary<ServiceKey, string> serviceToImpl)
	{
		bool syncShapeRegistered = parameterModel.Kind is (DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable)
		                           && CollectionShapeTypes(parameterModel.ServiceType).Any(shape => serviceToImpl.ContainsKey(new ServiceKey(shape, parameterModel.Key)));
		bool asyncShapeRegistered = parameterModel.Kind == DependencyKind.AsyncEnumerable
		                            && serviceToImpl.ContainsKey(new ServiceKey(AsyncEnumerableShapeType(parameterModel.ServiceType), parameterModel.Key));
		bool awaitedShapeRegistered = parameterModel.Kind == DependencyKind.AwaitedEnumerable
		                              && serviceToImpl.ContainsKey(new ServiceKey(parameter.Type.ToDisplayString(FullyQualified), parameterModel.Key));
		if (syncShapeRegistered || asyncShapeRegistered || awaitedShapeRegistered)
		{
			string collectionType = parameter.Type.ToDisplayString(FullyQualified);
			return parameterModel with { ServiceType = collectionType, Kind = DependencyKind.Direct, AwaitedCollectionType = null, };
		}

		return parameterModel;
	}

	/// <summary>
	///     Classifies the producer's parameters (a constructor's or a factory method's) and reports
	///     <see cref="Diagnostics.MissingDependency">AWT101</see> for any non-<c>[Arg]</c> parameter whose
	///     service type is not registered. A runtime argument (<c>[Arg]</c>) is supplied at resolve time, so
	///     it is never a missing dependency. A service in the context's <c>ConstraintRejected</c> set - an open
	///     generic that could not be closed at the required type argument (AWT126) - is not reported again as
	///     AWT101: the constraint violation is the one root cause.
	/// </summary>
	private static List<ParameterModel> ClassifyParameters(
		IMethodSymbol producer,
		ImplInfo info,
		bool asyncFactory,
		BuildContext context)
	{
		List<ParameterModel> parameters = new();
		foreach (IParameterSymbol parameter in producer.Parameters)
		{
			ParameterModel parameterModel = ClassifyParameter(parameter, asyncFactory);

			// AWT134: a [FromServices] parameter (External) cannot also be an [Arg] runtime argument - it
			// cannot be both an externally-resolved dependency and a caller-supplied value. Point the diagnostic
			// at the offending parameter, falling back to the registration when its location is unavailable.
			if (parameterModel.Kind == DependencyKind.External && HasArgAttribute(parameter.GetAttributes()))
			{
				context.Diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ConflictingExternalParameter,
					parameterModel.Location ?? info.Location,
					new EquatableArray<string>([parameter.Name, DisplayInstance(info.ImplementationType),])));
			}

			parameterModel = RedirectDecoratorInner(parameterModel, info, context.DecoratorInner);

			parameterModel = SuppressRegisteredCollectionSynthesis(parameterModel, parameter, context.ServiceToImpl);

			// Variance: a closed-generic-interface request with no exact registration is redirected to a
			// variance-compatible registration before the [ImportServices] fall-through below - a match makes
			// the dependency container-resolved (its rewritten service type has a registration), so it is no
			// longer "otherwise-unresolved" and never falls through to the external provider.
			parameterModel = RedirectVariance(parameterModel, parameter, context.ServiceToImpl, context.Variance);
			RecordRequestedCollectionElement(parameterModel, parameter, context.Variance);

			// [ImportServices]: an otherwise-unresolved direct dependency (unkeyed) is satisfied from the
			// external provider rather than reported as missing. Only direct dependencies fall through; an
			// unregistered relationship type still surfaces as AWT101 below.
			if (context.ImportServices
			    && parameterModel is { Kind: DependencyKind.Direct, Key: null, }
			    && !context.ServiceToImpl.ContainsKey(KeyOf(parameterModel)))
			{
				parameterModel = parameterModel with { Kind = DependencyKind.External, };
			}

			parameters.Add(parameterModel);
			ReportWhenUnregistered(parameterModel, info, context);
		}

		return parameters;
	}

	/// <summary>
	///     Reports <see cref="Diagnostics.MissingDependency">AWT101</see> (or
	///     <see cref="Diagnostics.OwnedThroughLazy">AWT121</see> for an <c>Owned&lt;T&gt;</c> reached through a
	///     <c>Lazy</c>) for a classified parameter whose service type has no registration. A CancellationToken is
	///     forwarded from the resolve-time token, not resolved from the graph (like <c>[Arg]</c>), so it is never
	///     a missing dependency. A collection (Enumerable, AsyncEnumerable or the awaited AwaitedEnumerable) resolves
	///     to every registration of its element type under the parameter's key and an empty collection is legal, so
	///     an element type with no such registration is not a missing dependency either - it just yields an empty
	///     collection. (An unregistered collection type whose synthesis was suppressed was rewritten to Direct and so
	///     is no longer a collection kind here, and does surface as AWT101.) A closed generic that expansion refused to
	///     synthesize because its type arguments violate the open implementation's constraints (AWT126) is
	///     deliberately absent from <c>ServiceToImpl</c>; reporting AWT101 on top would name the same root cause
	///     twice, so it is suppressed here. An External dependency (a <c>[FromServices]</c> parameter, or an
	///     <c>[ImportServices]</c> fall-through) is resolved from the external provider, not the Awaiten graph, so
	///     it is never a missing dependency.
	/// </summary>
	private static void ReportWhenUnregistered(ParameterModel parameterModel, ImplInfo info, BuildContext context)
	{
		if (parameterModel.Kind is DependencyKind.Arg or DependencyKind.CancellationToken or DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable or DependencyKind.External
		    || context.ServiceToImpl.ContainsKey(KeyOf(parameterModel))
		    || context.ConstraintRejected.Contains(parameterModel.ServiceType))
		{
			return;
		}

		// Lazy does not unwrap Owned<T> (memoizing a disposal handle is a footgun), so a Lazy<Owned<T>> /
		// Lazy<Task<Owned<T>>> leaves the handle's Owned<T> type as the service - which is not registered.
		// Report that with the supported owned forms rather than a bare "missing Owned<T>" (AWT101).
		bool ownedThroughLazy = parameterModel.Kind is DependencyKind.Lazy or DependencyKind.LazyTask
		                        && parameterModel.ServiceType.StartsWith("global::Awaiten.Owned<", StringComparison.Ordinal);

		context.Diagnostics.Add(new DiagnosticInfo(
			ownedThroughLazy ? Diagnostics.OwnedThroughLazy : Diagnostics.MissingDependency,
			info.Location,
			new EquatableArray<string>([
				Display(info.OwningServiceOrImpl),
				DisplayInstance(info.ImplementationType),
				DisplayKeyed(parameterModel.ServiceType, parameterModel.Key),
			])));
	}

	/// <summary>
	///     Discovers the injected properties of a constructed implementation: every property marked
	///     <c>[Inject]</c> (opt-in only - a plain <c>required</c> property is left to the caller and is not
	///     auto-injected). Each is classified exactly like a Direct constructor parameter and resolved against
	///     the graph, so it produces a graph edge for cycle, captive and async-taint analysis and is filled
	///     through an object initializer after construction. Walks the implementation and its base types
	///     (most-derived first), so an overriding or shadowing declaration wins. Reports AWT136 (<c>[Inject]</c>
	///     on a property with no set/init accessor the container can assign through), AWT137 (an injected
	///     property marked <c>[Arg]</c>) and AWT101 (a member with no registration to satisfy it), each at the
	///     property's own location.
	/// </summary>
	private static void DiscoverInjectedMembers(
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Dictionary<ServiceKey, string> serviceToImpl,
		HashSet<string> constraintRejected,
		List<MemberModel> members,
		List<DiagnosticInfo> diagnostics)
	{
		foreach (IPropertySymbol property in InjectedProperties(info.Symbol))
		{
			if (ClassifyInjectedMember(property, info, containerSymbol, serviceToImpl, constraintRejected, diagnostics) is { } member)
			{
				members.Add(member);
			}
		}
	}

	/// <summary>
	///     The <c>[Inject]</c> properties of an implementation. Walks most-derived first, recording every
	///     instance property (<c>seen</c>) so a base declaration is shadowed by an overriding or <c>new</c> one.
	///     Property injection is opt-in: only a property marked <c>[Inject]</c> is yielded; a plain
	///     <c>required</c> property is left to the caller (never auto-injected). The shared walk behind
	///     <see cref="DiscoverInjectedMembers" /> and the loose-mode scan prune.
	/// </summary>
	private static IEnumerable<IPropertySymbol> InjectedProperties(INamedTypeSymbol implementation)
	{
		HashSet<string> seen = new(StringComparer.Ordinal);
		for (INamedTypeSymbol? type = implementation; type is not null; type = type.BaseType)
		{
			foreach (IPropertySymbol property in type.GetMembers().OfType<IPropertySymbol>())
			{
				if (property.IsStatic || property.IsIndexer || !seen.Add(property.Name) || !HasInject(property.GetAttributes()))
				{
					continue;
				}

				yield return property;
			}
		}
	}

	/// <summary>
	///     Classifies one <c>[Inject]</c> property into the member edge to fill after construction, or reports
	///     why it cannot be injected and returns <c>null</c>: AWT136 (no set/init accessor the container can
	///     reach), AWT137 (<c>[Arg]</c> on an injected property) or - when the resolved edge has no registration -
	///     AWT101 (with AWT121 substituted for an <c>Owned&lt;T&gt;</c> requested through <c>Lazy</c>). Each is
	///     reported at the property's own location. A missing registration only diagnoses; it still yields a
	///     member so the edge participates in analysis, exactly like a constructor parameter.
	/// </summary>
	private static MemberModel? ClassifyInjectedMember(
		IPropertySymbol property,
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Dictionary<ServiceKey, string> serviceToImpl,
		HashSet<string> constraintRejected,
		List<DiagnosticInfo> diagnostics)
	{
		LocationInfo? location = LocationInfo.From(property.Locations.FirstOrDefault());

		// AWT136: an [Inject] property must have a set/init accessor the container can assign through the object
		// initializer. The container is not a derived type, so a protected/private-protected setter (and a
		// cross-assembly internal one) is out of reach even though it is not private - apply the same accessibility
		// test the constructor path uses rather than a bare not-private check, so an unreachable setter surfaces as
		// AWT136 instead of an inaccessible-setter error in generated code.
		if (property.SetMethod is not { } setter || !IsAccessibleSetter(setter, containerSymbol))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.InjectedPropertyNotSettable,
				location,
				new EquatableArray<string>([property.Name, DisplayInstance(info.ImplementationType),])));
			return null;
		}

		// A deferred property ([Inject(Deferred = true)]) is assigned after construction and caching rather than
		// inside the object initializer, so it contributes no graph edge and can break a mutual constructor cycle.
		bool deferred = IsInjectDeferred(property.GetAttributes());

		// AWT144: a deferred property is assigned after construction and omitted from the emitted object
		// initializer, so it needs a real set accessor and must not be `required` - an init-only accessor can
		// only be assigned inside an object initializer (exactly the construction-time path a deferred property
		// must avoid to break a cycle), and a required member omitted from the initializer would surface as an
		// opaque CS9035 inside the generated container instead of a targeted diagnostic here.
		if (deferred && (setter.IsInitOnly || property.IsRequired))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.DeferredPropertyIsInitOnly,
				location,
				new EquatableArray<string>([
					property.Name,
					DisplayInstance(info.ImplementationType),
					setter.IsInitOnly ? "init-only" : "required",
				])));
			return null;
		}

		ParameterModel dependency = ClassifyDependency(
			property.Type, property.GetAttributes(), asyncFactory: false, location);

		// AWT137: runtime arguments flow only through a Func<…> factory into [Arg] constructor parameters, never
		// through property injection (the member resolves entirely from the graph).
		if (dependency.Kind == DependencyKind.Arg)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.InjectedPropertyIsArg,
				location,
				new EquatableArray<string>([property.Name, DisplayInstance(info.ImplementationType),])));
			return null;
		}

		// AWT101: a direct/relationship member edge needs a registration to satisfy it (a collection member is
		// satisfied elsewhere, like a constructor parameter, and yields an empty collection when unregistered).
		// Mirror ClassifyParameters: a constraint-rejected open generic (AWT126) is not re-reported here, and an
		// Owned<T> requested through Lazy surfaces the targeted AWT121 instead.
		if (dependency.Kind is not (DependencyKind.Enumerable or DependencyKind.AsyncEnumerable)
		    && !serviceToImpl.ContainsKey(KeyOf(dependency))
		    && !constraintRejected.Contains(dependency.ServiceType))
		{
			bool ownedThroughLazy = dependency.Kind is DependencyKind.Lazy or DependencyKind.LazyTask
			                        && dependency.ServiceType.StartsWith("global::Awaiten.Owned<", StringComparison.Ordinal);

			diagnostics.Add(new DiagnosticInfo(
				ownedThroughLazy ? Diagnostics.OwnedThroughLazy : Diagnostics.MissingDependency,
				location,
				new EquatableArray<string>([
					Display(info.OwningServiceOrImpl),
					DisplayInstance(info.ImplementationType),
					DisplayKeyed(dependency.ServiceType, dependency.Key),
				])));
		}

		return new MemberModel(property.Name, dependency, deferred);
	}

	// The setter must be reachable from the container's object initializer, which is not a derived context:
	// mirrors IsAccessibleConstructor - public always, internal/protected-internal only within the container's
	// own assembly, and protected/private-protected/private never (the container cannot reach them).
	private static bool IsAccessibleSetter(IMethodSymbol setter, INamedTypeSymbol containerSymbol)
		=> setter.DeclaredAccessibility switch
		{
			Accessibility.Public => true,
			Accessibility.Internal or Accessibility.ProtectedOrInternal =>
				SymbolEqualityComparer.Default.Equals(setter.ContainingAssembly, containerSymbol.ContainingAssembly),
			_ => false,
		};

	/// <summary>
	///     Classifies a constructor parameter as a runtime argument (<c>[Arg]</c>), a deferred relationship
	///     type (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c> or <c>Func&lt;TArg…, T&gt;</c>) or a direct
	///     dependency, returning the underlying service type it resolves. A <c>Func&lt;TArg…, T&gt;</c> also
	///     carries the leading runtime-argument types it supplies to the produced service's <c>[Arg]</c>
	///     parameters. Only one level of nesting is supported: a relationship over another relationship
	///     (e.g. <c>Func&lt;Func&lt;T&gt;&gt;</c>) is classified as a direct dependency so it surfaces as an
	///     unregistered service type rather than a misleading diagnostic about the inner relationship.
	/// </summary>
	private static ParameterModel ClassifyParameter(IParameterSymbol parameter, bool asyncFactory)
	{
		LocationInfo? location = LocationInfo.From(parameter.Locations.FirstOrDefault());

		// An explicit [FromServices] parameter is resolved from the external provider; its own type is the
		// external service type, and a [FromKey] on it selects the keyed external service (the key is forwarded
		// to the resolver). It takes precedence so the parameter is never treated as an Awaiten graph edge (a
		// [FromServices] together with [Arg] is reported as AWT134 in ClassifyParameters). [FromServices] is a
		// constructor-parameter concern only, so it lives here rather than in the shared ClassifyDependency core
		// (an injected property never resolves from the external provider).
		if (HasFromServices(parameter))
		{
			return new ParameterModel(
				parameter.Type.ToDisplayString(FullyQualified), DependencyKind.External, Key: FromKey(parameter.GetAttributes()), Location: location);
		}

		return ClassifyDependency(parameter.Type, parameter.GetAttributes(), asyncFactory, location);
	}

	/// <summary>
	///     Classifies a dependency by its declared type and attributes, shared by constructor parameters and
	///     injected properties: a runtime argument (<c>[Arg]</c>), an asynchronous factory's forwarded
	///     <c>CancellationToken</c>, a deferred relationship type (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>,
	///     their async siblings or a bare <c>Owned&lt;T&gt;</c> / <c>Task&lt;T&gt;</c>), a collection, or a
	///     direct dependency - returning the underlying service type it resolves and an optional
	///     <c>[FromKey]</c> selection. The property path reuses this verbatim, so a member resolves exactly
	///     like a constructor parameter.
	/// </summary>
	private static ParameterModel ClassifyDependency(ITypeSymbol type, ImmutableArray<AttributeData> attributes, bool asyncFactory, LocationInfo? location)
	{
		if (HasArgAttribute(attributes))
		{
			return new ParameterModel(type.ToDisplayString(FullyQualified), DependencyKind.Arg, Location: location);
		}

		// An asynchronous factory's CancellationToken parameter is not resolved from the graph: the container
		// forwards the resolve-time token (the async creator's cancellationToken). Limited to async factories -
		// only they are constructed on the async path where that token exists; a synchronous factory (or a
		// constructor) has no ambient token to forward, so its CancellationToken stays an ordinary dependency
		// and is reported as AWT101 when unregistered rather than silently receiving default. An [Arg]
		// CancellationToken is handled above as a caller-supplied runtime argument and is left untouched.
		if (asyncFactory
		    && type is INamedTypeSymbol { Name: "CancellationToken", } token
		    && token.ContainingNamespace?.ToDisplayString() == "System.Threading")
		{
			return new ParameterModel(
				type.ToDisplayString(FullyQualified), DependencyKind.CancellationToken, Location: location);
		}

		// A [FromKey] selects the keyed registration of the dependency's service type, whether it is required
		// directly, deferred behind a Func<T>/Lazy<T>, wrapped in an Owned<T> handle, or a collection - the
		// service type is the same, only the delivery differs.
		string? key = FromKey(attributes);

		// An asynchronous collection (IAsyncEnumerable<T>) resolves to every registration of its element type, like
		// the synchronous collection shapes below, but awaits each member's initialization - so it is the one shape
		// through which an async-tainted member is legal. Recognized before the synchronous shapes (both live in
		// System.Collections.Generic) and before the relationship gate.
		if (IsAsyncEnumerable(type, out string? asyncElementType))
		{
			return new ParameterModel(asyncElementType!, DependencyKind.AsyncEnumerable, Key: key, Location: location);
		}

		// A collection dependency resolves to every registration of its element type under the parameter's
		// [FromKey] key (unkeyed by default). Recognized before the relationship types so IEnumerable<T> and T[]
		// are not mistaken for a plain generic service or an array-typed direct dependency.
		if (TryGetCollectionElement(type, out string? elementType))
		{
			return new ParameterModel(elementType!, DependencyKind.Enumerable, Key: key, Location: location);
		}

		// An awaited collection (Task<C> over the synchronous collection shapes, e.g. Task<IReadOnlyList<T>>)
		// resolves to every registration of its element type under the parameter's key, awaiting each
		// async-initialized member behind the returned task - the eager awaited sibling of the synchronous
		// collection, and the second shape (besides IAsyncEnumerable<T>) through which an async-tainted member is
		// legal. Recognized before the bare Task<T> relationship below, so Task<IReadOnlyList<T>> is the awaited
		// collection of T rather than a Task relationship over the (unregistered) collection type itself.
		if (TryGetAwaitedCollection(type, out string? awaitedElement, out string? awaitedCollection))
		{
			return new ParameterModel(
				awaitedElement!, DependencyKind.AwaitedEnumerable, Key: key, Location: location,
				AwaitedCollectionType: awaitedCollection);
		}

		// A bare Owned<T> dependency: resolve T into a throwaway scope and hand the caller the disposal handle.
		if (IsOwned(type, out ITypeSymbol ownedInner))
		{
			return new ParameterModel(ownedInner.ToDisplayString(FullyQualified), DependencyKind.Owned, Key: key, Location: location);
		}

		// A bare Task<T> dependency: an awaitable that resolves (and initializes) T. Task lives in
		// System.Threading.Tasks, not System, so it is recognized here rather than through the System-generic
		// relationship gate below (which handles the Func/Lazy wrappers, including Func<…, Task<T>>).
		if (IsTask(type, out ITypeSymbol taskResult))
		{
			// Task<Owned<T>> is the async counterpart of a bare Owned<T>: async-resolve (and initialize) T into a
			// throwaway child scope and hand back the disposal handle.
			return IsOwned(taskResult, out ITypeSymbol taskOwnedInner)
				? new ParameterModel(taskOwnedInner.ToDisplayString(FullyQualified), DependencyKind.Task, Key: key, Location: location, ProducesOwned: true)
				: new ParameterModel(taskResult.ToDisplayString(FullyQualified), DependencyKind.Task, Key: key, Location: location);
		}

		if (type is INamedTypeSymbol { IsGenericType: true, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System"
		    && ClassifyRelationship(named, key, location) is { } relationship)
		{
			return relationship;
		}

		// A direct dependency, optionally selecting a keyed registration with [FromKey].
		return new ParameterModel(
			type.ToDisplayString(FullyQualified), DependencyKind.Direct, Key: key, Location: location);
	}

	/// <summary>
	///     Classifies a <c>System</c> generic as the single-level relationship it defers - <c>Lazy&lt;T&gt;</c>,
	///     <c>Func&lt;T&gt;</c> or <c>Func&lt;TArg…, T&gt;</c> (the latter optionally producing an
	///     <c>Owned&lt;T&gt;</c> disposal handle) - returning the underlying service type. A type that is not a
	///     recognized relationship, or whose produced type is itself a relationship (nesting beyond one level),
	///     returns <see langword="null" /> so the caller treats it as a direct dependency on the whole type.
	/// </summary>
	private static ParameterModel? ClassifyRelationship(INamedTypeSymbol named, string? key, LocationInfo? location)
	{
		if (named is { Name: "Lazy", TypeArguments.Length: 1, } && !IsRelationshipType(named.TypeArguments[0]))
		{
			// Lazy<Task<T>> is the async counterpart of Lazy<T>: a memoized awaitable dependency.
			return IsTask(named.TypeArguments[0], out ITypeSymbol lazyTaskResult)
				? new ParameterModel(lazyTaskResult.ToDisplayString(FullyQualified), DependencyKind.LazyTask, Key: key, Location: location)
				: new ParameterModel(named.TypeArguments[0].ToDisplayString(FullyQualified), DependencyKind.Lazy, Key: key, Location: location);
		}

		if (named is not { Name: "Func", TypeArguments.Length: >= 1, })
		{
			return null;
		}

		// Func<T> defers resolution; Func<TArg…, T> additionally supplies runtime arguments (the leading type
		// arguments) to the produced service's [Arg]-marked parameters.
		ITypeSymbol[] typeArgs = named.TypeArguments.ToArray();
		ITypeSymbol service = typeArgs[typeArgs.Length - 1];
		string[] argTypes = typeArgs.Take(typeArgs.Length - 1)
			.Select(t => t.ToDisplayString(FullyQualified))
			.ToArray();

		// Func<…, Task<T>> is the async counterpart of Func<…, T>: an async factory that resolves (and
		// initializes) T, awaiting it. It forwards any leading runtime arguments to T's [Arg] parameters.
		// Func<…, Task<Owned<T>>> is its leak-free form: each call async-resolves T into a throwaway child scope
		// and hands back the Owned<T> disposal handle (the async counterpart of Func<…, Owned<T>>).
		if (IsTask(service, out ITypeSymbol funcTaskResult))
		{
			return IsOwned(funcTaskResult, out ITypeSymbol funcTaskOwnedInner)
				? new ParameterModel(
					funcTaskOwnedInner.ToDisplayString(FullyQualified), DependencyKind.FuncTask,
					new EquatableArray<string>(argTypes), Key: key, Location: location, ProducesOwned: true)
				: new ParameterModel(
					funcTaskResult.ToDisplayString(FullyQualified), DependencyKind.FuncTask,
					new EquatableArray<string>(argTypes), Key: key, Location: location);
		}

		// Func<…, Owned<T>> is the leak-free factory: its produced value is an Owned<T> disposal handle.
		if (IsOwned(service, out ITypeSymbol funcOwnedInner))
		{
			return new ParameterModel(
				funcOwnedInner.ToDisplayString(FullyQualified), DependencyKind.Func,
				new EquatableArray<string>(argTypes), Key: key, Location: location, ProducesOwned: true);
		}

		// Func<…, T> over a relationship type (nesting beyond one level) falls through to a direct dependency.
		return IsRelationshipType(service)
			? null
			: new ParameterModel(
				service.ToDisplayString(FullyQualified), DependencyKind.Func, new EquatableArray<string>(argTypes), Key: key, Location: location);
	}

	private static ServiceKey KeyOf(ParameterModel parameter) => new(parameter.ServiceType, parameter.Key);

	private static string DisplayKeyed(string serviceType, string? key)
		=> key is null ? Display(serviceType) : $"{Display(serviceType)} (key: {key})";

	private static string? FromKey(ImmutableArray<AttributeData> attributes)
		=> TryGetAwaitenAttribute(attributes, "FromKeyAttribute", out AttributeData? attribute)
		   && attribute!.ConstructorArguments.Length == 1
		   && attribute.ConstructorArguments[0].Value is string key
			? key
			: null;

	// Whether a type is a System.Threading.Tasks.Task<T>, yielding its result type T. Used to recognize the
	// async relationship types (Task<T>, Func<…, Task<T>>, Lazy<Task<T>>); ValueTask<T> is deliberately not a
	// relationship type (a stored ValueTask may only be awaited once) - it is supported solely as an async
	// factory's return type, on the producer side.
	private static bool IsTask(ITypeSymbol type, out ITypeSymbol result)
	{
		if (type is INamedTypeSymbol { IsGenericType: true, Name: "Task", TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
		{
			result = named.TypeArguments[0];
			return true;
		}

		result = type;
		return false;
	}

	/// <summary>
	///     Recognizes a collection dependency - an array <c>T[]</c> or one of the standard generic collection
	///     interfaces (<c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>,
	///     <c>IReadOnlyCollection&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>) - yielding the
	///     fully-qualified element type. An array satisfies every one of these, so the emitter materializes all
	///     of them as an array.
	/// </summary>
	private static bool TryGetCollectionElement(ITypeSymbol type, out string? elementType)
	{
		// Only a single-dimensional (rank-1) array is a collection shape: it is what the emitter materializes
		// (new T[] { … }). A multidimensional array (T[,]) is a distinct type that a rank-1 literal cannot fill,
		// so it stays an ordinary direct dependency (an unregistered one surfaces as AWT101, not broken codegen).
		if (type is IArrayTypeSymbol { Rank: 1, } array)
		{
			elementType = array.ElementType.ToDisplayString(FullyQualified);
			return true;
		}

		if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
		    && named.Name is "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection")
		{
			elementType = named.TypeArguments[0].ToDisplayString(FullyQualified);
			return true;
		}

		elementType = null;
		return false;
	}

	/// <summary>
	///     Recognizes an awaited collection - a <c>Task&lt;C&gt;</c> whose result <c>C</c> is one of the collection
	///     shapes recognized by <see cref="TryGetCollectionElement" /> (e.g. <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c>
	///     or <c>Task&lt;T[]&gt;</c>) - yielding the fully-qualified element type and inner collection type.
	///     <c>ValueTask&lt;C&gt;</c> is deliberately not recognized, for the same reason a bare
	///     <c>ValueTask&lt;T&gt;</c> is not a relationship type (see <see cref="IsTask" />): a stored ValueTask may
	///     only be awaited once, and an injected collection is held for the consumer's lifetime.
	/// </summary>
	private static bool TryGetAwaitedCollection(ITypeSymbol type, out string? elementType, out string? collectionType)
	{
		if (IsTask(type, out ITypeSymbol result) && TryGetCollectionElement(result, out elementType))
		{
			collectionType = result.ToDisplayString(FullyQualified);
			return true;
		}

		elementType = null;
		collectionType = null;
		return false;
	}

	// Whether a type is a System.Collections.Generic.IAsyncEnumerable<T> asynchronous collection, yielding its
	// fully-qualified element type T. The one collection shape that awaits its members, so it is classified apart
	// from the synchronous shapes in TryGetCollectionElement (which materialize eagerly into an array).
	private static bool IsAsyncEnumerable(ITypeSymbol type, out string? elementType)
	{
		if (type is INamedTypeSymbol { IsGenericType: true, Name: "IAsyncEnumerable", TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
		{
			elementType = named.TypeArguments[0].ToDisplayString(FullyQualified);
			return true;
		}

		elementType = null;
		return false;
	}

	// The fully-qualified IAsyncEnumerable<T> shape of <paramref name="elementType" />, in the exact form
	// registrations are stored under, so a membership check against serviceToImpl recognizes an explicitly
	// registered async-collection type (the async analogue of CollectionShapeTypes). Its own shape, so a single
	// string rather than a set.
	internal static string AsyncEnumerableShapeType(string elementType)
		=> $"global::System.Collections.Generic.IAsyncEnumerable<{elementType}>";

	// The fully-qualified type strings of every collection shape of <paramref name="elementType" /> - the five
	// generic collection interfaces and the rank-1 array - in the exact form registrations are stored under, so a
	// membership check against serviceToImpl recognizes an explicitly registered collection type. Shared by the
	// generator (injection classification) and the emitter (public dispatch) so the two never drift.
	internal static IEnumerable<string> CollectionShapeTypes(string elementType)
	{
		yield return $"global::System.Collections.Generic.IEnumerable<{elementType}>";
		yield return $"global::System.Collections.Generic.IReadOnlyList<{elementType}>";
		yield return $"global::System.Collections.Generic.IReadOnlyCollection<{elementType}>";
		yield return $"global::System.Collections.Generic.IList<{elementType}>";
		yield return $"global::System.Collections.Generic.ICollection<{elementType}>";
		yield return $"{elementType}[]";
	}

	// Whether a type is an Awaiten.Owned<T> disposal handle, yielding the owned service type T.
	private static bool IsOwned(ITypeSymbol type, out ITypeSymbol inner)
	{
		if (type is INamedTypeSymbol { Name: "Owned", TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == AttributeNamespace)
		{
			inner = named.TypeArguments[0];
			return true;
		}

		inner = type;
		return false;
	}

	private static bool HasArgAttribute(ImmutableArray<AttributeData> attributes)
		=> HasAwaitenAttribute(attributes, "ArgAttribute");

	private static bool HasInject(ImmutableArray<AttributeData> attributes)
		=> HasAwaitenAttribute(attributes, "InjectAttribute");

	// Whether an [Inject] attribute sets Deferred = true, so the member is assigned after construction and
	// caching (breaking a mutual constructor cycle) rather than filled inside the object initializer.
	private static bool IsInjectDeferred(ImmutableArray<AttributeData> attributes)
	{
		if (!TryGetAwaitenAttribute(attributes, "InjectAttribute", out AttributeData? attribute) || attribute is null)
		{
			return false;
		}

		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == "Deferred" && argument.Value.Value is bool value)
			{
				return value;
			}
		}

		return false;
	}

	/// <summary>
	///     Whether <paramref name="attributes" /> carries the Awaiten attribute named
	///     <paramref name="attributeName" /> (matched by simple type name within the <c>Awaiten</c> namespace).
	/// </summary>
	private static bool HasAwaitenAttribute(ImmutableArray<AttributeData> attributes, string attributeName)
		=> TryGetAwaitenAttribute(attributes, attributeName, out _);

	/// <summary>
	///     Locates the Awaiten attribute named <paramref name="attributeName" /> in an attribute list, if
	///     present, so its named/constructor arguments can be read. Matches by simple type name within the
	///     <c>Awaiten</c> namespace, so it ignores same-named attributes from other namespaces.
	/// </summary>
	private static bool TryGetAwaitenAttribute(ImmutableArray<AttributeData> attributes, string attributeName, out AttributeData? attribute)
	{
		foreach (AttributeData candidate in attributes)
		{
			if (candidate.AttributeClass is { } attributeClass
			    && attributeClass.Name == attributeName
			    && attributeClass.ContainingNamespace?.ToDisplayString() == AttributeNamespace)
			{
				attribute = candidate;
				return true;
			}
		}

		attribute = null;
		return false;
	}

	// Whether a parameter is marked [FromServices], so it is resolved from the container's external provider
	// rather than the Awaiten graph.
	private static bool HasFromServices(IParameterSymbol parameter)
		=> HasAwaitenAttribute(parameter.GetAttributes(), "FromServicesAttribute");

	// Whether the container is marked [ImportServices], so every otherwise-unresolved direct dependency is
	// satisfied from the external provider rather than reported as missing.
	private static bool ContainerImportsServices(INamedTypeSymbol containerSymbol)
		=> HasAwaitenAttribute(containerSymbol.GetAttributes(), "ImportServicesAttribute");

	private static bool IsRelationshipType(ITypeSymbol type)
		=> type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, Name: "Func" or "Lazy", } named
		   && named.ContainingNamespace?.ToDisplayString() == "System";
}
