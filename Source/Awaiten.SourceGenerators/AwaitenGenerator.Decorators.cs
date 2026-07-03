using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	// A decorator chain link's synthetic key and identity are built from this prefix; DisplayInstance also keys
	// off it to trim the synthetic suffix out of diagnostics. Kept on the enclosing type so both the nested
	// DecoratorChainBuilder and DisplayInstance can reach it (a nested type's private member is not visible here).
	private const string DecoratorKeyPrefix = "__dec:";

	/// <summary>
	///     Builds the decorator chains after coalescing. For each decorated service it locates the base
	///     implementation(s) — every collection member, or the single-dispatch winner — and over each base impl
	///     synthesizes a chain of synthetic-keyed links: the base implementation is moved onto a synthetic key
	///     (<c>__dec:S:k:0</c>), each decorator is registered as a fresh instance whose single inner parameter is
	///     redirected to the next-lower key, and the public unkeyed winner is rewritten to the outermost decorator.
	///     The existing keyed resolver then produces <c>D2(D1(Real))</c> for free. The collection membership of the
	///     service is rewritten so a collection view yields the decorated chain, never the bare base impl (the
	///     decorator is unbypassable). Reports AWT123 (nothing to decorate) and AWT124 (no single inner parameter).
	/// </summary>
	/// <remarks>
	///     Holds the mutable graph state (the coalesced <c>serviceToImpl</c>, <c>implOrder</c>, collection
	///     membership and the decorator-inner map) as fields so the chain-building steps read as small focused
	///     methods rather than one deeply nested loop.
	/// </remarks>
	private sealed class DecoratorChainBuilder
	{
		private readonly INamedTypeSymbol _containerSymbol;
		private readonly Compilation _compilation;
		private readonly Dictionary<ServiceKey, string> _serviceToImpl;
		private readonly List<ImplInfo> _implOrder;
		private readonly Dictionary<ServiceKey, List<string>> _serviceMembers;
		private readonly Dictionary<string, DecoratorInner> _decoratorInner;
		private readonly bool _importServices;
		private readonly List<DiagnosticInfo> _diagnostics;

		// The coalesced implementations by identity, so a base impl's ImplInfo can be moved onto a synthetic key
		// and each new chain link's ImplInfo can be appended for BuildInstance to build.
		private readonly Dictionary<string, ImplInfo> _byImpl;

		public DecoratorChainBuilder(
			INamedTypeSymbol containerSymbol,
			Compilation compilation,
			CoalescedGraph graph,
			Dictionary<string, DecoratorInner> decoratorInner,
			bool importServices,
			List<DiagnosticInfo> diagnostics)
		{
			_containerSymbol = containerSymbol;
			_compilation = compilation;
			_serviceToImpl = graph.ServiceToImpl;
			_implOrder = graph.ImplOrder;
			_serviceMembers = graph.ServiceMembers;
			_decoratorInner = decoratorInner;
			_importServices = importServices;
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

		// Group decorators by decorated service, preserving first-seen service order for determinism.
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

		// The base implementations to wrap: every unkeyed collection member (so the collection view is also
		// decorated), or the single-dispatch winner when the service has no collection membership.
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
			ServiceKey publicKey = new(service, null);
			_serviceToImpl.TryGetValue(publicKey, out string? winner);
			List<string>? members = _serviceMembers.TryGetValue(publicKey, out List<string>? m) ? m : null;
			List<string> baseImpls = CollectBaseImpls(members, winner);

			// AWT123: the decorated service has no registration to wrap.
			if (baseImpls.Count == 0)
			{
				Report(Diagnostics.DecoratedServiceNotRegistered, chain[0].Location,
					service, chain[0].Decorator.ToDisplayString(FullyQualified));
				return;
			}

			// Order the chain by (Order, declaration) — innermost first, outermost last.
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

			ServiceChain sc = new(service, winner, members, ordered, innerParameterTypes);
			for (int k = 0; k < baseImpls.Count; k++)
			{
				WrapBaseImpl(sc, baseImpls[k], k);
			}
		}

		// Each decorator's inner-parameter type in chain order, or null (having reported AWT124, or AWT135 when
		// the would-be inner is marked [FromServices]) when any decorator has no single constructor parameter
		// that can receive the inner instance.
		private List<string>? ResolveInnerParameterTypes(string service, List<DecorateRegistration> ordered)
		{
			List<string> innerParameterTypes = new();
			bool valid = true;
			foreach (DecorateRegistration decorator in ordered)
			{
				string? innerType = SingleInnerParameterType(decorator.Decorator, decorator.ServiceSymbol, out IParameterSymbol? externalInner);
				if (innerType is null)
				{
					// The would-be inner is marked [FromServices]: point at the offending parameter (falling back
					// to the [Decorate] registration when its location is unavailable) rather than reporting the
					// generic missing-inner AWT124, whose "add a parameter" guidance would mislead here.
					if (externalInner is not null)
					{
						Report(Diagnostics.ExternalDecoratorInner,
							externalInner.Locations.FirstOrDefault() ?? decorator.Location,
							externalInner.Name, decorator.Decorator.ToDisplayString(FullyQualified));
					}
					else
					{
						Report(Diagnostics.DecoratorMissingInnerParameter, decorator.Location,
							decorator.Decorator.ToDisplayString(FullyQualified), service);
					}

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

			// Move the base implementation off the public service onto the chain's lowest synthetic key, so the
			// first decorator reaches it by key and the public dispatch no longer hits it directly.
			string baseKey = DecoratorKey(service, baseIndex, 0);
			MoveBaseToSyntheticKey(service, baseImpl, baseKey);

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

		// Moves the base implementation off the public service onto the chain's lowest synthetic key.
		private void MoveBaseToSyntheticKey(string service, string baseImpl, string baseKey)
		{
			if (_byImpl.TryGetValue(baseImpl, out ImplInfo? baseInfo))
			{
				baseInfo.Services.Remove(new ServiceKey(service, null));
				ServiceKey synthetic = new(service, baseKey);
				if (!baseInfo.Services.Contains(synthetic))
				{
					baseInfo.Services.Add(synthetic);
				}
			}

			_serviceToImpl[new ServiceKey(service, baseKey)] = baseImpl;
		}

		// Registers one chain link (the decorator at <paramref name="linkIndex" />) and returns its synthetic
		// identity. The outermost link of the winner chain (<paramref name="isPublic" />) takes the public
		// service key; every other link is keyed so it is reached only as the inner of the link above it (or as
		// a rewritten collection member).
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
				? new ServiceKey(service, null)
				: new ServiceKey(service, DecoratorKey(service, baseIndex, link));
			info.Services.Add(ownKey);
			_serviceToImpl[ownKey] = identity;

			// Redirect this link's inner parameter to the link below it. The chain links are all registered under
			// the decorated service type, so the redirect keys to (service, innerKey) - not to the parameter's own
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

		// The per-service state threaded through WrapBaseImpl / AddChainLink: the decorated service, its
		// single-dispatch winner (if any), its collection membership (if any), the decorators innermost-first,
		// and each decorator's inner-parameter type (positionally matching Ordered).
		private readonly record struct ServiceChain(
			string Service,
			string? Winner,
			List<string>? Members,
			List<DecorateRegistration> Ordered,
			List<string> InnerParameterTypes);

		/// <summary>
		///     The fully-qualified type of a decorator's single constructor parameter that receives the inner
		///     instance, or <see langword="null" /> when there is none or it is ambiguous (AWT124) - or when the
		///     would-be inner is marked <c>[FromServices]</c>, yielded through
		///     <paramref name="externalInner" /> so the caller reports the specific conflict (AWT135) instead. The
		///     constructor is chosen by the same <see cref="SelectConstructor" /> the container uses to build the
		///     decorator, so this validation can never inspect a different constructor than the one constructed - a
		///     divergence would leave the inner parameter un-redirected and the link resolving itself. The returned
		///     type string is what <see cref="ClassifyParameter" /> produces for that parameter, so the
		///     inner-parameter redirect in <see cref="ClassifyParameters" /> can match it.
		/// </summary>
		private string? SingleInnerParameterType(INamedTypeSymbol decorator, INamedTypeSymbol service, out IParameterSymbol? externalInner)
		{
			externalInner = null;
			IMethodSymbol? constructor = SelectConstructor(decorator, _containerSymbol, _serviceToImpl.Keys.Select(k => k.Service), importServices: _importServices);
			if (constructor is null)
			{
				return null;
			}

			// The inner is an instance of the decorated service, so its parameter must accept it: the service is
			// implicitly convertible to the parameter's type (the parameter is the service, or a base of it). A
			// [FromKey] parameter is excluded - it deliberately selects a specific keyed registration, so it is a
			// separate dependency, never the chain inner (which is redirected by key and would ignore the [FromKey]
			// anyway). A [FromServices] parameter is excluded for the same reason: it deliberately resolves from
			// the external provider, so it is a separate dependency, never the chain inner. More than one parameter
			// can be assignable at once - e.g. a plain `object` state parameter alongside the inner - so the inner
			// is the most-derived of them: the one every other assignable parameter is a base of. Exactly one such
			// maximum makes the inner unambiguous; a tie (two equally-derived assignable parameters, e.g. two
			// `IService`) is genuinely ambiguous and reported as AWT124, as is a decorator whose only
			// service-assignable parameter is [FromKey]-ed.
			List<IParameterSymbol> assignable = constructor.Parameters
				.Where(p => FromKey(p.GetAttributes()) is null && !HasFromServices(p) && _compilation.HasImplicitConversion(service, p.Type))
				.ToList();

			// AWT135: nothing is left to receive the inner instance, but a [FromServices] parameter of the
			// service is present - the would-be inner was marked external, which would silently bypass the
			// decorator chain. Yield it so the caller reports that specific conflict instead of a generic AWT124.
			if (assignable.Count == 0)
			{
				externalInner = constructor.Parameters
					.FirstOrDefault(p => HasFromServices(p) && _compilation.HasImplicitConversion(service, p.Type));
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
	///     Builds the composites after coalescing (and after decorator chains, so a composite fronts the
	///     decorated members). For each <c>[Composite&lt;TComposite, TService&gt;]</c> it registers the composite
	///     as an ordinary instance (constructed, cached and disposed by index) with the chosen lifetime and
	///     rewrites the public unkeyed winner of <c>TService</c> to the composite, so a plain <c>TService</c>
	///     parameter and <c>Resolve&lt;TService&gt;()</c> both get the composite. The composite is deliberately
	///     NOT added to <c>serviceMembers</c>: it is excluded from its own - and everyone else's - collection
	///     membership, so its own collection parameter (and any separate <c>IEnumerable&lt;TService&gt;</c>
	///     consumer) resolves to the OTHER registrations, never the composite. With no self-edge the cycle
	///     (AWT102) and captive (AWT105) analysis works unchanged over the composite's eager collection edges. An
	///     empty member set is legal (the composite fans out to an empty collection). Reports AWT130 when the
	///     composite has no collection parameter of the composed service, AWT133 when that collection is of a base
	///     type rather than the service itself, AWT132 for a second composite over an already-composed service, and
	///     AWT131 (a warning) when the composite type is also registered as a bare member of its own service - in
	///     which case that membership is dropped, keeping the no-self-edge invariant that would otherwise be
	///     violated (and surface as a confusing AWT102 cycle).
	/// </summary>
	private static void BuildComposites(
		List<CompositeRegistration> composites,
		Compilation compilation,
		INamedTypeSymbol containerSymbol,
		CoalescedGraph graph,
		bool importServices,
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
			if (!ValidateCompositeCollection(composite, compositeType, containerSymbol, compilation, serviceToImpl, importServices, diagnostics))
			{
				continue;
			}

			ImplInfo compositeInfo = EnsureCompositeInstance(composite, compositeType, byImpl, implOrder);
			DropRedundantSelfMembership(composite, compositeType, serviceMembers, diagnostics);
			MakeCompositeThePublicWinner(composite, compositeType, compositeInfo, serviceToImpl, byImpl);
		}
	}

	// AWT132: whether this composite duplicates one already chosen for its service (so the caller skips it),
	// recording the first composite per service. A second composite of a DIFFERENT type is reported; the same
	// type declared twice is idempotent and left silent.
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

	// Whether the composite's collection parameter is valid (the caller proceeds), reporting AWT130 for a missing
	// collection parameter and AWT133 for a collection of a base type of the service (which, since collections are
	// keyed by exact element type, would resolve a different collection than the composed service's registrations).
	private static bool ValidateCompositeCollection(
		CompositeRegistration composite,
		string compositeType,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		Dictionary<ServiceKey, string> serviceToImpl,
		bool importServices,
		List<DiagnosticInfo> diagnostics)
	{
		switch (ClassifyCompositeCollection(composite.Composite, composite.ServiceSymbol, containerSymbol, compilation, serviceToImpl, importServices, out string? relatedElement))
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

	// Registers the composite as an ordinary instance (constructed, cached and disposed by index) and returns its
	// ImplInfo, reusing the existing one if the type was already registered (idempotent when named twice or when
	// the composite type is also a normal service).
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

	// AWT131: the composite type is also registered as a bare member of the service it composes (e.g. a
	// [Transient<C, S>] alongside [Composite<C, S>]). A composite is excluded from its own fan-out, so drop it from
	// every collection of the composed service and warn - without the removal its own collection edge would include
	// itself and surface as a confusing AWT102 dependency cycle.
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

	// Makes the composite the public single-dispatch winner: takes the unkeyed service off whatever impl currently
	// holds it (a former winner stays a collection member, just no longer the façade) and hands it to the composite.
	// The composite is never a serviceMembers entry, so it stays excluded from its own - and every other consumer's -
	// IEnumerable<TService>.
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
		/// <summary>A collection parameter whose element type is exactly the composed service - valid.</summary>
		Exact,

		/// <summary>
		///     A collection parameter of a base (or otherwise related) type of the composed service. Collections
		///     resolve by exact element type, so it would fan out over a different collection - reported as AWT133.
		/// </summary>
		RelatedElement,

		/// <summary>No collection parameter of the composed service at all - reported as AWT130.</summary>
		Missing,
	}

	/// <summary>
	///     Classifies the collection constructor parameter <paramref name="composite" /> offers for its composed
	///     <paramref name="service" /> - what it fans out over. <see cref="CompositeCollectionKind.Exact" /> when a
	///     collection parameter (<c>IEnumerable&lt;TService&gt;</c>, <c>IReadOnlyList&lt;TService&gt;</c>,
	///     <c>TService[]</c>, …) has element type exactly the service; <see cref="CompositeCollectionKind.RelatedElement" />
	///     (yielding the offending element type in <paramref name="relatedElement" />) when a collection parameter's
	///     element is a base type the service is assignable to but is not the service itself - collections resolve
	///     by exact element type, so it would fan out over a different collection; otherwise
	///     <see cref="CompositeCollectionKind.Missing" />. An exact match wins over a related one. The constructor
	///     is chosen by the same <see cref="SelectConstructor" /> the container builds the composite through, so
	///     this validation can never inspect a different constructor than the one constructed.
	/// </summary>
	private static CompositeCollectionKind ClassifyCompositeCollection(
		INamedTypeSymbol composite,
		INamedTypeSymbol service,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		Dictionary<ServiceKey, string> serviceToImpl,
		bool importServices,
		out string? relatedElement)
	{
		relatedElement = null;
		IMethodSymbol? constructor = SelectConstructor(composite, containerSymbol, serviceToImpl.Keys.Select(k => k.Service), importServices: importServices);
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
	///     Redirects a decorator chain link's single inner parameter (a direct, unkeyed dependency assignable to
	///     the decorated service type) to the synthetic key of the next-lower link - so the keyed resolver
	///     produces <c>D2(D1(Real))</c> rather than the link resolving its own public service (itself) and
	///     recursing. Both the service type and key are rewritten: the links are registered under the decorated
	///     service type, so a parameter declared as a base of the service still resolves the link (its own
	///     declared type is not a registration key). A non-link instance, or any other parameter, is returned
	///     unchanged and resolves from the graph.
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
