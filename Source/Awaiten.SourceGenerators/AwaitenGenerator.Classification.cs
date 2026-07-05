using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     Rewrites a collection dependency to a direct dependency when that exact collection shape is itself
	///     registered as a service (an opaque <c>string[]</c>, config list, channel, or keyed dictionary).
	///     Suppression is all-or-nothing per shape: the registered shape resolves directly, and an unregistered
	///     sibling is AWT101 rather than a silently synthesized collection that could disagree. A registered
	///     synchronous shape also claims the <c>IAsyncEnumerable&lt;T&gt;</c> and awaited <c>Task&lt;C&gt;</c>
	///     views; async, awaited and keyed shapes claim only their own. Shared by the constructor-parameter and
	///     <c>[Inject]</c>-property paths.
	/// </summary>
	private static ParameterModel SuppressRegisteredCollectionSynthesis(
		ParameterModel parameterModel,
		ITypeSymbol declaredType,
		Dictionary<ServiceKey, string> serviceToImpl)
	{
		bool syncShapeRegistered = parameterModel.Kind is (DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable)
		                           && CollectionShapeTypes(parameterModel.ServiceType).Any(shape => serviceToImpl.ContainsKey(new ServiceKey(shape, parameterModel.Key)));
		bool asyncShapeRegistered = parameterModel.Kind == DependencyKind.AsyncEnumerable
		                            && serviceToImpl.ContainsKey(new ServiceKey(AsyncEnumerableShapeType(parameterModel.ServiceType), parameterModel.Key));
		bool awaitedOrKeyedShapeRegistered = parameterModel.Kind is DependencyKind.AwaitedEnumerable or DependencyKind.KeyedCollection or DependencyKind.AwaitedKeyedCollection
		                                     && serviceToImpl.ContainsKey(new ServiceKey(declaredType.ToDisplayString(FullyQualified), parameterModel.Key));
		// A registered dictionary claims the awaited Task<…> view too (inner dictionary type in
		// AwaitedCollectionType). How it is claimed depends on whether a synthesized awaited view could exist at
		// all (see below).
		bool keyedSyncShapeRegistered = parameterModel.Kind == DependencyKind.AwaitedKeyedCollection
		                                && serviceToImpl.ContainsKey(new ServiceKey(parameterModel.AwaitedCollectionType!, parameterModel.Key));
		if (syncShapeRegistered || asyncShapeRegistered || awaitedOrKeyedShapeRegistered)
		{
			string collectionType = declaredType.ToDisplayString(FullyQualified);
			return parameterModel with { ServiceType = collectionType, Kind = DependencyKind.Direct, AwaitedCollectionType = null, };
		}

		if (keyedSyncShapeRegistered)
		{
			// A registered synchronous dictionary claims its awaited Task<…> view all-or-nothing when the view is a
			// keyless string-keyed one (so the sibling becomes Direct, AWT101 if unregistered). A [FromKey] or
			// non-string key admits no synthesized awaited view, so it stays the bare Task over the registered dictionary.
			return parameterModel.Key is null && KeyedDependencyKeyType(declaredType)?.SpecialType == SpecialType.System_String
				? parameterModel with { ServiceType = declaredType.ToDisplayString(FullyQualified), Kind = DependencyKind.Direct, AwaitedCollectionType = null, }
				: parameterModel with { ServiceType = parameterModel.AwaitedCollectionType!, Kind = DependencyKind.Task, AwaitedCollectionType = null, };
		}

		return parameterModel;
	}

	/// <summary>
	///     Classifies the producer's parameters (a constructor's or factory method's) and reports
	///     <see cref="Diagnostics.MissingDependency">AWT101</see> for any non-<c>[Arg]</c> parameter whose
	///     service type is not registered. An <c>[Arg]</c> is supplied at resolve time, so it is never missing.
	///     A service in the context's <c>ConstraintRejected</c> set (an open generic that could not be closed at
	///     the required type argument, AWT126) is not reported again as AWT101, since the constraint violation is
	///     the one root cause.
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
			// A [RequestingType] parameter of a Factory method is filled at each construction site with the
			// consumer's typeof(…), not resolved from the graph, so it carries no edge. Honored only on a factory
			// producer; the parameter must be System.Type (AWT162). On a constructor it falls through as an ordinary
			// dependency (an unregistered System.Type surfaces as AWT101).
			if (info.Production == ProductionKind.Factory && HasRequestingType(parameter.GetAttributes()))
			{
				LocationInfo? requestingTypeLocation = LocationInfo.From(parameter.Locations.FirstOrDefault());
				if (!IsSystemType(parameter.Type))
				{
					context.Diagnostics.Add(new DiagnosticInfo(
						Diagnostics.InvalidRequestingType,
						requestingTypeLocation ?? info.Location,
						new EquatableArray<string>([parameter.Name, DisplayInstance(info.ImplementationType),])));
				}

				parameters.Add(new ParameterModel(
					parameter.Type.ToDisplayString(FullyQualified), DependencyKind.RequestingType,
					Location: requestingTypeLocation));
				continue;
			}

			ParameterModel parameterModel = ClassifyParameter(parameter, asyncFactory);

			ReportUnsupportedFromKey(parameter.GetAttributes(), parameterModel.Location ?? info.Location, DisplayInstance(info.ImplementationType), context.Diagnostics);

			// AWT134: a [FromServices] parameter (External) cannot also be an [Arg]: it cannot be both an
			// externally-resolved dependency and a caller-supplied value. Point the diagnostic at the offending
			// parameter, falling back to the registration when its location is unavailable.
			if (parameterModel.Kind == DependencyKind.External && HasArgAttribute(parameter.GetAttributes()))
			{
				context.Diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ConflictingExternalParameter,
					parameterModel.Location ?? info.Location,
					new EquatableArray<string>([parameter.Name, DisplayInstance(info.ImplementationType),])));
			}

			parameterModel = RedirectDecoratorInner(parameterModel, info, context.DecoratorInner);

			// A [FromKey] and a decorator's inner redirect already carry a key, so both take precedence; the
			// redirect also keeps the parameter off the [ImportServices] fall-through below.
			parameterModel = RedirectContextualBinding(parameterModel, info, context.ServiceToImpl, context.ConsumedConditionals);

			parameterModel = SuppressRegisteredCollectionSynthesis(parameterModel, parameter.Type, context.ServiceToImpl);

			// AWT159/AWT160: keyed-collection misuse is reported only for a dictionary that stays synthesized. An
			// explicitly registered dictionary was rewritten to Direct above and resolves that registration,
			// whatever its key type or [FromKey].
			ReportUnsupportedKeyedCollectionKey(parameterModel, parameter.Type, info, context.ServiceToImpl, context.Diagnostics);
			ReportFromKeyOnKeyedCollection(parameterModel, parameter.Type, info, context.Diagnostics);

			// Variance: a closed-generic-interface request with no exact registration is redirected to a
			// variance-compatible registration before the [ImportServices] fall-through below. A match makes the
			// dependency container-resolved, so it is no longer "otherwise-unresolved" and never falls through to
			// the external provider.
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

		// AWT163: a requesting-type factory is built fresh per consumer with the consumer's typeof(…) supplied at
		// each site, so it is not reached through a Func<TArg…, T> and cannot also be a parameterized ([Arg])
		// factory. The two together would emit a resolver that takes the requesting type but omits the runtime
		// arguments (and vice versa at the call site), so reject the combination outright.
		if (parameters.Any(p => p.Kind == DependencyKind.RequestingType)
		    && parameters.Any(p => p.Kind == DependencyKind.Arg))
		{
			context.Diagnostics.Add(new DiagnosticInfo(
				Diagnostics.RequestingTypeWithArg,
				info.Location,
				new EquatableArray<string>([DisplayInstance(info.ImplementationType),])));
		}

		return parameters;
	}

	/// <summary>
	///     The single predicate every gate uses to treat all collection kinds alike (AWT101/AWT141 exemption,
	///     constructor eligibility, the guarded graph indexer), so adding a collection kind is a one-line change here.
	///     A collection is synthesized from its element type's registrations and always satisfiable: an unregistered
	///     element type yields an empty collection, never a missing dependency.
	/// </summary>
	private static bool IsSynthesizedCollection(DependencyKind kind)
		=> kind is DependencyKind.Enumerable or DependencyKind.AsyncEnumerable or DependencyKind.AwaitedEnumerable or DependencyKind.KeyedCollection or DependencyKind.AwaitedKeyedCollection;

	/// <summary>
	///     Reports <see cref="Diagnostics.MissingDependency">AWT101</see> (or
	///     <see cref="Diagnostics.OwnedThroughLazy">AWT121</see> for an <c>Owned&lt;T&gt;</c> reached through
	///     <c>Lazy</c>) for a classified parameter whose service type has no registration. A CancellationToken, a
	///     collection, a constraint-rejected open generic (AWT126) and an External dependency are never missing, so
	///     none is reported.
	/// </summary>
	private static void ReportWhenUnregistered(ParameterModel parameterModel, ImplInfo info, BuildContext context)
	{
		if (parameterModel.Kind is DependencyKind.Arg or DependencyKind.CancellationToken or DependencyKind.External
		    || IsSynthesizedCollection(parameterModel.Kind)
		    || context.ServiceToImpl.ContainsKey(KeyOf(parameterModel))
		    || context.ConstraintRejected.Contains(parameterModel.ServiceType))
		{
			return;
		}

		// Lazy does not unwrap Owned<T> (memoizing a disposal handle is a footgun), so a Lazy<Owned<T>> /
		// Lazy<Task<Owned<T>>> leaves the unregistered Owned<T> type as the service. Report AWT121 with the
		// supported owned forms rather than a bare "missing Owned<T>" (AWT101).
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
	///     Discovers the <c>[Inject]</c> properties of a constructed implementation (opt-in; a plain
	///     <c>required</c> property is not auto-injected). Each is classified like a Direct constructor parameter and
	///     produces a graph edge, filled through an object initializer after construction. See
	///     <see cref="ClassifyInjectedMember" /> for the diagnostics reported.
	/// </summary>
	private static void DiscoverInjectedMembers(
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Dictionary<ServiceKey, string> serviceToImpl,
		HashSet<string> constraintRejected,
		HashSet<ServiceKey> consumedConditionals,
		List<MemberModel> members,
		List<DiagnosticInfo> diagnostics)
	{
		foreach (IPropertySymbol property in InjectedProperties(info.Symbol))
		{
			if (ClassifyInjectedMember(property, info, containerSymbol, serviceToImpl, constraintRejected, consumedConditionals, diagnostics) is { } member)
			{
				members.Add(member);
			}
		}
	}

	/// <summary>
	///     The <c>[Inject]</c> properties of an implementation. Walks most-derived first, recording every instance
	///     property (<c>seen</c>) so a base declaration is shadowed by an overriding or <c>new</c> one. Injection
	///     is opt-in: only a property marked <c>[Inject]</c> is yielded. Shared by
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
	///     Classifies one <c>[Inject]</c> property into the member edge to fill after construction, or reports why
	///     it cannot be injected and returns <c>null</c>. Diagnostics (each at the property's location): AWT136 (no
	///     reachable set/init accessor), AWT137 (<c>[Arg]</c> on an injected property), AWT157/AWT158 (a malformed
	///     <c>Optional</c> property), and AWT101/AWT121 for a missing registration. A required member with a missing
	///     registration still yields an edge (so it participates in analysis); an <c>Optional</c> one is dropped
	///     without diagnostic.
	/// </summary>
	private static MemberModel? ClassifyInjectedMember(
		IPropertySymbol property,
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Dictionary<ServiceKey, string> serviceToImpl,
		HashSet<string> constraintRejected,
		HashSet<ServiceKey> consumedConditionals,
		List<DiagnosticInfo> diagnostics)
	{
		LocationInfo? location = LocationInfo.From(property.Locations.FirstOrDefault());

		// AWT136: an [Inject] property must have a set/init accessor the container can assign through the object
		// initializer. The container is not a derived type, so a protected/private-protected setter (and a
		// cross-assembly internal one) is out of reach even though not private. Apply the same accessibility test
		// the constructor path uses rather than a bare not-private check, so an unreachable setter surfaces as
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
		// An optional property ([Inject(Optional = true)]) is instead dropped when its dependency is unregistered.
		bool deferred = IsInjectDeferred(property.GetAttributes());
		bool optional = IsInjectOptional(property.GetAttributes());

		ParameterModel dependency = ClassifyDependency(
			property.Type, property.GetAttributes(), asyncFactory: false, location);

		ReportUnsupportedFromKey(property.GetAttributes(), location, DisplayInstance(info.ImplementationType), diagnostics);

		dependency = RedirectContextualBinding(dependency, info, serviceToImpl, consumedConditionals);

		// A collection member is always filled (an unregistered collection yields an empty one), so it is never
		// omitted from the object initializer: Optional has no effect on it, and neither the Optional shape rules
		// (AWT157/AWT158) nor the Optional drop below apply. Measured before synthesis suppression below, since a
		// collection whose shape is explicitly registered is still a collection-typed member here (never omitted),
		// so the shape rules must skip it too.
		bool isCollection = IsSynthesizedCollection(dependency.Kind);

		// AWT144/AWT157/AWT158: the accessor and modifiers must be compatible with how the member is assigned.
		if (RejectsInjectedPropertyShape(property, setter, info, deferred, optional, isCollection, diagnostics))
		{
			return null;
		}

		// An explicitly registered collection shape (or keyed dictionary) preempts synthesis for an [Inject]
		// member exactly as for a constructor parameter: the member is rewritten to a direct dependency on the
		// registered opaque value.
		dependency = SuppressRegisteredCollectionSynthesis(dependency, property.Type, serviceToImpl);

		// AWT159/AWT160: keyed-collection misuse is reported only for a dictionary that stays synthesized. An
		// explicitly registered dictionary was rewritten to Direct above and resolves that registration.
		ReportUnsupportedKeyedCollectionKey(dependency, property.Type, info, serviceToImpl, diagnostics);
		ReportFromKeyOnKeyedCollection(dependency, property.Type, info, diagnostics);

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
		if (!isCollection
		    && !serviceToImpl.ContainsKey(KeyOf(dependency))
		    && !constraintRejected.Contains(dependency.ServiceType))
		{
			bool ownedThroughLazy = dependency.Kind is DependencyKind.Lazy or DependencyKind.LazyTask
			                        && dependency.ServiceType.StartsWith("global::Awaiten.Owned<", StringComparison.Ordinal);

			// An optional member with a merely missing registration is dropped so the property is left at its
			// default. Dropping it (rather than yielding a member) also keeps the absent edge out of cycle, captive
			// and async-taint analysis: there is nothing to assign, so nothing to analyze. A structurally
			// impossible request is not a missing registration, though: Owned<T> can never be produced through
			// Lazy, so AWT121 is reported (and the edge kept) regardless of Optional, as for a required one.
			if (optional && !ownedThroughLazy)
			{
				return null;
			}

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

	/// <summary>
	///     Reports the shape diagnostics for an <c>[Inject]</c> property whose accessor or modifiers are
	///     incompatible with how it would be assigned, returning <see langword="true" /> when the property is
	///     rejected. AWT144: a deferred property is assigned after construction and omitted from the object
	///     initializer, so it needs a real <c>set</c> accessor and must not be <c>required</c> (an init-only
	///     accessor and a required member can only be satisfied inside the initializer, the construction-time path
	///     a deferred property avoids; a required member omitted would otherwise surface as an opaque CS9035).
	///     AWT157: an optional property is omitted from the initializer when its dependency is unregistered, which a
	///     <c>required</c> member does not allow (same CS9035). AWT158 (suppressible warning, does <em>not</em>
	///     reject): an optional init-only property is omittable, but once construction has passed an init-only
	///     accessor can no longer be assigned, so an unregistered optional member stays at its default. The two
	///     Optional rules do not apply to a collection member (<paramref name="isCollection" />): a collection is
	///     always filled (empty when unregistered), so required and init-only are both fine.
	/// </summary>
	private static bool RejectsInjectedPropertyShape(
		IPropertySymbol property,
		IMethodSymbol setter,
		ImplInfo info,
		bool deferred,
		bool optional,
		bool isCollection,
		List<DiagnosticInfo> diagnostics)
	{
		LocationInfo? location = LocationInfo.From(property.Locations.FirstOrDefault());

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
			return true;
		}

		if (optional && !isCollection && property.IsRequired)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.OptionalPropertyIsRequired,
				location,
				new EquatableArray<string>([property.Name, DisplayInstance(info.ImplementationType),])));
			return true;
		}

		if (optional && !isCollection && setter.IsInitOnly)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.OptionalPropertyIsInitOnly,
				location,
				new EquatableArray<string>([property.Name, DisplayInstance(info.ImplementationType),])));
		}

		return false;
	}

	// The setter must be reachable from the container's object initializer, which is not a derived context.
	// Mirrors IsAccessibleConstructor: public always, internal/protected-internal only within the container's own
	// assembly, and protected/private-protected/private never (the container cannot reach them).
	private static bool IsAccessibleSetter(IMethodSymbol setter, INamedTypeSymbol containerSymbol)
		=> setter.DeclaredAccessibility switch
		{
			Accessibility.Public => true,
			Accessibility.Internal or Accessibility.ProtectedOrInternal =>
				SymbolEqualityComparer.Default.Equals(setter.ContainingAssembly, containerSymbol.ContainingAssembly),
			_ => false,
		};

	/// <summary>
	///     Classifies a constructor parameter as a runtime argument (<c>[Arg]</c>), a deferred relationship type
	///     (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c> or <c>Func&lt;TArg…, T&gt;</c>) or a direct dependency,
	///     returning the underlying service type it resolves. A <c>Func&lt;TArg…, T&gt;</c> also carries the
	///     leading runtime-argument types it supplies to the produced service's <c>[Arg]</c> parameters. Only one
	///     level of nesting is supported: a relationship over another (e.g. <c>Func&lt;Func&lt;T&gt;&gt;</c>) is a
	///     direct dependency, so it surfaces as an unregistered service type rather than a misleading diagnostic
	///     about the inner relationship.
	/// </summary>
	private static ParameterModel ClassifyParameter(IParameterSymbol parameter, bool asyncFactory)
	{
		LocationInfo? location = LocationInfo.From(parameter.Locations.FirstOrDefault());

		// An explicit [FromServices] parameter resolves from the external provider; its own type is the external
		// service type, and a [FromKey] on it selects the keyed external service. It takes precedence so the
		// parameter is never treated as an Awaiten graph edge ([FromServices] with [Arg] is AWT134 in
		// ClassifyParameters). It is a constructor-parameter concern only, so it lives here rather than in the
		// shared ClassifyDependency core (an injected property never resolves from the external provider).
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
	///     their async siblings or a bare <c>Owned&lt;T&gt;</c> / <c>Task&lt;T&gt;</c>), a collection, or a direct
	///     dependency. Returns the underlying service type it resolves and an optional <c>[FromKey]</c> selection.
	///     The property path reuses this verbatim, so a member resolves exactly like a constructor parameter.
	/// </summary>
	private static ParameterModel ClassifyDependency(ITypeSymbol type, ImmutableArray<AttributeData> attributes, bool asyncFactory, LocationInfo? location)
	{
		if (HasArgAttribute(attributes))
		{
			return new ParameterModel(type.ToDisplayString(FullyQualified), DependencyKind.Arg, Location: location);
		}

		// An asynchronous factory's CancellationToken parameter is not resolved from the graph: the container
		// forwards the resolve-time token. Limited to async factories, since only they run on the async path where
		// that token exists; a synchronous factory or constructor has no ambient token to forward, so its
		// CancellationToken stays an ordinary dependency and is AWT101 when unregistered rather than silently
		// receiving default. An [Arg] CancellationToken is handled above as a runtime argument and left untouched.
		if (asyncFactory
		    && type is INamedTypeSymbol { Name: "CancellationToken", } token
		    && token.ContainingNamespace?.ToDisplayString() == "System.Threading")
		{
			return new ParameterModel(
				type.ToDisplayString(FullyQualified), DependencyKind.CancellationToken, Location: location);
		}

		// A [FromKey] selects the keyed registration of the dependency's service type, whether required directly,
		// deferred behind a Func<T>/Lazy<T>, wrapped in an Owned<T> handle, or a collection. The service type is
		// the same, only the delivery differs.
		string? key = FromKey(attributes);

		// An asynchronous collection (IAsyncEnumerable<T>) resolves to every registration of its element type, like
		// the synchronous shapes below, but awaits each member's initialization, so it is the one shape through
		// which an async-tainted member is legal. Recognized before the synchronous shapes (both live in
		// System.Collections.Generic) and before the relationship gate.
		if (IsAsyncEnumerable(type, out string? asyncElementType))
		{
			return new ParameterModel(asyncElementType!, DependencyKind.AsyncEnumerable, Key: key, Location: location);
		}

		// A keyed collection (IReadOnlyDictionary<TKey, T>) resolves every keyed registration of its value type T,
		// keyed by each registration's [Key]. Recognized before the plain collection shapes (both live in
		// System.Collections.Generic). The [FromKey] key is carried only for suppression; a [FromKey] that survives
		// is AWT160. The requested key type is stored so the dictionary is synthesized under it (string or enum),
		// including the empty index; a key type that is neither, or that mismatches the registrations, is AWT159
		// (still classified here, so an empty index is not misreported as AWT101).
		if (TryGetKeyedCollectionElement(type, out string? keyedService, out ITypeSymbol? keyedKeyType))
		{
			return new ParameterModel(
				keyedService!, DependencyKind.KeyedCollection, Key: key, Location: location,
				KeyType: DictionaryKeyTypeDisplay(keyedKeyType!));
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
		// async-initialized member behind the returned task. It is the eager awaited sibling of the synchronous
		// collection, and the second shape (besides IAsyncEnumerable<T>) through which an async-tainted member is
		// legal. Recognized before the bare Task<T> relationship below, so Task<IReadOnlyList<T>> is the awaited
		// collection of T rather than a Task relationship over the (unregistered) collection type itself.
		if (TryGetAwaitedCollection(type, out string? awaitedElement, out string? awaitedCollection))
		{
			return new ParameterModel(
				awaitedElement!, DependencyKind.AwaitedEnumerable, Key: key, Location: location,
				AwaitedCollectionType: awaitedCollection);
		}

		// An awaited keyed collection (Task<IReadOnlyDictionary<TKey, T>>) is the keyed analogue of the awaited
		// collection above: it resolves every keyed registration of T behind the returned task, awaiting each
		// async-initialized member. Recognized before the bare Task<T> relationship so it is not mistaken for a Task
		// over the unregistered dictionary type. The declared dictionary type is carried for suppression and
		// emission; [FromKey] and key-type handling match the synchronous dictionary (AWT159/AWT160).
		if (TryGetAwaitedKeyedCollection(type, out string? awaitedKeyedService, out ITypeSymbol? awaitedKeyedKeyType, out string? awaitedKeyedDictionary))
		{
			return new ParameterModel(
				awaitedKeyedService!, DependencyKind.AwaitedKeyedCollection, Key: key, Location: location,
				AwaitedCollectionType: awaitedKeyedDictionary, KeyType: DictionaryKeyTypeDisplay(awaitedKeyedKeyType!));
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
	///     Classifies a <c>System</c> generic as the single-level relationship it defers (<c>Lazy&lt;T&gt;</c>,
	///     <c>Func&lt;T&gt;</c> or <c>Func&lt;TArg…, T&gt;</c>, the latter optionally producing an
	///     <c>Owned&lt;T&gt;</c> disposal handle), returning the underlying service type. A type that is not a
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

	/// <summary>
	///     Redirects an unkeyed direct dependency to the synthetic context key of a <c>WhenInjectedInto</c>
	///     registration that targets this consumer, so the consumer resolves the contextual implementation while
	///     every other resolution keeps the unconditional one. Consuming the key marks the binding applied (else
	///     AWT167). Shared by the constructor-parameter and <c>[Inject]</c>-property paths.
	/// </summary>
	private static ParameterModel RedirectContextualBinding(
		ParameterModel model,
		ImplInfo info,
		Dictionary<ServiceKey, string> serviceToImpl,
		HashSet<ServiceKey> consumedConditionals)
	{
		if (model is not { Kind: DependencyKind.Direct, Key: null, })
		{
			return model;
		}

		ServiceKey contextKey = new(model.ServiceType, ContextKey(info.ImplementationType));
		if (!serviceToImpl.ContainsKey(contextKey))
		{
			return model;
		}

		consumedConditionals.Add(contextKey);
		return model with { Key = contextKey.Key, };
	}

	private static ServiceKey KeyOf(ParameterModel parameter) => new(parameter.ServiceType, parameter.Key);

	private static string DisplayKeyed(string serviceType, string? key)
		=> key is null ? Display(serviceType) : $"{Display(serviceType)} (key: {KeyDisplay(key)})";

	private static string? FromKey(ImmutableArray<AttributeData> attributes)
		=> TryGetAwaitenAttribute(attributes, "FromKeyAttribute", out AttributeData? attribute)
		   && attribute!.ConstructorArguments.Length == 1
			? EncodeKeyConstant(attribute.ConstructorArguments[0])
			: null;

	/// <summary>
	///     Whether a type is a <c>System.Threading.Tasks.Task&lt;T&gt;</c>, yielding its result type T. Used to
	///     recognize the async relationship types (<c>Task&lt;T&gt;</c>, <c>Func&lt;…, Task&lt;T&gt;&gt;</c>,
	///     <c>Lazy&lt;Task&lt;T&gt;&gt;</c>). <c>ValueTask&lt;T&gt;</c> is deliberately not a relationship type (a
	///     stored ValueTask may only be awaited once); it is supported solely as an async factory's return type, on
	///     the producer side.
	/// </summary>
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
	///     Recognizes a collection dependency (an array <c>T[]</c> or one of the standard generic collection
	///     interfaces <c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>,
	///     <c>IReadOnlyCollection&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>), yielding the
	///     fully-qualified element type. An array satisfies every one of these, so the emitter materializes all of
	///     them as an array.
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
	///     Recognizes a keyed collection (<c>IReadOnlyDictionary&lt;TKey, T&gt;</c>), yielding the fully-qualified
	///     service (value) type <c>T</c> and the declared key type symbol. <c>string</c> and enum key types synthesize;
	///     any other key type is still recognized here so the caller reports AWT159 rather than treating the dependency
	///     as a plain, unregistered generic service and misreporting AWT101.
	/// </summary>
	private static bool TryGetKeyedCollectionElement(ITypeSymbol type, out string? serviceType, out ITypeSymbol? keyType)
	{
		if (type is INamedTypeSymbol { IsGenericType: true, Name: "IReadOnlyDictionary", TypeArguments.Length: 2, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
		{
			keyType = named.TypeArguments[0];
			serviceType = named.TypeArguments[1].ToDisplayString(FullyQualified);
			return true;
		}

		serviceType = null;
		keyType = null;
		return false;
	}

	/// <summary>
	///     Reports <see cref="Diagnostics.UnsupportedKeyedCollectionKey">AWT159</see> when a synthesized keyed
	///     collection cannot be synthesized under its requested key type: the key type is neither <c>string</c> nor an
	///     enum, or it is one of those but the service's keyed registrations do not all carry a key of that kind (mixed
	///     kinds have no coherent dictionary). Called after <see cref="SuppressRegisteredCollectionSynthesis" />, so an
	///     explicitly registered dictionary (rewritten to Direct) is not reported.
	/// </summary>
	private static void ReportUnsupportedKeyedCollectionKey(
		ParameterModel dependency, ITypeSymbol type, ImplInfo info, Dictionary<ServiceKey, string> serviceToImpl, List<DiagnosticInfo> diagnostics)
	{
		if (dependency.Kind is not (DependencyKind.KeyedCollection or DependencyKind.AwaitedKeyedCollection)
		    || KeyedDependencyKeyType(type) is not { } keyType)
		{
			return;
		}

		string requested = Display(keyType.ToDisplayString(FullyQualified));
		string? reason = SupportedDictionaryKeyType(keyType) is { } supported
			? MismatchedKeyedRegistration(dependency.ServiceType, supported, serviceToImpl)
			: $"is not supported; keyed dictionaries support only string and enum key types";

		if (reason is not null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.UnsupportedKeyedCollectionKey,
				dependency.Location ?? info.Location,
				new EquatableArray<string>([DisplayInstance(info.ImplementationType), requested, reason,])));
		}
	}

	// The declared key type symbol of a keyed-dictionary dependency, whether synchronous (IReadOnlyDictionary<TKey, T>)
	// or awaited (Task<IReadOnlyDictionary<TKey, T>>), or null for neither shape. Lets the AWT159 report read the key
	// type off either form.
	private static ITypeSymbol? KeyedDependencyKeyType(ITypeSymbol type)
	{
		if (TryGetKeyedCollectionElement(type, out _, out ITypeSymbol? keyType))
		{
			return keyType;
		}

		return TryGetAwaitedKeyedCollection(type, out _, out keyType, out _) ? keyType : null;
	}

	// The offending registration reason when the service's keyed members do not all match the requested (supported)
	// dictionary key type ("string" or a fully-qualified enum), or null when they do (or there are none: an empty
	// index synthesizes an empty dictionary of any supported key type). Names the first mismatching implementation.
	private static string? MismatchedKeyedRegistration(string serviceType, string supported, Dictionary<ServiceKey, string> serviceToImpl)
	{
		foreach (KeyValuePair<ServiceKey, string> entry in serviceToImpl)
		{
			if (entry.Key.Service != serviceType || !IsUserKey(entry.Key.Key))
			{
				continue;
			}

			bool matches = supported == "string" ? IsStringKey(entry.Key.Key!) : EnumKeyType(entry.Key.Key!) == supported;
			if (!matches)
			{
				return $"does not match the keyed registrations of '{Display(serviceType)}': '{DisplayInstance(entry.Value)}' is registered under a key of a different kind";
			}
		}

		return null;
	}

	/// <summary>
	///     Reports <see cref="Diagnostics.FromKeyOnKeyedCollection">AWT160</see> when a synthesized keyed collection
	///     carries a <c>[FromKey]</c>: the synthesized dictionary resolves every keyed registration, so a key
	///     selection cannot apply. Called after <see cref="SuppressRegisteredCollectionSynthesis" />, so a dictionary
	///     explicitly registered under that key (where <c>[FromKey]</c> legitimately selects it) is not reported.
	/// </summary>
	private static void ReportFromKeyOnKeyedCollection(ParameterModel dependency, ITypeSymbol type, ImplInfo info, List<DiagnosticInfo> diagnostics)
	{
		if (dependency is { Kind: DependencyKind.KeyedCollection or DependencyKind.AwaitedKeyedCollection, Key: not null, })
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.FromKeyOnKeyedCollection,
				dependency.Location ?? info.Location,
				new EquatableArray<string>([
					DisplayInstance(info.ImplementationType),
					KeyDisplay(dependency.Key!),
					Display(type.ToDisplayString(FullyQualified)),
				])));
		}
	}

	/// <summary>
	///     Recognizes an awaited collection: a <c>Task&lt;C&gt;</c> whose result <c>C</c> is one of the collection
	///     shapes recognized by <see cref="TryGetCollectionElement" /> (e.g. <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c>
	///     or <c>Task&lt;T[]&gt;</c>), yielding the fully-qualified element type and inner collection type.
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

	/// <summary>
	///     Recognizes an awaited keyed collection (<c>Task&lt;IReadOnlyDictionary&lt;TKey, T&gt;&gt;</c>), yielding
	///     the fully-qualified service (value) type <c>T</c>, the declared key type, and the inner dictionary type.
	///     The keyed analogue of <see cref="TryGetAwaitedCollection" />; like the synchronous dictionary a
	///     non-<c>string</c> key type is still recognized here so the caller reports AWT159 rather than treating the
	///     dependency as a bare <c>Task&lt;T&gt;</c> relationship over an unregistered dictionary type.
	///     <c>ValueTask&lt;…&gt;</c> is deliberately not recognized, matching <see cref="TryGetAwaitedCollection" />.
	/// </summary>
	private static bool TryGetAwaitedKeyedCollection(ITypeSymbol type, out string? serviceType, out ITypeSymbol? keyType, out string? dictionaryType)
	{
		if (IsTask(type, out ITypeSymbol result) && TryGetKeyedCollectionElement(result, out serviceType, out keyType))
		{
			dictionaryType = result.ToDisplayString(FullyQualified);
			return true;
		}

		serviceType = null;
		keyType = null;
		dictionaryType = null;
		return false;
	}

	/// <summary>
	///     Whether a type is a <c>System.Collections.Generic.IAsyncEnumerable&lt;T&gt;</c> asynchronous collection,
	///     yielding its fully-qualified element type T. The one collection shape that awaits its members, so it is
	///     classified apart from the synchronous shapes in <c>TryGetCollectionElement</c> (which materialize eagerly
	///     into an array).
	/// </summary>
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

	/// <summary>
	///     The fully-qualified <c>IAsyncEnumerable&lt;T&gt;</c> shape of <paramref name="elementType" />, in the exact
	///     form registrations are stored under, so a membership check against <c>serviceToImpl</c> recognizes an
	///     explicitly registered async-collection type (the async analogue of <c>CollectionShapeTypes</c>). Its own
	///     shape, so a single string rather than a set.
	/// </summary>
	internal static string AsyncEnumerableShapeType(string elementType)
		=> $"global::System.Collections.Generic.IAsyncEnumerable<{elementType}>";

	/// <summary>
	///     The fully-qualified type strings of every collection shape of <paramref name="elementType" /> (the five
	///     generic collection interfaces and the rank-1 array), in the exact form registrations are stored under, so a
	///     membership check against <c>serviceToImpl</c> recognizes an explicitly registered collection type. Shared
	///     by the generator (injection classification) and the emitter (public dispatch) so the two never drift.
	/// </summary>
	internal static IEnumerable<string> CollectionShapeTypes(string elementType)
	{
		yield return $"global::System.Collections.Generic.IEnumerable<{elementType}>";
		yield return $"global::System.Collections.Generic.IReadOnlyList<{elementType}>";
		yield return $"global::System.Collections.Generic.IReadOnlyCollection<{elementType}>";
		yield return $"global::System.Collections.Generic.IList<{elementType}>";
		yield return $"global::System.Collections.Generic.ICollection<{elementType}>";
		yield return $"{elementType}[]";
	}

	/// <summary>Whether a type is an <c>Awaiten.Owned&lt;T&gt;</c> disposal handle, yielding the owned service type T.</summary>
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

	/// <summary>
	///     Whether a factory parameter is marked <c>[RequestingType]</c>, so it receives the requesting consumer's
	///     typeof(…) rather than being resolved from the graph.
	/// </summary>
	private static bool HasRequestingType(ImmutableArray<AttributeData> attributes)
		=> HasAwaitenAttribute(attributes, "RequestingTypeAttribute");

	/// <summary>
	///     Whether a type is exactly <c>global::System.Type</c> (the required type of a <c>[RequestingType]</c>
	///     parameter; any other type is AWT162). Matched structurally so a user-defined System.Type in a nested
	///     namespace does not qualify.
	/// </summary>
	private static bool IsSystemType(ITypeSymbol type)
		=> type is { Name: "Type", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true, }, };

	private static bool HasInject(ImmutableArray<AttributeData> attributes)
		=> HasAwaitenAttribute(attributes, "InjectAttribute");

	/// <summary>
	///     Whether an <c>[Inject]</c> attribute sets Deferred = true, so the member is assigned after construction and
	///     caching (breaking a mutual constructor cycle) rather than filled inside the object initializer.
	/// </summary>
	private static bool IsInjectDeferred(ImmutableArray<AttributeData> attributes)
		=> InjectFlag(attributes, "Deferred");

	/// <summary>
	///     Whether an <c>[Inject]</c> attribute sets Optional = true, so the member is left unassigned (rather than
	///     reported as AWT101) when its service type is not registered.
	/// </summary>
	private static bool IsInjectOptional(ImmutableArray<AttributeData> attributes)
		=> InjectFlag(attributes, "Optional");

	/// <summary>The value of a named bool flag on the <c>[Inject]</c> attribute (Deferred/Optional), false when absent.</summary>
	private static bool InjectFlag(ImmutableArray<AttributeData> attributes, string flag)
	{
		if (!TryGetAwaitenAttribute(attributes, "InjectAttribute", out AttributeData? attribute) || attribute is null)
		{
			return false;
		}

		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == flag && argument.Value.Value is bool value)
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

	/// <summary>
	///     Whether a parameter is marked <c>[FromServices]</c>, so it is resolved from the container's external
	///     provider rather than the Awaiten graph.
	/// </summary>
	private static bool HasFromServices(IParameterSymbol parameter)
		=> HasAwaitenAttribute(parameter.GetAttributes(), "FromServicesAttribute");

	/// <summary>
	///     Whether the container is marked <c>[ImportServices]</c>, so every otherwise-unresolved direct dependency is
	///     satisfied from the external provider rather than reported as missing.
	/// </summary>
	private static bool ContainerImportsServices(INamedTypeSymbol containerSymbol)
		=> HasAwaitenAttribute(containerSymbol.GetAttributes(), "ImportServicesAttribute");

	private static bool IsRelationshipType(ITypeSymbol type)
		=> type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, Name: "Func" or "Lazy", } named
		   && named.ContainingNamespace?.ToDisplayString() == "System";
}
