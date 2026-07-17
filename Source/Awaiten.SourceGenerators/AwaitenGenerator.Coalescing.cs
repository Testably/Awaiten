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

	private static (List<ImplInfo> Order, Dictionary<ServiceKey, string> ServiceToImpl, Dictionary<ServiceKey, List<string>> Members, List<ServiceKey> MemberOrder, List<(string ServiceType, INamedTypeSymbol Symbol)> VarianceCandidates, Dictionary<string, List<KeyedMember>> KeyedMembers, List<string> KeyedMemberOrder) CoalesceByImplementation(
		List<RawRegistration> raw,
		List<DiagnosticInfo> diagnostics)
	{
		List<ImplInfo> implOrder = new();
		Dictionary<string, ImplInfo> implInfos = new(StringComparer.Ordinal);
		Dictionary<ServiceKey, string> serviceToImpl = new();
		HashSet<string> reportedConflicts = new(StringComparer.Ordinal);
		HashSet<string> reportedProductionConflicts = new(StringComparer.Ordinal);
		HashSet<string> reportedDirectiveConflicts = new(StringComparer.Ordinal);

		// Collection membership: every registration of a service, keyed by (service type, key) and deduped by
		// implementation, in registration order. serviceMemberOrder preserves first-seen order for emission.
		Dictionary<ServiceKey, List<string>> serviceMembers = new();
		List<ServiceKey> serviceMemberOrder = new();

		// Keyed-collection membership: every real keyed registration of a service ([Key] and implementation),
		// grouped by service (value) type in registration order. Only real [Key] registrations join here, so a
		// keyed dictionary never surfaces a decorator's synthetic chain link (minted by a later phase).
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
				ReportCoalescingConflicts(info, registration, reportedConflicts, reportedProductionConflicts, reportedDirectiveConflicts, diagnostics);
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
			EnsureImpl(implInfos, implOrder, registration);

			// A keyed registration is also a member of its service's keyed collection, indexed by its [Key].
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
					SuppressDisposal = reg.SuppressDisposal,
				};
				implInfos.Add(reg.ImplementationType, info);
				implOrder.Add(info);
			}

			// Union every registration's closed marker forms (deduped), so a generic hook bound by two open-generic
			// scans that close the marker differently is seen as ambiguous in ResolveHook, not silently fixed to the
			// first-seen closing. All registrations of one scan carry the same forms, so this is a no-op for them.
			if (reg.HookClosedMarkers is { } markers)
			{
				foreach (INamedTypeSymbol marker in markers)
				{
					if (!info.HookClosedMarkers.Any(seen => SymbolEqualityComparer.Default.Equals(seen, marker)))
					{
						info.HookClosedMarkers.Add(marker);
					}
				}
			}

			return info;
		}
	}

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
				new EquatableArray<string>([Display(conditional.Service), Display(conditional.Consumer),])));
		}
	}

	/// <summary>
	///     AWT175: a type declared <c>[ImportService&lt;T&gt;]</c> (drawn from the external provider) must not also be
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
	///     AWT176: a type declared <c>[ImportService&lt;T&gt;]</c> that no dependency in the graph consumes with a
	///     shape that either routes it or gives its own diagnostic is a dead declaration. Reported once every instance
	///     is built (so every dependency has had the chance to route), at the container's location. A type is treated
	///     as consumed when it is either routed externally (an <c>External</c> direct dependency) or reached through a
	///     relationship over it (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>, <c>Owned&lt;T&gt;</c>, their Task forms) -
	///     the latter is not routed but surfaces its own AWT101, so a second diagnostic for the one root cause is
	///     avoided. A collection element reference (<c>IEnumerable&lt;T&gt;</c> and friends) does <em>not</em> count:
	///     the resolver never yields collection elements, so such a declaration is genuinely inert and stays reported.
	///     A type flagged AWT175 (registered, so resolved from the graph) is excluded, its contradiction the root cause.
	/// </summary>
	private static void ReportUnconsumedExternalServices(
		HashSet<string> externalServiceTypes,
		Dictionary<ServiceKey, string> serviceToImpl,
		List<InstanceModel> instances,
		INamedTypeSymbol containerSymbol,
		List<DiagnosticInfo> diagnostics)
	{
		if (externalServiceTypes.Count == 0)
		{
			return;
		}

		HashSet<string> consumed = new(StringComparer.Ordinal);
		foreach (InstanceModel instance in instances)
		{
			foreach (ParameterModel parameter in instance.ConstructorParameters.AsArray())
			{
				RecordExternalConsumption(parameter, consumed);
			}

			foreach (MemberModel member in instance.InjectedMembers.AsArray())
			{
				RecordExternalConsumption(member.Dependency, consumed);
			}

			// A lifecycle hook parameter consumes an external dependency too, so an import used only by a hook is
			// not unconsumed (AWT176 would otherwise wrongly advise removing it).
			foreach (ParameterModel parameter in instance.HookParameters())
			{
				RecordExternalConsumption(parameter, consumed);
			}
		}

		LocationInfo? location = LocationInfo.From(containerSymbol.Locations.FirstOrDefault());
		foreach (string externalType in externalServiceTypes
			         .Where(type => !consumed.Contains(type) && !IsRegisteredService(type, serviceToImpl))
			         .OrderBy(type => type, StringComparer.Ordinal))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.UnconsumedExternalService,
				location,
				new EquatableArray<string>([Display(externalType),])));
		}
	}

	/// <summary>
	///     Records a dependency's service type as consuming its <c>[ImportService&lt;T&gt;]</c> declaration (for AWT176)
	///     when the shape either routes the type externally (<c>External</c>) or resolves it from the graph through a
	///     relationship that surfaces its own AWT101 (<c>Func</c>/<c>Lazy</c>/<c>Owned</c>/<c>Task</c> and their forms).
	///     A collection element reference is deliberately not recorded: it is never routed, so the declaration stays inert.
	/// </summary>
	private static void RecordExternalConsumption(ParameterModel dependency, HashSet<string> consumed)
	{
		if (dependency.Kind is DependencyKind.External
		    or DependencyKind.Func or DependencyKind.Lazy or DependencyKind.Owned
		    or DependencyKind.Task or DependencyKind.FuncTask or DependencyKind.LazyTask)
		{
			consumed.Add(dependency.ServiceType);
		}
	}

	/// <summary>Whether any registration (of any key) provides the service type, so a declared external type contradicts it.</summary>
	private static bool IsRegisteredService(string serviceType, Dictionary<ServiceKey, string> serviceToImpl)
		=> serviceToImpl.Keys.Any(key => string.Equals(key.Service, serviceType, StringComparison.Ordinal));

	/// <summary>
	///     The single encoding of coalescing precedence: explicit strong registrations first, then overridable
	///     defaults (<c>Fallback.Warn</c>/<c>Fallback.Silent</c>), then open-generic-synthesized registrations, then scan matches.
	///     Consumed by the coalescing loop and the open generic expansion seed, which must agree on who wins.
	/// </summary>
	private static int PrecedenceRank(RawRegistration registration)
		=> registration switch
		{
			{ IsScan: true, } => 3,
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
		for (int rank = 0; rank <= 3; rank++)
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
					Display(registration.ImplementationType),
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
					Display(registration.ImplementationType),
					DescribeProduction(info.Production, info.ProductionMember),
					DescribeProduction(registration.Production, registration.ProductionMember),
				])));
		}

		// Deduped per (implementation, directive) rather than per implementation: a registration that
		// contradicts the winner on more than one directive (say a differing OnActivated and an opted-into
		// Eager) drops each independently, so each is reported once - mirroring how AWT107 and AWT111 keep
		// separate reported sets rather than collapsing a lifetime and a production conflict into one. The
		// Add doubles as the dedup filter, the same short-circuiting guard the AWT107/AWT111 reports use above.
		foreach ((string Directive, string Winner, string Loser) directive in ConflictingDirectives(info, registration)
			         .Where(directive => reportedDirectiveConflicts.Add(registration.ImplementationType + "\0" + directive.Directive)))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.ConflictingLifecycleDirectives,
				LocationInfo.From(registration.Location),
				new EquatableArray<string>([
					Display(registration.ImplementationType),
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
	/// </summary>
	private static IEnumerable<(string Directive, string Winner, string Loser)> ConflictingDirectives(ImplInfo info, RawRegistration registration)
	{
		if (registration.OnActivated is not null && !string.Equals(registration.OnActivated, info.OnActivated, StringComparison.Ordinal))
		{
			yield return ("OnActivated", DescribeHook(info.OnActivated), $"'{registration.OnActivated}'");
		}

		if (registration.OnRelease is not null && !string.Equals(registration.OnRelease, info.OnRelease, StringComparison.Ordinal))
		{
			yield return ("OnRelease", DescribeHook(info.OnRelease), $"'{registration.OnRelease}'");
		}

		if (registration.Eager && !info.Eager)
		{
			yield return ("Eager", "false", "true");
		}

		if (registration.SuppressDisposal && !info.SuppressDisposal)
		{
			yield return ("SuppressDisposal", "false", "true");
		}
	}

	private static string DescribeHook(string? hook) => hook is null ? "unset" : $"'{hook}'";

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
	///     Records a keyed registration as a member of its service's keyed collection ([Key] and implementation),
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
			new EquatableArray<string>([Display(registration.ServiceType), Display(consumer),])));
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

	private static string DescribeProduction(ProductionKind production, string? member)
		=> production switch
		{
			ProductionKind.Factory => $"factory '{member}'",
			ProductionKind.Instance => $"instance '{member}'",
			_ => "a constructor",
		};
}
