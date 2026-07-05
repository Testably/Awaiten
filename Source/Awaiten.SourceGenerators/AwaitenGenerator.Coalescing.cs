using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     Removes parameterized ([Arg]) and requesting-type-factory implementations from every collection's
	///     membership: a parameterized service is built fresh from its runtime arguments and reachable only
	///     through its <c>Func&lt;TArg…, T&gt;</c> factory, and a requesting-type factory needs the consumer's
	///     <c>typeof(…)</c> at each site (which a collection materialization does not supply) - so neither is
	///     ever a collection member.
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

		// Collection membership: every registration of a service, keyed by (service type, resolution key) and
		// deduped by implementation, kept in registration order - so an unkeyed IEnumerable<T> resolves the
		// unkeyed registrations and a [FromKey("k")] IEnumerable<T> the registrations under "k".
		// serviceMemberOrder preserves the first-seen (type, key) order for deterministic emission.
		Dictionary<ServiceKey, List<string>> serviceMembers = new();
		List<ServiceKey> serviceMemberOrder = new();

		// Keyed-collection membership: every real keyed registration of a service (its [Key] and implementation),
		// grouped by service (value) type in registration order, so an IReadOnlyDictionary<string, T> resolves to
		// all of them keyed by their [Key]. Only real [Key] registrations join here - the synthetic keys minted
		// for decorator chains are added to serviceMembers/serviceToImpl by a later phase, never to the raw
		// registrations this loop sees, so a keyed dictionary never surfaces a decorator's internal chain link.
		Dictionary<string, List<KeyedMember>> keyedMembers = new(StringComparer.Ordinal);
		List<string> keyedMemberOrder = new();

		// Variance: every unkeyed registration of a closed generic interface whose definition declares variance
		// (in/out), keyed by its fully-qualified string, in registration order. When a consumer requests a closed
		// generic interface with no exact registration, the request is redirected to a variance-compatible
		// candidate here (a registered IHandler<DomainEvent> satisfying a requested IHandler<OrderPlaced> via
		// `in T`). Keyed registrations are reached only through their key, so they are never variance-redirect
		// targets; an invariant interface can never satisfy a different closure, so it is not a candidate.
		List<(string ServiceType, INamedTypeSymbol Symbol)> varianceCandidates = new();
		HashSet<string> varianceSeen = new(StringComparer.Ordinal);

		// The registration that currently owns each service key, so a later loser can be judged against the
		// full winner: a Default losing to another Default is an ambiguous-default warning (AWT148), two
		// same-tier module registrations colliding is AWT155, and a default losing to another default with a
		// contradicting lifetime/production is an AWT107/AWT111 conflict - while a default correctly
		// overridden by a strong registration, and the container overriding a module (the intended
		// mechanisms), stay silent.
		Dictionary<ServiceKey, RawRegistration> winners = new();

		// Registrations are processed in precedence order (see PrecedenceRank): a losing default is dropped
		// entirely below (not built, not a collection member), so a container or module replaces it
		// transparently; a losing strong, synthesized or scan registration stays a collection member as before.
		foreach ((RawRegistration registration, _) in InPrecedenceOrder(raw))
		{
			// Record an unkeyed closed-generic-interface registration as a variance candidate, so a
			// differently-closed consumer request can be redirected to it. Recorded even when it loses the
			// single-resolution slot to an earlier registration, since it stays reachable through its resolver.
			if (registration.Key is null
			    && registration.ServiceSymbol is { IsGenericType: true, TypeKind: TypeKind.Interface, } variantService
			    && HasDeclaredVariance(variantService)
			    && varianceSeen.Add(registration.ServiceType))
			{
				varianceCandidates.Add((registration.ServiceType, variantService));
			}

			// Setting both Factory and Instance on one attribute is contradictory; the directives are
			// mutually exclusive, so report AWT110 against the offending registration. Like AWT108/109/112,
			// this is a fault in a single registration's directives, so it names the service type (the
			// AWT107/AWT111 coalescing conflicts name the implementation instead).
			if (registration.ConflictingDirectives)
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ConflictingProductionDirectives,
					LocationInfo.From(registration.Location),
					new EquatableArray<string>([Display(registration.ServiceType),])));
			}

			// The implementation's already-recorded ImplInfo (null on first sight), which ReportCoalescingConflicts
			// compares the current registration against. EnsureImpl below takes implInfos as a parameter rather
			// than capturing it, so its Add crosses a call boundary and this lookup is not misread as reading an
			// always-empty dictionary.
			implInfos.TryGetValue(registration.ImplementationType, out ImplInfo? info);

			ServiceKey serviceKey = new(registration.ServiceType, registration.Key);
			bool alreadyChosen = winners.TryGetValue(serviceKey, out RawRegistration? winner);

			// A lifetime (AWT107) or production (AWT111) conflict is a property of the implementation, not of any
			// single service type, so it is checked before the per-service dedup below; otherwise re-registering
			// the same service type differently would be skipped and the contradiction silently dropped.
			// Coalescing keeps the first, so the conflicting one is reported rather than ignored. A scan
			// registration is overridable and yields to whatever an explicit registration (always processed
			// first) fixed for the implementation, so it is exempt from that check - but two scans that match
			// the same implementation with different lifetimes contradict each other with nothing explicit to
			// yield to, so that is surfaced as AWT142 rather than silently resolved by attribute order.
			// An overridable default losing its service key to a strong registration is meant to be replaced
			// transparently - possibly by a strong registration of the same implementation with a different
			// lifetime - so that one loser yields rather than reporting a conflict. Every other default is
			// checked like an explicit registration: one that keeps its key still contributes its declared
			// lifetime/production and must not silently inherit what another registration fixed for the
			// implementation, and one losing to another default contradicts it with nothing stronger to resolve
			// them. AWT142 fires only between two scans: a scan whose implementation was first fixed by an
			// explicit registration (strong or default, both processed earlier) yields to it silently instead.
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

			// An overridable default whose service is already claimed is dropped in full - not built and not a
			// collection member - so the stronger (or earlier) registration replaces it transparently. When both
			// the loser and the current winner are Defaults, which one applies is left to declaration order, so
			// AWT148 warns; a TryAdd default (or a default correctly overridden by a strong registration) is silent.
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

			// Every registration is a member of the collection for its (service type, key): an unkeyed
			// IEnumerable<T> resolves the unkeyed registrations, a [FromKey("k")] IEnumerable<T> the ones under
			// "k". A member is built even when it loses the single-resolution slot to an earlier registration,
			// since it is reachable through the collection.
			AddCollectionMember(serviceMembers, serviceMemberOrder, serviceKey, registration.ImplementationType);
			EnsureImpl(implInfos, implOrder, registration);

			// A keyed registration is also a member of its service's keyed collection, indexed by its [Key].
			AddKeyedMember(keyedMembers, keyedMemberOrder, registration, alreadyChosen);

			if (alreadyChosen)
			{
				ReportDuplicateKey(registration, winner!.ImplementationType, diagnostics);
				ReportCrossModuleDuplicate(registration, winner, diagnostics);
				continue;
			}

			ImplInfo chosen = EnsureImpl(implInfos, implOrder, registration);
			serviceToImpl[serviceKey] = registration.ImplementationType;
			winners[serviceKey] = registration;
			chosen.Services.Add(serviceKey);
		}

		return (implOrder, serviceToImpl, serviceMembers, serviceMemberOrder, varianceCandidates, keyedMembers, keyedMemberOrder);

		// Creates the single ImplInfo for a registration's implementation (idempotent): the first registration seen
		// for an implementation fixes its lifetime/production, and the same instance is shared by every registration
		// of that implementation (multi-service or collection member). A collection member that is not the
		// single-resolution winner is still built here - it is reached only through the collection. Takes implInfos
		// and implOrder as parameters (rather than being a capturing local function) so the caller's pre-loop lookup
		// sees the Add across a call boundary.
		static ImplInfo EnsureImpl(Dictionary<string, ImplInfo> implInfos, List<ImplInfo> implOrder, RawRegistration reg)
		{
			if (!implInfos.TryGetValue(reg.ImplementationType, out ImplInfo? info))
			{
				info = new ImplInfo(
					reg.ImplementationType, reg.Implementation, reg.Lifetime,
					LocationInfo.From(reg.Location), reg.Production, reg.ProductionMember)
				{
					IsScan = reg.IsScan,
					Origin = reg.Origin,
					Eager = reg.Eager,
					OnActivated = reg.OnActivated,
					OnRelease = reg.OnRelease,
				};
				implInfos.Add(reg.ImplementationType, info);
				implOrder.Add(info);
			}

			return info;
		}
	}

	/// <summary>
	///     The single encoding of coalescing precedence. Explicit strong registrations claim their service
	///     first; overridable defaults (<c>Default</c>/<c>TryAdd</c>) fill the remaining gaps; closed
	///     registrations synthesized by open generic expansion yield to both - a deliberate declaration,
	///     even an overridable default, beats the blanket expansion, mirroring how a default beats a
	///     blanket scan; scan matches come last. Consumed by the coalescing loop and by the open generic
	///     expansion seed (<see cref="DroppedOverridableDefaults" />), which must agree on who wins.
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
	///     Enumerates registrations in coalescing precedence order (see <see cref="PrecedenceRank" />),
	///     preserving declaration order within each tier - so the container's own registrations still win
	///     over an imported module's, and an earlier import's over a later one's. Each registration is
	///     paired with its index into <paramref name="raw" /> for consumers that track identity across
	///     passes (value equality cannot: two identical attributes coalesce into equal records).
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
	///     The registrations (by index into <paramref name="raw" />) that coalescing will drop in full: an
	///     overridable default whose service key a higher-tier or earlier registration claims. Losing
	///     strong, synthesized and scan registrations are not dropped - they stay collection members and
	///     are built. Sound to compute before open generic expansion runs: synthesized registrations rank
	///     below defaults, so nothing expansion adds to <paramref name="raw" /> can claim a key ahead of one.
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

	// Reports the coalescing conflicts a re-registration of an already-seen implementation raises: a different
	// lifetime (AWT107), a different production strategy (AWT111), or a contradicting per-instance directive -
	// OnActivated/OnRelease/Eager (AWT166). Each is reported at most once per implementation (the reported sets
	// guard that), since coalescing keeps the first registration.
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

		if (ConflictingDirective(info, registration) is { } directive && reportedDirectiveConflicts.Add(registration.ImplementationType))
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

	// The first per-instance directive (OnActivated, OnRelease, or Eager) this registration sets to a value the
	// coalesced instance will not use, or null when none does. Coalescing keeps the first (winning) registration's
	// directives, so a conflict is a later registration explicitly naming a directive value that differs from the
	// winner's: a differing hook, or opting into Eager the winner did not. A registration that leaves a directive
	// unset (a null hook, or Eager left at its default false) states no opinion and merges with the winner rather
	// than conflicting - so the winner's own directives, which this registration inherits, are never a conflict
	// against themselves.
	private static (string Directive, string Winner, string Loser)? ConflictingDirective(ImplInfo info, RawRegistration registration)
	{
		if (registration.OnActivated is not null && !string.Equals(registration.OnActivated, info.OnActivated, StringComparison.Ordinal))
		{
			return ("OnActivated", DescribeHook(info.OnActivated), $"'{registration.OnActivated}'");
		}

		if (registration.OnRelease is not null && !string.Equals(registration.OnRelease, info.OnRelease, StringComparison.Ordinal))
		{
			return ("OnRelease", DescribeHook(info.OnRelease), $"'{registration.OnRelease}'");
		}

		if (registration.Eager && !info.Eager)
		{
			return ("Eager", "false", "true");
		}

		return null;
	}

	private static string DescribeHook(string? hook) => hook is null ? "unset" : $"'{hook}'";

	// Records a registration's implementation as a member of the collection for its (service type, key), in
	// registration order and deduped by implementation. serviceMemberOrder preserves first-seen (type, key) order.
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

	// AWT155: two different imported modules register the same unkeyed service with different
	// implementations at the same precedence tier - two strong registrations, or two closed registrations
	// expanded from open typeof templates - so which wins is decided only by [Import] order, invisible at
	// either module. A cross-tier loss is deterministic by design (an explicit registration beats an
	// expanded one regardless of import order), so it stays silent, as do scans and overridable defaults
	// (which yield by design), the container overriding a module (the intended override mechanism), and
	// keyed collisions (already surfaced as AWT117).
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

	// Records a keyed registration as a member of its service's keyed collection: the [Key] and its
	// implementation, grouped by service (value) type in registration order. keyedMemberOrder preserves the
	// first-seen service order for deterministic emission. An unkeyed registration contributes nothing, and the
	// first registration per (service, key) wins - mirroring single keyed resolution (a later one already lost
	// that slot, alreadyChosen; a genuine duplicate is the caller's AWT117) - so each key maps to a single
	// implementation.
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

	// AWT117: two different implementations claim the same service type and key, so a keyed resolution of
	// that key would be ambiguous. The same implementation re-registered under one key is just a coalesce
	// (first wins), and an unkeyed duplicate keeps the existing first-wins behavior, so neither is reported.
	private static void ReportDuplicateKey(RawRegistration registration, string existingImpl, List<DiagnosticInfo> diagnostics)
	{
		if (registration.Key is null || existingImpl == registration.ImplementationType)
		{
			return;
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.DuplicateKey,
			LocationInfo.From(registration.Location),
			new EquatableArray<string>([Display(registration.ServiceType), registration.Key,])));
	}

	// Two registrations of the same implementation conflict when they produce it differently: a different
	// kind (constructor vs factory vs instance), the same kind naming a different member, or the same
	// member name declared by different owners (a container member and a module member of the same name
	// are different methods). Coalescing keeps the first, so the second would otherwise be dropped
	// without a trace.
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
