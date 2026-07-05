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
			if (registration.Key is null
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

			ServiceKey serviceKey = new(registration.ServiceType, registration.Key);
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
			// earlier) registration replaces it transparently. Two colliding Defaults warn (AWT148); TryAdd is silent.
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
	///     The single encoding of coalescing precedence: explicit strong registrations first, then overridable
	///     defaults (<c>Default</c>/<c>TryAdd</c>), then open-generic-synthesized registrations, then scan matches.
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

	// Reports the coalescing conflicts a re-registration of an already-seen implementation raises: a different
	// lifetime (AWT107), a different production strategy (AWT111), or a contradicting OnActivated/OnRelease/Eager
	// directive (AWT166). Lifetime and production conflicts are reported once per implementation, each directive
	// conflict once per (implementation, directive), since coalescing keeps the first registration.
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

	// Every per-instance directive (OnActivated, OnRelease, or Eager) this registration sets to a value the
	// coalesced instance will not use, each yielded independently so it can be reported on its own. Coalescing
	// keeps the first (winning) registration's directives, so a conflict is a later registration explicitly
	// naming a directive value that differs from the winner's: a differing hook, or opting into Eager the winner
	// did not. A registration that leaves a directive unset (a null hook, or Eager left at its default false)
	// states no opinion and merges with the winner rather than conflicting - so the winner's own directives,
	// which this registration inherits, are never a conflict against themselves.
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

	// AWT155: two different imported modules register the same unkeyed service with different implementations at
	// the same precedence tier, so which wins is decided only by [Import] order, invisible at either module. A
	// cross-tier loss, scans, overridable defaults, container-over-module and keyed collisions (AWT117) stay silent.
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

	// Records a keyed registration as a member of its service's keyed collection ([Key] and implementation),
	// grouped by service (value) type in registration order. An unkeyed registration contributes nothing, and the
	// first registration per (service, key) wins (a genuine duplicate is the caller's AWT117).
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

	// AWT117: two different implementations claim the same service type and key, so a keyed resolution would be
	// ambiguous. The same implementation re-registered, or an unkeyed duplicate, is just first-wins and not reported.
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

	// Two registrations of the same implementation conflict when they produce it differently: a different kind
	// (constructor vs factory vs instance), a different member, or the same member name on different owners.
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
