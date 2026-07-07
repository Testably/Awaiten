using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     The prefix for a decorator chain link's synthetic key and identity. <c>DisplayInstance</c> keys off it to
	///     trim the synthetic suffix from diagnostics. Kept on the enclosing type so both <c>DecoratorChainBuilder</c>
	///     and <c>DisplayInstance</c> reach it (a nested type's private member is not visible here).
	/// </summary>
	private const string DecoratorKeyPrefix = "__dec:";

	/// <summary>
	///     Builds the decorator chains after coalescing. Over each base impl (every collection member, or the
	///     single-dispatch winner) it synthesizes a chain of synthetic-keyed links: the base moves onto a synthetic
	///     key, each decorator's inner parameter is redirected to the next-lower key, and the public winner is
	///     rewritten to the outermost decorator, so the resolver produces <c>D2(D1(Real))</c>. Collection membership
	///     is rewritten to the chain. Reports AWT123 (nothing to decorate) and AWT124 (no single inner parameter).
	/// </summary>
	private sealed class DecoratorChainBuilder
	{
		private readonly INamedTypeSymbol _containerSymbol;
		private readonly Compilation _compilation;
		private readonly Dictionary<ServiceKey, string> _serviceToImpl;
		private readonly List<ImplInfo> _implOrder;
		private readonly Dictionary<ServiceKey, List<string>> _serviceMembers;
		private readonly Dictionary<string, DecoratorInner> _decoratorInner;
		private readonly ExternalSurface _external;
		private readonly List<DiagnosticInfo> _diagnostics;

		/// <summary>
		///     The coalesced implementations by identity, so a base impl's <c>ImplInfo</c> can be moved onto a
		///     synthetic key and each new chain link's <c>ImplInfo</c> can be appended for <c>BuildInstance</c> to build.
		/// </summary>
		private readonly Dictionary<string, ImplInfo> _byImpl;

		public DecoratorChainBuilder(
			INamedTypeSymbol containerSymbol,
			Compilation compilation,
			CoalescedGraph graph,
			Dictionary<string, DecoratorInner> decoratorInner,
			ExternalSurface external,
			List<DiagnosticInfo> diagnostics)
		{
			_containerSymbol = containerSymbol;
			_compilation = compilation;
			_serviceToImpl = graph.ServiceToImpl;
			_implOrder = graph.ImplOrder;
			_serviceMembers = graph.ServiceMembers;
			_decoratorInner = decoratorInner;
			_external = external;
			_diagnostics = diagnostics;

			_byImpl = new Dictionary<string, ImplInfo>(StringComparer.Ordinal);
			foreach (ImplInfo info in _implOrder)
			{
				_byImpl[info.ImplementationType] = info;
			}
		}

		public void Build(List<DecorateRegistration> decorators)
		{
			foreach ((string service, List<DecorateRegistration> chain) in GroupByService(decorators))
			{
				ProcessService(service, chain);
			}
		}

		/// <summary>Groups decorators by decorated service, preserving first-seen service order for determinism.</summary>
		private static List<(string Service, List<DecorateRegistration> Chain)> GroupByService(List<DecorateRegistration> decorators)
		{
			List<(string, List<DecorateRegistration>)> ordered = new();
			Dictionary<string, List<DecorateRegistration>> byService = new(StringComparer.Ordinal);
			foreach (DecorateRegistration decorator in decorators)
			{
				if (!byService.TryGetValue(decorator.Service, out List<DecorateRegistration>? list))
				{
					list = new List<DecorateRegistration>();
					byService.Add(decorator.Service, list);
					ordered.Add((decorator.Service, list));
				}

				list.Add(decorator);
			}

			return ordered;
		}

		/// <summary>
		///     The base implementations to wrap: every unkeyed collection member (so the collection view is also
		///     decorated), or the single-dispatch winner when the service has no collection membership.
		/// </summary>
		private static List<string> CollectBaseImpls(List<string>? members, string? winner)
		{
			if (members is { Count: > 0, })
			{
				return new List<string>(members);
			}

			return winner is null ? new List<string>() : new List<string> { winner, };
		}

		private void ProcessService(string service, List<DecorateRegistration> chain)
		{
			// The public (unkeyed) resolution, then each contextual (WhenInjectedInto) resolution of the service, so
			// a contextually-bound implementation is wrapped by the same chain as the default. Snapshotted and sorted
			// before wrapping mutates _serviceToImpl (and for deterministic synthetic keys).
			List<string?> targets = new() { null, };
			targets.AddRange(ContextTargetKeys(service));

			// AWT123: the decorated service has no registration (under any resolution key) to wrap.
			if (targets.All(key => TargetBaseImpls(service, key).Count == 0))
			{
				Report(Diagnostics.DecoratedServiceNotRegistered, chain[0].Location,
					service, chain[0].Decorator.ToDisplayString(FullyQualified));
				return;
			}

			// Order the chain by (Order, declaration): innermost first, outermost last.
			List<DecorateRegistration> ordered = chain
				.OrderBy(d => d.Order)
				.ThenBy(d => d.DeclarationOrder)
				.ToList();

			// AWT124: each decorator must have exactly one constructor parameter assignable to the service.
			List<string>? innerParameterTypes = ResolveInnerParameterTypes(service, ordered);
			if (innerParameterTypes is null)
			{
				return;
			}

			int baseIndex = 0;
			foreach (string? resolutionKey in targets)
			{
				_serviceToImpl.TryGetValue(new ServiceKey(service, resolutionKey), out string? winner);
				List<string>? members = resolutionKey is null ? MembersOf(service) : null;
				ServiceChain sc = new(service, resolutionKey, winner, members, ordered, innerParameterTypes);
				foreach (string baseImpl in TargetBaseImpls(service, resolutionKey))
				{
					WrapBaseImpl(sc, baseImpl, baseIndex++);
				}
			}
		}

		/// <summary>
		///     The base implementations to wrap under a resolution key: the unkeyed key's collection members (so the
		///     collection view is decorated too), or the single winner for a contextual key (which has no public
		///     collection). A snapshot, since wrapping mutates the underlying membership.
		/// </summary>
		private List<string> TargetBaseImpls(string service, string? resolutionKey)
		{
			_serviceToImpl.TryGetValue(new ServiceKey(service, resolutionKey), out string? winner);
			return CollectBaseImpls(resolutionKey is null ? MembersOf(service) : null, winner);
		}

		private List<string>? MembersOf(string service)
			=> _serviceMembers.TryGetValue(new ServiceKey(service, null), out List<string>? members) ? members : null;

		/// <summary>
		///     The synthetic contextual (WhenInjectedInto) keys of a service, sorted for deterministic wrapping. Each
		///     is a separate resolution slot the consumer redirects to, wrapped by the same decorator chain.
		/// </summary>
		private List<string> ContextTargetKeys(string service)
		{
			List<string> keys = new();
			foreach (ServiceKey key in _serviceToImpl.Keys)
			{
				if (key.Service == service && key.Key is { } k && k.StartsWith(ContextKeyPrefix, StringComparison.Ordinal))
				{
					keys.Add(k);
				}
			}

			keys.Sort(StringComparer.Ordinal);
			return keys;
		}

		/// <summary>
		///     Each decorator's inner-parameter type in chain order, or <see langword="null" /> (having reported
		///     AWT124) when any decorator has no single constructor parameter that can receive the inner instance.
		/// </summary>
		private List<string>? ResolveInnerParameterTypes(string service, List<DecorateRegistration> ordered)
		{
			List<string> innerParameterTypes = new();
			bool valid = true;
			foreach (DecorateRegistration decorator in ordered)
			{
				string? innerType = SingleInnerParameterType(decorator.Decorator, decorator.ServiceSymbol);
				if (innerType is null)
				{
					Report(Diagnostics.DecoratorMissingInnerParameter, decorator.Location,
						decorator.Decorator.ToDisplayString(FullyQualified), service);
					valid = false;
					continue;
				}

				innerParameterTypes.Add(innerType);
			}

			return valid ? innerParameterTypes : null;
		}

		private void WrapBaseImpl(ServiceChain chain, string baseImpl, int baseIndex)
		{
			string service = chain.Service;
			bool isWinnerChain = baseImpl == chain.Winner;

			// Move the base implementation off the resolution key onto the chain's lowest synthetic key, so the
			// first decorator reaches it by key and the resolution no longer hits it directly.
			string baseKey = DecoratorKey(service, baseIndex, 0);
			MoveBaseToSyntheticKey(chain.ResolutionKey, service, baseImpl, baseKey);

			ImplInfo? baseInfo = _byImpl.TryGetValue(baseImpl, out ImplInfo? bi) ? bi : null;
			Lifetime lifetime = baseInfo?.Lifetime ?? Lifetime.Transient;
			LocationInfo? location = baseInfo?.Location;

			// Register each decorator as a fresh instance with a synthetic identity (so one decorator type can
			// wrap several base impls as distinct instances), inheriting the base registration's lifetime.
			string innerKey = baseKey;
			string outermostIdentity = baseImpl;
			for (int i = 0; i < chain.Ordered.Count; i++)
			{
				bool isPublic = isWinnerChain && i == chain.Ordered.Count - 1;
				outermostIdentity = AddChainLink(chain, baseIndex, i, isPublic, lifetime, location, innerKey);
				innerKey = DecoratorKey(service, baseIndex, i + 1);
			}

			// Collection membership is unbypassable: replace the base impl with the chain's outermost decorator
			// identity, so a collection view yields the full chain as a single element rather than the bare impl.
			RewriteCollectionMembership(chain.Members, baseImpl, outermostIdentity);
		}

		private void MoveBaseToSyntheticKey(string? resolutionKey, string service, string baseImpl, string baseKey)
		{
			if (_byImpl.TryGetValue(baseImpl, out ImplInfo? baseInfo))
			{
				baseInfo.Services.Remove(new ServiceKey(service, resolutionKey));
				ServiceKey synthetic = new(service, baseKey);
				if (!baseInfo.Services.Contains(synthetic))
				{
					baseInfo.Services.Add(synthetic);
				}
			}

			_serviceToImpl[new ServiceKey(service, baseKey)] = baseImpl;
		}

		/// <summary>
		///     Registers one chain link and returns its synthetic identity. The outermost link of the winner chain
		///     (<c>isPublic</c>) takes the target resolution key (null for public dispatch, a context key for a
		///     contextual binding); every other link is keyed so it is reached only as the inner of the link above it
		///     (or as a rewritten collection member).
		/// </summary>
		private string AddChainLink(ServiceChain chain, int baseIndex, int linkIndex, bool isPublic, Lifetime lifetime, LocationInfo? location, string innerKey)
		{
			string service = chain.Service;
			int link = linkIndex + 1;
			INamedTypeSymbol decorator = chain.Ordered[linkIndex].Decorator;
			string identity = DecoratorIdentity(decorator.ToDisplayString(FullyQualified), service, baseIndex, link);

			ImplInfo info = new(identity, decorator, lifetime, location, ProductionKind.Constructor, null);
			_byImpl[identity] = info;
			_implOrder.Add(info);

			ServiceKey ownKey = isPublic
				? new ServiceKey(service, chain.ResolutionKey)
				: new ServiceKey(service, DecoratorKey(service, baseIndex, link));
			info.Services.Add(ownKey);
			_serviceToImpl[ownKey] = identity;

			// Redirect this link's inner parameter to the link below it. The chain links are all registered under
			// the decorated service type, so the redirect keys to (service, innerKey), not to the parameter's own
			// declared type, which may be a base of the service and is not a registration key.
			_decoratorInner[identity] = new DecoratorInner(chain.InnerParameterTypes[linkIndex], service, innerKey);

			return identity;
		}

		private static void RewriteCollectionMembership(List<string>? members, string baseImpl, string outermostIdentity)
		{
			int index = members?.IndexOf(baseImpl) ?? -1;
			if (index >= 0)
			{
				members![index] = outermostIdentity;
			}
		}

		private void Report(DiagnosticDescriptor descriptor, Location? location, params string[] messageArgs)
		{
			string[] displayed = new string[messageArgs.Length];
			for (int i = 0; i < messageArgs.Length; i++)
			{
				displayed[i] = Display(messageArgs[i]);
			}

			_diagnostics.Add(new DiagnosticInfo(descriptor, LocationInfo.From(location), new EquatableArray<string>(displayed)));
		}

		/// <summary>
		///     The per-target state threaded through <c>WrapBaseImpl</c> / <c>AddChainLink</c>: the decorated service,
		///     the resolution key being wrapped (null for the public dispatch, a synthetic context key for a
		///     contextual binding), that key's winner and collection membership (if any), the decorators
		///     innermost-first, and each decorator's inner-parameter type (positionally matching <c>Ordered</c>).
		/// </summary>
		private readonly record struct ServiceChain(
			string Service,
			string? ResolutionKey,
			string? Winner,
			List<string>? Members,
			List<DecorateRegistration> Ordered,
			List<string> InnerParameterTypes);

		/// <summary>
		///     The fully-qualified type of a decorator's single constructor parameter that receives the inner
		///     instance, or <see langword="null" /> when there is none or it is ambiguous (AWT124). Uses the same
		///     <see cref="SelectConstructor" /> the container builds through, and returns the type string
		///     <see cref="ClassifyParameter" /> produces so the redirect can match it.
		/// </summary>
		private string? SingleInnerParameterType(INamedTypeSymbol decorator, INamedTypeSymbol service)
		{
			IMethodSymbol? constructor = SelectConstructor(decorator, _containerSymbol, _serviceToImpl.Keys.Select(k => k.Service), _external);
			if (constructor is null)
			{
				return null;
			}

			// The inner parameter must accept the decorated service (implicitly convertible from it). A [FromKey]
			// parameter is excluded, since it selects a separate keyed dependency. When several parameters are
			// assignable, the inner is the most-derived one (every other is a base of it); a tie is ambiguous and
			// reported as AWT124.
			List<IParameterSymbol> assignable = constructor.Parameters
				.Where(p => FromKey(p.GetAttributes()) is null && _compilation.HasImplicitConversion(service, p.Type))
				.ToList();

			if (assignable.Count == 0)
			{
				return null;
			}

			IParameterSymbol? inner = null;
			foreach (IParameterSymbol candidate in assignable)
			{
				bool isMaximum = assignable.All(other =>
					ReferenceEquals(other, candidate) || _compilation.HasImplicitConversion(candidate.Type, other.Type));
				if (!isMaximum)
				{
					continue;
				}

				if (inner is not null)
				{
					return null;
				}

				inner = candidate;
			}

			return inner?.Type.ToDisplayString(FullyQualified);
		}

		private static string DecoratorKey(string service, int baseIndex, int link)
			=> $"{DecoratorKeyPrefix}{service}:{baseIndex}:{link}";

		private static string DecoratorIdentity(string decoratorType, string service, int baseIndex, int link)
			=> $"{decoratorType}@{DecoratorKeyPrefix}{service}:{baseIndex}:{link}";
	}

	/// <summary>
	///     Builds the composites after coalescing (and after decorator chains, so a composite fronts the decorated
	///     members). Each <c>[Composite&lt;TComposite, TService&gt;]</c> is registered as an ordinary instance and
	///     rewritten to the public winner of <c>TService</c>. The composite is deliberately excluded from
	///     <c>serviceMembers</c>, so every collection of <c>TService</c> resolves to the other registrations, never
	///     the composite (no self-edge, so cycle/captive analysis is unchanged). Reports AWT130 (no collection
	///     parameter of the service), AWT133 (a collection of a base type), AWT132 (a second composite), and AWT131
	///     (the composite is also a bare member, which is dropped to keep the no-self-edge invariant).
	/// </summary>
	private static void BuildComposites(
		List<CompositeRegistration> composites,
		Compilation compilation,
		INamedTypeSymbol containerSymbol,
		CoalescedGraph graph,
		ExternalSurface external,
		List<DiagnosticInfo> diagnostics)
	{
		(Dictionary<ServiceKey, string> serviceToImpl, List<ImplInfo> implOrder, Dictionary<ServiceKey, List<string>> serviceMembers) = graph;

		// The coalesced implementations by identity (including decorator chain links), so the composite's
		// ImplInfo can be appended for BuildInstance to build and a former winner's public key removed.
		Dictionary<string, ImplInfo> byImpl = new(StringComparer.Ordinal);
		foreach (ImplInfo info in implOrder)
		{
			byImpl[info.ImplementationType] = info;
		}

		// The composite type chosen for each composed service, so a second composite for the same service is
		// caught (AWT132) rather than silently overwriting the first and leaving it as a dead built instance.
		Dictionary<string, string> compositeByService = new(StringComparer.Ordinal);

		foreach (CompositeRegistration composite in composites)
		{
			string compositeType = composite.Composite.ToDisplayString(FullyQualified);

			// AWT132: a service can have at most one composite façade; a later composite for it is skipped.
			if (IsDuplicateComposite(composite, compositeType, compositeByService, diagnostics))
			{
				continue;
			}

			// AWT130/AWT133: the composite must fan out over a collection of exactly the composed service.
			if (!ValidateCompositeCollection(composite, compositeType, containerSymbol, compilation, serviceToImpl, external, diagnostics))
			{
				continue;
			}

			ImplInfo compositeInfo = EnsureCompositeInstance(composite, compositeType, byImpl, implOrder);
			DropRedundantSelfMembership(composite, compositeType, serviceMembers, diagnostics);
			MakeCompositeThePublicWinner(composite, compositeType, compositeInfo, serviceToImpl, byImpl);
		}
	}

	/// <summary>
	///     AWT132: whether this composite duplicates one already chosen for its service (so the caller skips it),
	///     recording the first composite per service. A second composite of a DIFFERENT type is reported; the same
	///     type declared twice is idempotent and left silent.
	/// </summary>
	private static bool IsDuplicateComposite(
		CompositeRegistration composite,
		string compositeType,
		Dictionary<string, string> compositeByService,
		List<DiagnosticInfo> diagnostics)
	{
		if (!compositeByService.TryGetValue(composite.Service, out string? firstComposite))
		{
			compositeByService.Add(composite.Service, compositeType);
			return false;
		}

		if (firstComposite != compositeType)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.MultipleCompositesForService,
				LocationInfo.From(composite.Location),
				new EquatableArray<string>([Display(composite.Service),])));
		}

		return true;
	}

	/// <summary>
	///     Whether the composite's collection parameter is valid (the caller proceeds), reporting AWT130 for a missing
	///     collection parameter and AWT133 for a collection of a base type of the service (which, since collections are
	///     keyed by exact element type, would resolve a different collection than the composed service's registrations).
	/// </summary>
	private static bool ValidateCompositeCollection(
		CompositeRegistration composite,
		string compositeType,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		Dictionary<ServiceKey, string> serviceToImpl,
		ExternalSurface external,
		List<DiagnosticInfo> diagnostics)
	{
		switch (ClassifyCompositeCollection(composite.Composite, composite.ServiceSymbol, containerSymbol, compilation, serviceToImpl, external, out string? relatedElement))
		{
			case CompositeCollectionKind.Missing:
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.CompositeMissingCollectionParameter,
					LocationInfo.From(composite.Location),
					new EquatableArray<string>([
						Display(compositeType),
						Display(composite.Service),
					])));
				return false;

			case CompositeCollectionKind.RelatedElement:
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.CompositeCollectionNotOfComposedService,
					LocationInfo.From(composite.Location),
					new EquatableArray<string>([
						Display(compositeType),
						Display(relatedElement!),
						Display(composite.Service),
					])));
				return false;

			default:
				return true;
		}
	}

	/// <summary>
	///     Registers the composite as an ordinary instance (constructed, cached and disposed by index) and returns its
	///     <c>ImplInfo</c>, reusing the existing one if the type was already registered (idempotent when named twice or
	///     when the composite type is also a normal service).
	/// </summary>
	private static ImplInfo EnsureCompositeInstance(
		CompositeRegistration composite,
		string compositeType,
		Dictionary<string, ImplInfo> byImpl,
		List<ImplInfo> implOrder)
	{
		if (byImpl.TryGetValue(compositeType, out ImplInfo? compositeInfo))
		{
			return compositeInfo;
		}

		compositeInfo = new ImplInfo(
			compositeType, composite.Composite, composite.Lifetime,
			LocationInfo.From(composite.Location), ProductionKind.Constructor, null);
		byImpl.Add(compositeType, compositeInfo);
		implOrder.Add(compositeInfo);
		return compositeInfo;
	}

	/// <summary>
	///     AWT131: the composite type is also registered as a bare member of the service it composes (e.g. a
	///     <c>[Transient&lt;C, S&gt;]</c> alongside <c>[Composite&lt;C, S&gt;]</c>). A composite is excluded from its
	///     own fan-out, so drop it from every collection of the composed service and warn. Without the removal its own
	///     collection edge would include itself and surface as a confusing AWT102 dependency cycle.
	/// </summary>
	private static void DropRedundantSelfMembership(
		CompositeRegistration composite,
		string compositeType,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		List<DiagnosticInfo> diagnostics)
	{
		bool wasMember = false;
		foreach (KeyValuePair<ServiceKey, List<string>> entry in serviceMembers)
		{
			if (entry.Key.Service == composite.Service && entry.Value.Remove(compositeType))
			{
				wasMember = true;
			}
		}

		if (!wasMember)
		{
			return;
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.CompositeAlsoRegisteredAsMember,
			LocationInfo.From(composite.Location),
			new EquatableArray<string>([
				Display(compositeType),
				Display(composite.Service),
			])));
	}

	/// <summary>
	///     Makes the composite the public single-dispatch winner: takes the unkeyed service off whatever impl
	///     currently holds it (a former winner stays a collection member, just no longer the façade) and hands it to
	///     the composite. The composite is never a <c>serviceMembers</c> entry, so it stays excluded from every
	///     <c>IEnumerable&lt;TService&gt;</c>.
	/// </summary>
	private static void MakeCompositeThePublicWinner(
		CompositeRegistration composite,
		string compositeType,
		ImplInfo compositeInfo,
		Dictionary<ServiceKey, string> serviceToImpl,
		Dictionary<string, ImplInfo> byImpl)
	{
		ServiceKey publicKey = new(composite.Service, null);
		if (serviceToImpl.TryGetValue(publicKey, out string? previousWinner)
		    && previousWinner != compositeType
		    && byImpl.TryGetValue(previousWinner, out ImplInfo? previousInfo))
		{
			previousInfo.Services.Remove(publicKey);
		}

		serviceToImpl[publicKey] = compositeType;
		if (!compositeInfo.Services.Contains(publicKey))
		{
			compositeInfo.Services.Add(publicKey);
		}
	}

	/// <summary>Which collection-parameter shape a composite offers for its composed service.</summary>
	private enum CompositeCollectionKind
	{
		/// <summary>A collection parameter whose element type is exactly the composed service. Valid.</summary>
		Exact,

		/// <summary>
		///     A collection parameter of a base (or otherwise related) type of the composed service. Collections
		///     resolve by exact element type, so it would fan out over a different collection. Reported as AWT133.
		/// </summary>
		RelatedElement,

		/// <summary>No collection parameter of the composed service at all. Reported as AWT130.</summary>
		Missing,
	}

	/// <summary>
	///     Classifies the collection constructor parameter <paramref name="composite" /> offers for its composed
	///     <paramref name="service" />: <see cref="CompositeCollectionKind.Exact" /> for a collection of exactly the
	///     service, <see cref="CompositeCollectionKind.RelatedElement" /> (yielding the element in
	///     <paramref name="relatedElement" />) for a collection of a base type (which resolves a different collection),
	///     otherwise <see cref="CompositeCollectionKind.Missing" />. Exact wins over related. Uses the same
	///     <see cref="SelectConstructor" /> the container builds the composite through.
	/// </summary>
	private static CompositeCollectionKind ClassifyCompositeCollection(
		INamedTypeSymbol composite,
		INamedTypeSymbol service,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		Dictionary<ServiceKey, string> serviceToImpl,
		ExternalSurface external,
		out string? relatedElement)
	{
		relatedElement = null;
		IMethodSymbol? constructor = SelectConstructor(composite, containerSymbol, serviceToImpl.Keys.Select(k => k.Service), external);
		if (constructor is null)
		{
			return CompositeCollectionKind.Missing;
		}

		foreach (IParameterSymbol parameter in constructor.Parameters)
		{
			if (!TryGetCompositeCollectionElement(parameter.Type, out ITypeSymbol? element))
			{
				continue;
			}

			if (SymbolEqualityComparer.Default.Equals(element, service))
			{
				return CompositeCollectionKind.Exact;
			}

			if (relatedElement is null && compilation.HasImplicitConversion(service, element!))
			{
				relatedElement = element!.ToDisplayString(FullyQualified);
			}
		}

		return relatedElement is null ? CompositeCollectionKind.Missing : CompositeCollectionKind.RelatedElement;
	}

	/// <summary>
	///     The element type of a collection parameter (the rank-1 array element, or the single type argument of
	///     one of the standard generic collection interfaces), mirroring <see cref="TryGetCollectionElement" />
	///     but yielding the element symbol so the composed service's convertibility to it can be checked.
	/// </summary>
	private static bool TryGetCompositeCollectionElement(ITypeSymbol type, out ITypeSymbol? element)
	{
		if (type is IArrayTypeSymbol { Rank: 1, } array)
		{
			element = array.ElementType;
			return true;
		}

		if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } named
		    && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
		    && named.Name is "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection")
		{
			element = named.TypeArguments[0];
			return true;
		}

		element = null;
		return false;
	}

	/// <summary>
	///     Redirects a decorator chain link's single inner parameter to the synthetic key of the next-lower link, so
	///     the resolver produces <c>D2(D1(Real))</c> rather than the link resolving itself and recursing. Both the
	///     service type and key are rewritten (the links are registered under the decorated service type). Any other
	///     parameter is returned unchanged.
	/// </summary>
	private static ParameterModel RedirectDecoratorInner(
		ParameterModel parameterModel,
		ImplInfo info,
		Dictionary<string, DecoratorInner> decoratorInner)
	{
		if (parameterModel is { Kind: DependencyKind.Direct, Key: null, }
		    && decoratorInner.TryGetValue(info.ImplementationType, out DecoratorInner inner)
		    && parameterModel.ServiceType == inner.ParameterServiceType)
		{
			return parameterModel with { ServiceType = inner.ServiceType, Key = inner.InnerKey, };
		}

		return parameterModel;
	}
}
