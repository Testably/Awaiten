using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     Removes parameterized ([Arg]) implementations from every collection's membership: such a service is
	///     built fresh from its runtime arguments and is reachable only through its <c>Func&lt;TArg…, T&gt;</c>
	///     factory, so it is never a collection member.
	/// </summary>
	private static void PruneParameterizedMembers(List<InstanceModel> instances, Dictionary<ServiceKey, List<string>> serviceMembers)
	{
		HashSet<string> parameterized = new(StringComparer.Ordinal);
		foreach (InstanceModel instance in instances)
		{
			if (instance.IsParameterized)
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
	}

	private static (List<ImplInfo> Order, Dictionary<ServiceKey, string> ServiceToImpl, Dictionary<ServiceKey, List<string>> Members, List<ServiceKey> MemberOrder, List<(string ServiceType, INamedTypeSymbol Symbol)> VarianceCandidates) CoalesceByImplementation(
		List<RawRegistration> raw,
		List<DiagnosticInfo> diagnostics)
	{
		List<ImplInfo> implOrder = new();
		Dictionary<string, ImplInfo> implInfos = new(StringComparer.Ordinal);
		Dictionary<ServiceKey, string> serviceToImpl = new();
		HashSet<string> reportedConflicts = new(StringComparer.Ordinal);
		HashSet<string> reportedProductionConflicts = new(StringComparer.Ordinal);

		// Collection membership: every registration of a service, keyed by (service type, resolution key) and
		// deduped by implementation, kept in registration order - so an unkeyed IEnumerable<T> resolves the
		// unkeyed registrations and a [FromKey("k")] IEnumerable<T> the registrations under "k".
		// serviceMemberOrder preserves the first-seen (type, key) order for deterministic emission.
		Dictionary<ServiceKey, List<string>> serviceMembers = new();
		List<ServiceKey> serviceMemberOrder = new();

		// Variance: every unkeyed registration of a closed generic interface whose definition declares variance
		// (in/out), keyed by its fully-qualified string, in registration order. When a consumer requests a closed
		// generic interface with no exact registration, the request is redirected to a variance-compatible
		// candidate here (a registered IHandler<DomainEvent> satisfying a requested IHandler<OrderPlaced> via
		// `in T`). Keyed registrations are reached only through their key, so they are never variance-redirect
		// targets; an invariant interface can never satisfy a different closure, so it is not a candidate.
		List<(string ServiceType, INamedTypeSymbol Symbol)> varianceCandidates = new();
		HashSet<string> varianceSeen = new(StringComparer.Ordinal);

		foreach (RawRegistration registration in raw)
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

			// A lifetime (AWT107) or production (AWT111) conflict is a property of the implementation, not of any
			// single service type, so it is checked before the per-service dedup below; otherwise re-registering
			// the same service type differently would be skipped and the contradiction silently dropped.
			// Coalescing keeps the first, so the conflicting one is reported rather than ignored. A scan
			// registration is overridable and yields to whatever an explicit registration (always processed
			// first) fixed for the implementation, so it is exempt from that check - but two scans that match
			// the same implementation with different lifetimes contradict each other with nothing explicit to
			// yield to, so that is surfaced as AWT142 rather than silently resolved by attribute order.
			if (!registration.IsScan)
			{
				ReportCoalescingConflicts(info, registration, reportedConflicts, reportedProductionConflicts, diagnostics);
			}
			else if (info is { IsScan: true, } && info.Lifetime != registration.Lifetime
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

			ServiceKey serviceKey = new(registration.ServiceType, registration.Key);
			bool alreadyChosen = serviceToImpl.TryGetValue(serviceKey, out string? existingImpl);

			// Every registration is a member of the collection for its (service type, key): an unkeyed
			// IEnumerable<T> resolves the unkeyed registrations, a [FromKey("k")] IEnumerable<T> the ones under
			// "k". A member is built even when it loses the single-resolution slot to an earlier registration,
			// since it is reachable through the collection.
			AddCollectionMember(serviceMembers, serviceMemberOrder, serviceKey, registration.ImplementationType);
			EnsureImpl(implInfos, implOrder, registration);

			if (alreadyChosen)
			{
				ReportDuplicateKey(registration, existingImpl, diagnostics);
				continue;
			}

			ImplInfo chosen = EnsureImpl(implInfos, implOrder, registration);
			serviceToImpl[serviceKey] = registration.ImplementationType;
			chosen.Services.Add(serviceKey);
		}

		return (implOrder, serviceToImpl, serviceMembers, serviceMemberOrder, varianceCandidates);

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
					LocationInfo.From(reg.Location), reg.Production, reg.ProductionMember, reg.IsScan);
				implInfos.Add(reg.ImplementationType, info);
				implOrder.Add(info);
			}

			return info;
		}
	}

	// Reports the coalescing conflicts a re-registration of an already-seen implementation raises: a different
	// lifetime (AWT107) or a different production strategy (AWT111). Each is reported at most once per
	// implementation (the reported sets guard that), since coalescing keeps the first registration.
	private static void ReportCoalescingConflicts(
		ImplInfo? info,
		RawRegistration registration,
		HashSet<string> reportedConflicts,
		HashSet<string> reportedProductionConflicts,
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
	}

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
	// kind (constructor vs factory vs instance), or the same kind naming a different container member.
	// Coalescing keeps the first, so the second would otherwise be dropped without a trace.
	private static bool ConflictsWith(ImplInfo info, RawRegistration registration)
		=> info.Production != registration.Production
		   || !string.Equals(info.ProductionMember, registration.ProductionMember, StringComparison.Ordinal);

	private static string DescribeProduction(ProductionKind production, string? member)
		=> production switch
		{
			ProductionKind.Factory => $"factory '{member}'",
			ProductionKind.Instance => $"instance '{member}'",
			_ => "a constructor",
		};
}
