using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     Removes parameterized ([Arg]) and requesting-type-factory implementations from every collection's
	///     membership: a parameterized service is reachable only through its <c>Func&lt;TArg…, T&gt;</c> factory, and
	///     a requesting-type factory needs the consumer's <c>typeof(…)</c>, so neither is ever a collection member.
	/// </summary>
	private static void PruneParameterizedMembers(List<InstanceModel> instances, Dictionary<ServiceKey, List<string>> serviceMembers, Dictionary<string, List<KeyedMember>> keyedMembers)
	{
		HashSet<string> parameterized = new(StringComparer.Ordinal);
		foreach (InstanceModel instance in instances)
		{
			if (instance.IsParameterized || instance.IsRequestingTypeFactory)
			{
				parameterized.Add(instance.ImplementationType);
			}
		}

		if (parameterized.Count == 0)
		{
			return;
		}

		foreach (List<string> members in serviceMembers.Values)
		{
			members.RemoveAll(parameterized.Contains);
		}

		foreach (List<KeyedMember> members in keyedMembers.Values)
		{
			members.RemoveAll(member => parameterized.Contains(member.Implementation));
		}
	}

	/// <summary>
	///     Gives the scan match of a module in another assembly the identity of the implementation its generated factory
	///     builds (see <see cref="RawRegistration.ScanImplementation" />) when another registration or scan match names
	///     that implementation too, so coalescing shares one instance between them, as a container in the module's own
	///     assembly does. The instance is built by whichever of them coalescing picks first, so a match whose service the
	///     instance's type does not convert to cannot share it. The container then constructs the implementation for all
	///     of these matches when it can, and otherwise <see cref="DropUnsharedScanMatches" /> drops each such match.
	///     Returns the identities of the shared implementations.
	/// </summary>
	private static List<string> ShareScannedInstances(List<RawRegistration> raw, Compilation compilation)
	{
		HashSet<string> named = new(raw.Where(registration => registration.ScanImplementation is null).Select(registration => registration.ImplementationType), StringComparer.Ordinal);
		Dictionary<string, INamedTypeSymbol> shared = raw
			.Where(registration => registration.ScanImplementation is not null)
			.GroupBy(registration => TypeIdentity(registration.ScanImplementation!, compilation), StringComparer.Ordinal)
			.Where(group => group.Count() > 1 || named.Contains(group.Key))
			.ToDictionary(group => group.Key, group => group.First().ScanImplementation!, StringComparer.Ordinal);
		if (shared.Count == 0)
		{
			return [];
		}

		for (int index = 0; index < raw.Count; index++)
		{
			if (raw[index].ScanImplementation is { } implementation && shared.ContainsKey(TypeIdentity(implementation, compilation)))
			{
				raw[index] = raw[index] with { ImplementationType = TypeIdentity(implementation, compilation), };
			}
		}

		foreach (KeyValuePair<string, INamedTypeSymbol> entry in shared)
		{
			if (UnsharedScanMatches(raw, entry.Key, compilation).Count == 0 || !CanConstruct(entry.Value, compilation))
			{
				continue;
			}

			for (int index = 0; index < raw.Count; index++)
			{
				if (raw[index].ScanImplementation is { } implementation && SymbolEqualityComparer.Default.Equals(implementation, entry.Value))
				{
					raw[index] = raw[index] with
					{
						Implementation = implementation,
						Production = ProductionKind.Constructor,
						ProductionMember = null,
						GreedyConstructor = true,
						Traits = null,
					};
				}
			}
		}

		return shared.Keys.ToList();
	}

	/// <summary>
	///     Drops each scan match of a <paramref name="shared" /> implementation (see <see cref="ShareScannedInstances" />)
	///     that cannot share its instance after reporting AWT240, since its factory would build a second instance. Runs
	///     after the <c>SkipUnconstructable</c> pruning, so a match the pruning drops reports only AWT141. Returns whether
	///     it dropped a match.
	/// </summary>
	private static bool DropUnsharedScanMatches(List<RawRegistration> raw, List<string> shared, Compilation compilation, List<DiagnosticInfo> diagnostics)
	{
		bool dropped = false;
		foreach (string identity in shared)
		{
			foreach ((RawRegistration match, INamedTypeSymbol builtAs) in UnsharedScanMatches(raw, identity, compilation))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ScanMatchNotShared,
					LocationInfo.From(match.Location),
					new EquatableArray<string>([
						DisplayInstance(identity),
						Display(match.Origin!.ToDisplayString(FullyQualified)),
						Display(match.ServiceType),
						Display(builtAs.ToDisplayString(FullyQualified)),
					])));
				raw.RemoveAll(registration => ReferenceEquals(registration, match));
				dropped = true;
			}
		}

		return dropped;
	}

	/// <summary>
	///     The generated scan matches of the implementation <paramref name="identity" /> whose service the type of its
	///     shared instance does not convert to: coalescing builds the instance by the first registration in precedence
	///     order that it does not drop. An instance built as the implementation itself converts to every service a scan
	///     matched it under.
	/// </summary>
	private static List<(RawRegistration Match, INamedTypeSymbol BuiltAs)> UnsharedScanMatches(List<RawRegistration> raw, string identity, Compilation compilation)
	{
		HashSet<int> dropped = DroppedOverridableDefaults(raw);
		if (InPrecedenceOrder(raw).FirstOrDefault(candidate => !dropped.Contains(candidate.Index) && candidate.Registration.ImplementationType == identity).Registration?.Implementation is not { } builtAs
		    || TypeIdentity(builtAs, compilation) == identity)
		{
			return [];
		}

		return raw
			.Where(registration => registration is { ScanImplementation: not null, ServiceSymbol: { } service, }
			                       && registration.ImplementationType == identity
			                       && !compilation.HasImplicitConversion(builtAs, service))
			.Select(registration => (registration, builtAs))
			.ToList();
	}

	/// <summary>
	///     The marker inside the implementation identity of a <c>Factory</c> or <c>Instance</c> registration that names
	///     only its service (<c>&lt;service&gt;@member:&lt;owner&gt;.&lt;member&gt;</c>, see
	///     <see cref="AssignMemberIdentities" />). <c>DisplayInstance</c> and <see cref="RegisteredType" /> trim it.
	/// </summary>
	private const string MemberKeyMarker = "@member:";

	/// <summary>
	///     Gives every <c>Factory</c> or <c>Instance</c> registration that names only its service an implementation
	///     identity of its own: the service plus the owner-qualified member. Two producers of one service are then two
	///     implementations, as two constructed classes are, so precedence decides the single resolution and both stay
	///     collection members, while registrations naming the same member still share one instance. A type that another
	///     registration names as its implementation keeps its type identity, so a factory and a constructor of that one
	///     implementation still conflict (AWT111). An open generic factory's closing is identified the same way, per
	///     closing. Scan matches keep their own identities.
	/// </summary>
	private static void AssignMemberIdentities(List<RawRegistration> raw, INamedTypeSymbol containerSymbol, Compilation compilation)
	{
		HashSet<string> implementations = new(
			raw.Where(registration => !NamesOnlyItsService(registration)).Select(registration => registration.ImplementationType),
			StringComparer.Ordinal);
		for (int index = 0; index < raw.Count; index++)
		{
			RawRegistration registration = raw[index];
			if (NamesOnlyItsService(registration) && !implementations.Contains(registration.ImplementationType))
			{
				raw[index] = registration with
				{
					ImplementationType = $"{registration.ImplementationType}{MemberKeyMarker}{TypeIdentity(registration.Origin ?? containerSymbol, compilation)}.{registration.ProductionMember}",
				};
			}
		}

		bool NamesOnlyItsService(RawRegistration registration)
			=> registration is { Production: not ProductionKind.Constructor, IsScan: false, }
			   && registration.ImplementationType == (registration.ServiceSymbol is { } service ? TypeIdentity(service, compilation) : registration.ServiceType);
	}

	/// <summary>
	///     The marker inside the identity of a type that the compilation does not find by its name (see
	///     <see cref="TypeIdentity" />). <c>DisplayInstance</c> trims it.
	/// </summary>
	private const string AssemblyMarker = "@assembly:";

	/// <summary>
	///     The identity that coalescing and every map keyed by an implementation know a type by: its fully qualified
	///     name, followed by the assemblies of the type and of its type arguments when the compilation finds another
	///     type or none by one of their names, as for two same-named types of different assemblies. Two different
	///     types never share an identity, and a type the compilation finds by its name keeps that name.
	/// </summary>
	private static string TypeIdentity(INamedTypeSymbol type, Compilation compilation)
	{
		string name = type.ToDisplayString(FullyQualified);
		List<INamedTypeSymbol> components = NamedComponents(type).ToList();
		return components.All(component => component.TypeKind == TypeKind.Error
		                                   || SymbolEqualityComparer.Default.Equals(compilation.GetTypeByMetadataName(MetadataNameOf(component.OriginalDefinition)), component.OriginalDefinition))
			? name
			: $"{name}{AssemblyMarker}{string.Join(",", components.Select(component => component.ContainingAssembly?.Name))}";
	}

	/// <summary>The named types that make up <paramref name="type" />: the type itself, then its and its containing types' type arguments.</summary>
	private static IEnumerable<INamedTypeSymbol> NamedComponents(ITypeSymbol type)
	{
		if (type is IArrayTypeSymbol array)
		{
			foreach (INamedTypeSymbol component in NamedComponents(array.ElementType))
			{
				yield return component;
			}
		}
		else if (type is INamedTypeSymbol named)
		{
			yield return named;
			for (INamedTypeSymbol? owner = named; owner is not null; owner = owner.ContainingType)
			{
				foreach (INamedTypeSymbol component in owner.TypeArguments.SelectMany(NamedComponents))
				{
					yield return component;
				}
			}
		}
	}

	/// <summary>
	///     The type a registration names, without the member suffix <see cref="AssignMemberIdentities" /> adds: what an
	///     <c>[InjectProperty]</c> target or a <c>WhenInjectedInto</c> consumer is matched against.
	/// </summary>
	private static string RegisteredType(string implementationType)
	{
		int marker = implementationType.IndexOf(MemberKeyMarker, StringComparison.Ordinal);
		return marker < 0 ? implementationType : implementationType.Substring(0, marker);
	}

	private static string QualifiedMember(INamedTypeSymbol owner, string? member) => $"{owner.ToDisplayString(FullyQualified)}.{member}";

	private static (List<ImplInfo> Order, Dictionary<ServiceKey, string> ServiceToImpl, Dictionary<ServiceKey, List<string>> Members, List<ServiceKey> MemberOrder, List<(string ServiceType, INamedTypeSymbol Symbol)> VarianceCandidates, Dictionary<string, List<KeyedMember>> KeyedMembers, List<string> KeyedMemberOrder) CoalesceByImplementation(
		List<RawRegistration> raw,
		INamedTypeSymbol containerSymbol,
		List<DiagnosticInfo> diagnostics)
	{
		List<ImplInfo> implOrder = new();
		Dictionary<string, ImplInfo> implInfos = new(StringComparer.Ordinal);
		Dictionary<ServiceKey, string> serviceToImpl = new();
		HashSet<string> reportedConflicts = new(StringComparer.Ordinal);
		HashSet<string> reportedProductionConflicts = new(StringComparer.Ordinal);
		HashSet<string> reportedDirectiveConflicts = new(StringComparer.Ordinal);
		HashSet<string> reportedHookConflicts = new(StringComparer.Ordinal);

		// Collection membership: every registration of a service, keyed by (service type, key) and deduped by
		// implementation, in registration order. serviceMemberOrder preserves first-seen order for emission.
		Dictionary<ServiceKey, List<string>> serviceMembers = new();
		List<ServiceKey> serviceMemberOrder = new();

		// Keyed-collection membership: every real keyed registration of a service (Key and implementation),
		// grouped by service (value) type in registration order. Only real keyed registrations join here; a decorated
		// key later points its entry at the outermost decorator, never at an inner chain link.
		Dictionary<string, List<KeyedMember>> keyedMembers = new(StringComparer.Ordinal);
		List<string> keyedMemberOrder = new();

		// Variance: every unkeyed registration of a closed generic interface whose definition declares variance
		// (in/out), in registration order. A consumer request with no exact registration is redirected to a
		// variance-compatible candidate here. Keyed and invariant registrations are never candidates.
		List<(string ServiceType, INamedTypeSymbol Symbol)> varianceCandidates = new();
		HashSet<string> varianceSeen = new(StringComparer.Ordinal);

		// The registration that currently owns each service key, so a later loser can be judged against the full
		// winner: two Defaults colliding is AWT148, two same-tier module registrations is AWT155, a contradicting
		// lifetime/production is AWT107/AWT111. The intended overrides stay silent.
		Dictionary<ServiceKey, RawRegistration> winners = new();

		// Registrations are processed in precedence order (see PrecedenceRank): a losing default is dropped
		// entirely, so it is replaced transparently; a losing strong/synthesized/scan registration stays a member.
		foreach ((RawRegistration registration, _) in InPrecedenceOrder(raw))
		{
			// Record an unkeyed closed-generic-interface registration as a variance candidate, so a
			// differently-closed consumer request can be redirected to it (even when it loses the resolution slot).
			// A contextual (WhenInjectedInto) registration is excluded: it must reach only its named consumer, not
			// stand in for every differently-closed request of the service.
			if (registration.Key is null
			    && registration.WhenInjectedInto is null
			    && registration.ServiceSymbol is { IsGenericType: true, TypeKind: TypeKind.Interface, } variantService
			    && HasDeclaredVariance(variantService)
			    && varianceSeen.Add(registration.ServiceType))
			{
				varianceCandidates.Add((registration.ServiceType, variantService));
			}

			// Setting both Factory and Instance on one attribute is contradictory (AWT110). Like AWT108/109/112 it
			// is a single-registration fault, so it names the service type (AWT107/AWT111 name the implementation).
			if (registration.ConflictingDirectives)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ConflictingProductionDirectives,
					LocationInfo.From(registration.Location),
					new EquatableArray<string>([Display(registration.ServiceType),])));
			}

			// The implementation's already-recorded ImplInfo (null on first sight), which ReportCoalescingConflicts
			// compares the current registration against.
			implInfos.TryGetValue(registration.ImplementationType, out ImplInfo? info);

			// A WhenInjectedInto registration is stored under a synthetic context key, reached only from its named
			// consumer's dependencies. Its real Key stays null, so it never joins a keyed collection (AddKeyedMember).
			string? effectiveKey = registration.WhenInjectedInto is { } consumer ? ContextKey(consumer) : registration.Key;
			ServiceKey serviceKey = new(registration.ServiceType, effectiveKey);
			bool alreadyChosen = winners.TryGetValue(serviceKey, out RawRegistration? winner);

			// A lifetime (AWT107) or production (AWT111) conflict is a property of the implementation, so it is
			// checked before the per-service dedup below (else re-registering a service type differently would drop
			// the contradiction silently). A scan yields to an explicit registration and is exempt, but two scans
			// with different lifetimes are AWT142. An overridable default losing its key yields transparently; every
			// other default is checked like an explicit registration.
			if (!registration.IsScan && (!registration.Weak || !alreadyChosen || winner!.Weak))
			{
				ReportCoalescingConflicts(info, registration, containerSymbol, reportedConflicts, reportedProductionConflicts, reportedDirectiveConflicts, diagnostics);
			}
			else if (registration.IsScan && info is { IsScan: true, } && info.Lifetime != registration.Lifetime
			         && reportedConflicts.Add(registration.ImplementationType))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ScanLifetimeConflict,
					LocationInfo.From(registration.Location),
					new EquatableArray<string>([
						Display(registration.ImplementationType),
						info.Lifetime.ToString(),
						registration.Lifetime.ToString(),
					])));
			}

			// An overridable default whose service is already claimed is dropped in full, so the stronger (or
			// earlier) registration replaces it transparently. Two colliding Fallback.Warn defaults warn (AWT148); Fallback.Silent is silent.
			if (registration.Weak && alreadyChosen)
			{
				if (registration.IsDefault && winner!.IsDefault
				    && winner.ImplementationType != registration.ImplementationType)
				{
					diagnostics.Add(new DiagnosticInfo(
						Diagnostics.AmbiguousDefault,
						LocationInfo.From(registration.Location),
						new EquatableArray<string>([Display(registration.ServiceType),])));
				}

				continue;
			}

			// Every registration is a member of the collection for its (service type, key), built even when it
			// loses the single-resolution slot, since it is reachable through the collection.
			AddCollectionMember(serviceMembers, serviceMemberOrder, serviceKey, registration.ImplementationType);
			MergeScanHooks(EnsureImpl(implInfos, implOrder, registration), registration, reportedHookConflicts, diagnostics);

			// A keyed registration is also a member of its service's keyed collection, indexed by its Key.
			AddKeyedMember(keyedMembers, keyedMemberOrder, registration, alreadyChosen);

			if (alreadyChosen)
			{
				ReportDuplicateKey(registration, winner!.ImplementationType, diagnostics);
				ReportDuplicateContextualBinding(registration, winner, diagnostics);
				ReportCrossModuleDuplicate(registration, winner, diagnostics);
				continue;
			}

			ImplInfo chosen = EnsureImpl(implInfos, implOrder, registration);
			serviceToImpl[serviceKey] = registration.ImplementationType;
			winners[serviceKey] = registration;
			chosen.Services.Add(serviceKey);
		}

		return (implOrder, serviceToImpl, serviceMembers, serviceMemberOrder, varianceCandidates, keyedMembers, keyedMemberOrder);

		// Creates the single ImplInfo for a registration's implementation (idempotent): the first registration
		// fixes its lifetime/production, shared by every registration of that implementation. Takes implInfos and
		// implOrder as parameters so the caller's pre-loop lookup sees the Add across a call boundary.
		static ImplInfo EnsureImpl(Dictionary<string, ImplInfo> implInfos, List<ImplInfo> implOrder, RawRegistration reg)
		{
			if (!implInfos.TryGetValue(reg.ImplementationType, out ImplInfo? info))
			{
				info = new ImplInfo(
					reg.ImplementationType, reg.Implementation, reg.Lifetime,
					LocationInfo.From(reg.Location), reg.Production, reg.ProductionMember)
				{
					IsScan = reg.IsScan,
					GreedyConstructor = reg.GreedyConstructor,
					Origin = reg.Origin,
					Eager = reg.Eager,
					OnActivated = reg.OnActivated,
					OnRelease = reg.OnRelease,
					OnActivatedOrigin = reg.OnActivated is null ? null : reg.Origin,
					OnReleaseOrigin = reg.OnRelease is null ? null : reg.Origin,
					SuppressDisposal = reg.SuppressDisposal,
					GenericFactory = reg.GenericFactory,
					Traits = reg.Traits,
					TraitsExact = reg.TraitsExact,
					ConstructVia = reg.ConstructVia,
					ConstructViaType = reg.ConstructViaType,
					Constructor = reg.Constructor,
				};
				info.OnActivatedMarkers.AddRange(reg.OnActivatedMarkers ?? []);
				info.OnReleaseMarkers.AddRange(reg.OnReleaseMarkers ?? []);
				implInfos.Add(reg.ImplementationType, info);
				implOrder.Add(info);
			}

			return info;
		}
	}

	/// <summary>
	///     Merges a scan registration's lifecycle hooks into its implementation's coalesced info, per hook slot.
	///     Two scans matching one type combine: a hook fills a slot no earlier scan claimed (so one scan's
	///     <c>OnActivated</c> and another's <c>OnRelease</c> both apply), and a scan restating the slot's hook
	///     contributes its closed marker forms to that slot's set, so a generic hook bound by two open-generic scans
	///     that close their markers differently is seen as ambiguous in <c>ResolveHook</c> (AWT198) rather than
	///     silently fixed to the first-seen closing. Naming a <em>different</em> method for a claimed slot is
	///     <see cref="Diagnostics.ScanHookConflict">AWT199</see> (once per implementation and slot): which method ran
	///     would depend on attribute order, the same order-dependence AWT142 surfaces for lifetimes. A hook name is
	///     owner-relative (each slot resolves against the origin that named it), so the same name from a different
	///     origin - a container scan and a same-compilation module scan, or two modules - is a different method and
	///     conflicts too; only a restatement from the same origin merges. When the implementation's winning
	///     registration is not a scan the whole merge is skipped: a scan yields to an explicit registration, whose
	///     hooks (or deliberate lack of them) replace the scan's like its other options.
	/// </summary>
	private static void MergeScanHooks(
		ImplInfo info,
		RawRegistration registration,
		HashSet<string> reportedHookConflicts,
		List<DiagnosticInfo> diagnostics)
	{
		if (!registration.IsScan || !info.IsScan)
		{
			return;
		}

		(info.OnActivated, info.OnActivatedOrigin) = MergeScanHookSlot(
			registration, release: false, (info.OnActivated, info.OnActivatedOrigin), info.OnActivatedMarkers, reportedHookConflicts, diagnostics);
		(info.OnRelease, info.OnReleaseOrigin) = MergeScanHookSlot(
			registration, release: true, (info.OnRelease, info.OnReleaseOrigin), info.OnReleaseMarkers, reportedHookConflicts, diagnostics);
	}

	/// <summary>
	///     Merges one hook slot (see <see cref="MergeScanHooks" />): returns the slot's coalesced hook name and the
	///     origin it resolves against, unioning the registration's closed marker forms into the slot's set when it
	///     fills or restates the slot, and reporting AWT199 (keeping the first-seen hook) when it contradicts it -
	///     by naming a different method, or the same name on a different origin, which is a different method too.
	/// </summary>
	private static (string? Name, INamedTypeSymbol? Origin) MergeScanHookSlot(
		RawRegistration registration,
		bool release,
		(string? Name, INamedTypeSymbol? Origin) current,
		List<INamedTypeSymbol> slotMarkers,
		HashSet<string> reportedHookConflicts,
		List<DiagnosticInfo> diagnostics)
	{
		string? name = release ? registration.OnRelease : registration.OnActivated;
		if (name is null)
		{
			return current;
		}

		string slot = release ? "OnRelease" : "OnActivated";
		if (current.Name is not null
		    && (!string.Equals(current.Name, name, StringComparison.Ordinal)
		        || !SymbolEqualityComparer.Default.Equals(current.Origin, registration.Origin)))
		{
			if (reportedHookConflicts.Add(registration.ImplementationType + "\0" + slot))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ScanHookConflict,
					LocationInfo.From(registration.Location),
					new EquatableArray<string>([
						Display(registration.ImplementationType),
						slot,
						QualifiedHookDisplay(current.Name, current.Origin),
						QualifiedHookDisplay(name, registration.Origin),
					])));
			}

			return current;
		}

		IReadOnlyList<INamedTypeSymbol>? markers = release ? registration.OnReleaseMarkers : registration.OnActivatedMarkers;
		if (markers is { } contributed)
		{
			foreach (INamedTypeSymbol marker in contributed.Where(marker =>
				         !slotMarkers.Any(seen => SymbolEqualityComparer.Default.Equals(seen, marker))))
			{
				slotMarkers.Add(marker);
			}
		}

		return (name, registration.Origin);
	}

	/// <summary>
	///     Names a hook in the AWT199 message: qualified with its owning module so two same-named hooks on
	///     different origins read as the different methods they are; a container-owned hook stays a bare name.
	/// </summary>
	private static string QualifiedHookDisplay(string name, INamedTypeSymbol? origin)
		=> origin is { } owner ? $"{Display(owner.ToDisplayString(FullyQualified))}.{name}" : name;

	/// <summary>
	///     The synthetic resolution key a contextual (WhenInjectedInto) registration is stored under: unique per
	///     consumer type and prefixed so it is very unlikely to collide with a user <c>Key</c>. Reached only from
	///     that consumer's dependencies, so the contextual implementation never surfaces on the public dispatch.
	/// </summary>
	private const string ContextKeyPrefix = "__ctx:";

	private static string ContextKey(string consumerType) => ContextKeyPrefix + consumerType;

	/// <summary>
	///     A contextual (WhenInjectedInto) registration recorded for AWT167: its context key, the service and
	///     consumer types (for the message) and the registration location. One whose key no consumer dependency
	///     consumed never applied and is reported.
	/// </summary>
	private sealed record ConditionalRegistration(ServiceKey Key, string Service, string Consumer, LocationInfo? Location);

	/// <summary>
	///     Every contextual (WhenInjectedInto) registration paired with its context key, so <see cref="BuildGraph" />
	///     can report AWT167 for any whose named consumer never consumes it.
	/// </summary>
	private static List<ConditionalRegistration> CollectConditionalRegistrations(List<RawRegistration> raw)
	{
		List<ConditionalRegistration> conditionals = new();
		foreach (RawRegistration registration in raw)
		{
			if (registration.WhenInjectedInto is { } consumer)
			{
				conditionals.Add(new ConditionalRegistration(
					new ServiceKey(registration.ServiceType, ContextKey(consumer)),
					registration.ServiceType,
					consumer,
					LocationInfo.From(registration.Location)));
			}
		}

		return conditionals;
	}

	/// <summary>
	///     AWT167: reports every contextual (WhenInjectedInto) registration whose named consumer never redirected to
	///     its context key, so it was never reached. Called once every instance is built, so a consumer's every
	///     dependency has had the chance to consume the key.
	/// </summary>
	private static void ReportUnappliedContextualBindings(
		List<ConditionalRegistration> conditionals,
		HashSet<ServiceKey> consumedConditionals,
		List<DiagnosticInfo> diagnostics)
	{
		foreach (ConditionalRegistration conditional in conditionals.Where(c => !consumedConditionals.Contains(c.Key)))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ContextualBindingNeverApplies,
				conditional.Location,
				new EquatableArray<string>([Display(conditional.Service), DisplayInstance(conditional.Consumer),])));
		}
	}

	/// <summary>
	///     AWT175: a closed type declared <c>[ImportService]</c> (drawn from the external provider) must not also be
	///     registered on the container - it is either host-owned or Awaiten-owned. Reported once the coalesced
	///     service-&gt;impl map is known, at the container's location.
	/// </summary>
	private static void ReportContradictingExternalServices(
		HashSet<string> externalServiceTypes,
		Dictionary<ServiceKey, string> serviceToImpl,
		INamedTypeSymbol containerSymbol,
		List<DiagnosticInfo> diagnostics)
	{
		if (externalServiceTypes.Count == 0)
		{
			return;
		}

		LocationInfo? location = LocationInfo.From(containerSymbol.Locations.FirstOrDefault());
		foreach (string externalType in externalServiceTypes
			         .Where(type => IsRegisteredService(type, serviceToImpl))
			         .OrderBy(type => type, StringComparer.Ordinal))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ContradictingExternalService,
				location,
				new EquatableArray<string>([Display(externalType),])));
		}
	}

	/// <summary>
	///     AWT175 for the open generic definitions declared <c>[ImportService(typeof(…&lt;&gt;))]</c>, at the container's
	///     location: a routed closing the container also registers is contradictory, unless a closed declaration of that
	///     closing already reports it.
	/// </summary>
	private static void ReportOpenExternalServices(
		ExternalServiceTypes externalServiceTypes,
		Dictionary<ServiceKey, string> serviceToImpl,
		HashSet<string> routed,
		INamedTypeSymbol containerSymbol,
		List<DiagnosticInfo> diagnostics)
	{
		if (externalServiceTypes.Open.Count == 0)
		{
			return;
		}

		LocationInfo? location = LocationInfo.From(containerSymbol.Locations.FirstOrDefault());
		foreach (string closing in routed
			         .Where(type => externalServiceTypes.Closings.ContainsKey(type)
			                        && !externalServiceTypes.Closed.Contains(type)
			                        && IsRegisteredService(type, serviceToImpl))
			         .OrderBy(type => type, StringComparer.Ordinal))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ContradictingExternalService,
				location,
				new EquatableArray<string>([Display(closing),])));
		}
	}

	/// <summary>
	///     The service types the built instances route externally, across constructor and factory parameters,
	///     injected members and lifecycle hook parameters.
	/// </summary>
	private static HashSet<string> RoutedExternal(List<InstanceModel> instances)
		=> new(instances.SelectMany(instance => instance.WalkedDependencies())
			.Where(dependency => dependency.Kind == DependencyKind.External)
			.Select(dependency => dependency.ServiceType), StringComparer.Ordinal);

	/// <summary>Whether any registration (of any key) provides the service type, so a declared external type contradicts it.</summary>
	private static bool IsRegisteredService(string serviceType, Dictionary<ServiceKey, string> serviceToImpl)
		=> serviceToImpl.Keys.Any(key => string.Equals(key.Service, serviceType, StringComparison.Ordinal));

	/// <summary>
	///     The single encoding of coalescing precedence: explicit strong registrations first, then overridable
	///     defaults (<c>Fallback.Warn</c>/<c>Fallback.Silent</c>), then open-generic-synthesized registrations, then
	///     the closings of open generic overridable defaults, then scan matches. Consumed by the coalescing loop and
	///     the open generic expansion seed, which must agree on who wins.
	/// </summary>
	private static int PrecedenceRank(RawRegistration registration)
		=> registration switch
		{
			{ IsScan: true, } => 4,
			{ IsSynthesized: true, Weak: true, } => 3,
			{ IsSynthesized: true, } => 2,
			{ Weak: true, } => 1,
			_ => 0,
		};

	/// <summary>
	///     Enumerates registrations in coalescing precedence order (see <see cref="PrecedenceRank" />), preserving
	///     declaration order within each tier. Each registration is paired with its index into <paramref name="raw" />
	///     for consumers that track identity across passes (value equality cannot).
	/// </summary>
	private static IEnumerable<(RawRegistration Registration, int Index)> InPrecedenceOrder(List<RawRegistration> raw)
	{
		for (int rank = 0; rank <= 4; rank++)
		{
			for (int index = 0; index < raw.Count; index++)
			{
				if (PrecedenceRank(raw[index]) == rank)
				{
					yield return (raw[index], index);
				}
			}
		}
	}

	/// <summary>
	///     The registrations (by index into <paramref name="raw" />) coalescing will drop in full: an overridable
	///     default whose service key a higher-tier or earlier registration claims. Losing strong/synthesized/scan
	///     registrations stay collection members. Sound to compute before open generic expansion (synthesized ranks
	///     below defaults).
	/// </summary>
	private static HashSet<int> DroppedOverridableDefaults(List<RawRegistration> raw)
	{
		HashSet<ServiceKey> claimed = new();
		HashSet<int> dropped = new();
		foreach ((RawRegistration registration, int index) in InPrecedenceOrder(raw))
		{
			if (!claimed.Add(new ServiceKey(registration.ServiceType, registration.Key)) && registration.Weak)
			{
				dropped.Add(index);
			}
		}

		return dropped;
	}

	/// <summary>
	///     Reports the coalescing conflicts a re-registration of an already-seen implementation raises: a different
	///     lifetime (AWT107), a different production strategy (AWT111), or a contradicting
	///     OnActivated/OnRelease/Eager/SuppressDisposal directive (AWT166). Lifetime and production conflicts are
	///     reported once per implementation, each directive
	///     conflict once per (implementation, directive), since coalescing keeps the first registration.
	/// </summary>
	private static void ReportCoalescingConflicts(
		ImplInfo? info,
		RawRegistration registration,
		INamedTypeSymbol containerSymbol,
		HashSet<string> reportedConflicts,
		HashSet<string> reportedProductionConflicts,
		HashSet<string> reportedDirectiveConflicts,
		List<DiagnosticInfo> diagnostics)
	{
		if (info is null)
		{
			return;
		}

		if (info.Lifetime != registration.Lifetime && reportedConflicts.Add(registration.ImplementationType))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ConflictingLifetime,
				LocationInfo.From(registration.Location),
				new EquatableArray<string>([
					DisplayInstance(registration.ImplementationType),
					info.Lifetime.ToString(),
					registration.Lifetime.ToString(),
				])));
		}

		if (ConflictsWith(info, registration) && reportedProductionConflicts.Add(registration.ImplementationType))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ConflictingProduction,
				LocationInfo.From(registration.Location),
				new EquatableArray<string>([
					DisplayInstance(registration.ImplementationType),
					DescribeProduction(info.Production, info.ProductionMember, info.Origin ?? containerSymbol),
					DescribeProduction(registration.Production, registration.ProductionMember, registration.Origin ?? containerSymbol),
				])));
		}

		// Deduped per (implementation, directive) rather than per implementation: a registration that
		// contradicts the winner on more than one directive (say a differing OnActivated and an opted-into
		// Eager) drops each independently, so each is reported once - mirroring how AWT107 and AWT111 keep
		// separate reported sets rather than collapsing a lifetime and a production conflict into one. The
		// Add doubles as the dedup filter, the same short-circuiting guard the AWT107/AWT111 reports use above.
		foreach ((string Directive, string Winner, string Loser) directive in ConflictingDirectives(info, registration, containerSymbol)
			         .Where(directive => reportedDirectiveConflicts.Add(registration.ImplementationType + "\0" + directive.Directive)))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ConflictingLifecycleDirectives,
				LocationInfo.From(registration.Location),
				new EquatableArray<string>([
					DisplayInstance(registration.ImplementationType),
					directive.Directive,
					directive.Winner,
					directive.Loser,
				])));
		}
	}

	/// <summary>
	///     Every per-instance directive (OnActivated, OnRelease, Eager, or SuppressDisposal) this registration sets to
	///     a value the coalesced instance will not use, each yielded independently so it can be reported on its own.
	///     Coalescing keeps the first (winning) registration's directives, so a conflict is a later registration
	///     explicitly naming a directive value that differs from the winner's: a differing hook, or opting into Eager
	///     or SuppressDisposal the winner did not. A registration that leaves a directive unset (a null hook, or a
	///     bool flag left at its default false) states no opinion and merges with the winner rather than conflicting,
	///     so the winner's own directives, which this registration inherits, are never a conflict against themselves.
	///     A hook is identified by its name and the type that declares it, so the same name on a module and on the
	///     container (or on two modules) is a different hook.
	/// </summary>
	private static IEnumerable<(string Directive, string Winner, string Loser)> ConflictingDirectives(ImplInfo info, RawRegistration registration, INamedTypeSymbol containerSymbol)
	{
		if (IsDifferentHook(registration.OnActivated, info.OnActivated, info.OnActivatedOrigin))
		{
			yield return ("OnActivated", DescribeHook(info.OnActivated, info.OnActivatedOrigin), DescribeHook(registration.OnActivated, registration.Origin));
		}

		if (IsDifferentHook(registration.OnRelease, info.OnRelease, info.OnReleaseOrigin))
		{
			yield return ("OnRelease", DescribeHook(info.OnRelease, info.OnReleaseOrigin), DescribeHook(registration.OnRelease, registration.Origin));
		}

		if (registration.Eager && !info.Eager)
		{
			yield return ("Eager", "false", "true");
		}

		if (registration.SuppressDisposal && !info.SuppressDisposal)
		{
			yield return ("SuppressDisposal", "false", "true");
		}

		bool IsDifferentHook(string? hook, string? winnerHook, INamedTypeSymbol? winnerOrigin)
			=> hook is not null
			   && (!string.Equals(hook, winnerHook, StringComparison.Ordinal)
			       || !SymbolEqualityComparer.Default.Equals(registration.Origin, winnerOrigin));

		string DescribeHook(string? hook, INamedTypeSymbol? origin)
			=> hook is null ? "unset" : $"'{Display(QualifiedMember(origin ?? containerSymbol, hook))}'";
	}

	/// <summary>
	///     Records a registration's implementation as a member of the collection for its (service type, key), in
	///     registration order and deduped by implementation. <c>serviceMemberOrder</c> preserves first-seen (type, key) order.
	/// </summary>
	private static void AddCollectionMember(
		Dictionary<ServiceKey, List<string>> serviceMembers,
		List<ServiceKey> serviceMemberOrder,
		ServiceKey serviceKey,
		string implementationType)
	{
		if (!serviceMembers.TryGetValue(serviceKey, out List<string>? members))
		{
			members = new List<string>();
			serviceMembers.Add(serviceKey, members);
			serviceMemberOrder.Add(serviceKey);
		}

		if (!members.Contains(implementationType))
		{
			members.Add(implementationType);
		}
	}

	/// <summary>
	///     AWT155: two different imported modules register the same unkeyed service with different implementations at
	///     the same precedence tier, so which wins is decided only by [Import] order, invisible at either module. A
	///     cross-tier loss, scans, overridable defaults, container-over-module and keyed collisions (AWT117) stay silent.
	/// </summary>
	private static void ReportCrossModuleDuplicate(
		RawRegistration registration,
		RawRegistration winner,
		List<DiagnosticInfo> diagnostics)
	{
		if (registration.Key is not null
		    || registration.IsScan
		    || registration.Weak
		    || registration.Origin is null
		    || winner.Origin is null
		    || HasAwaitenAttribute(winner.Origin.GetAttributes(), "ContainerAttribute")
		    || PrecedenceRank(registration) != PrecedenceRank(winner)
		    || SymbolEqualityComparer.Default.Equals(registration.Origin, winner.Origin)
		    || winner.ImplementationType == registration.ImplementationType)
		{
			return;
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.CrossModuleDuplicate,
			LocationInfo.From(registration.Location),
			new EquatableArray<string>([
				Display(registration.ServiceType),
				Display(winner.Origin.ToDisplayString(FullyQualified)),
				Display(registration.Origin.ToDisplayString(FullyQualified)),
			])));
	}

	/// <summary>
	///     Records a keyed registration as a member of its service's keyed collection (Key and implementation),
	///     grouped by service (value) type in registration order. An unkeyed registration contributes nothing, and the
	///     first registration per (service, key) wins (a genuine duplicate is the caller's AWT117).
	/// </summary>
	private static void AddKeyedMember(
		Dictionary<string, List<KeyedMember>> keyedMembers,
		List<string> keyedMemberOrder,
		RawRegistration registration,
		bool alreadyChosen)
	{
		if (registration.Key is null || alreadyChosen)
		{
			return;
		}

		if (!keyedMembers.TryGetValue(registration.ServiceType, out List<KeyedMember>? members))
		{
			members = new List<KeyedMember>();
			keyedMembers.Add(registration.ServiceType, members);
			keyedMemberOrder.Add(registration.ServiceType);
		}

		members.Add(new KeyedMember(registration.Key, registration.ImplementationType));
	}

	/// <summary>
	///     AWT117: two different implementations claim the same service type and key, so a keyed resolution would be
	///     ambiguous. The same implementation re-registered, or an unkeyed duplicate, is just first-wins and not reported.
	/// </summary>
	private static void ReportDuplicateKey(RawRegistration registration, string existingImpl, List<DiagnosticInfo> diagnostics)
	{
		if (registration.Key is null || existingImpl == registration.ImplementationType)
		{
			return;
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.DuplicateKey,
			LocationInfo.From(registration.Location),
			new EquatableArray<string>([Display(registration.ServiceType), KeyDisplay(registration.Key),])));
	}

	/// <summary>
	///     AWT169: a second implementation targets the same service and consumer via <c>WhenInjectedInto</c>, so
	///     which one the consumer resolves would be ambiguous. Both share the synthetic context key, so this is
	///     reached from the <c>alreadyChosen</c> branch; the same implementation re-registered is left silent.
	/// </summary>
	private static void ReportDuplicateContextualBinding(RawRegistration registration, RawRegistration winner, List<DiagnosticInfo> diagnostics)
	{
		if (registration.WhenInjectedInto is not { } consumer || winner.ImplementationType == registration.ImplementationType)
		{
			return;
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.DuplicateContextualBinding,
			LocationInfo.From(registration.Location),
			new EquatableArray<string>([Display(registration.ServiceType), DisplayInstance(consumer),])));
	}

	/// <summary>
	///     Two registrations of the same implementation conflict when they produce it differently: a different kind
	///     (constructor vs factory vs instance), a different member, or the same member name on different owners.
	/// </summary>
	private static bool ConflictsWith(ImplInfo info, RawRegistration registration)
		=> info.Production != registration.Production
		   || !string.Equals(info.ProductionMember, registration.ProductionMember, StringComparison.Ordinal)
		   || (registration.Production != ProductionKind.Constructor
		       && !SymbolEqualityComparer.Default.Equals(info.Origin, registration.Origin));

	/// <summary>
	///     Names a production strategy in the AWT111 message, qualifying a member with its owner so two members of the
	///     same name on different owners read as the different members they are.
	/// </summary>
	private static string DescribeProduction(ProductionKind production, string? member, INamedTypeSymbol owner)
		=> production switch
		{
			ProductionKind.Factory => $"factory '{Display(QualifiedMember(owner, member))}'",
			ProductionKind.Instance => $"instance '{Display(QualifiedMember(owner, member))}'",
			_ => "a constructor",
		};
}
