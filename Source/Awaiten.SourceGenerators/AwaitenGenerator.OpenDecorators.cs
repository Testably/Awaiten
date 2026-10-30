using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     Expands each open generic <c>[Decorate(typeof(D&lt;&gt;), typeof(IService&lt;&gt;))]</c> into a closed
	///     <see cref="DecorateRegistration" /> for every closing of its service already present in the coalesced graph
	///     under a key the decorator selects (however it was registered - explicit, scanned or open generic),
	///     constructing the matching closing of the decorator. The synthesized registrations carry the open form's
	///     <c>Order</c>, declaration order and the keys present for that closing, so the existing
	///     <see cref="DecoratorChainBuilder" /> interleaves them with explicit closed decorators of the same closing.
	///     Warns AWT171 and skips a closing whose type arguments violate the decorator's constraints; reports AWT123 for
	///     an open decorator that matches no closing at all and AWT210 for a selected key that no closing has.
	/// </summary>
	private static void ExpandOpenDecorators(
		List<DecorateRegistration> decorators,
		List<OpenDecorateRegistration> open,
		List<(string Display, INamedTypeSymbol Symbol)> closedServices,
		CoalescedGraph graph,
		List<DiagnosticInfo> diagnostics)
	{
		foreach (OpenDecorateRegistration decorator in open)
		{
			List<INamedTypeSymbol> closings = MatchingClosings(decorator.Service, closedServices,
				display => decorator.Keys is not { } keys
					? PresentUnderAnyKey(graph, display)
					: keys.Any(key => PresentUnderKey(graph, display, key)));

			// AWT210: a selected key that no closing of the service is registered under.
			if (decorator.Keys is { Count: > 0, } selected && !(selected.Count == 1 && selected[0] is null))
			{
				foreach (string? key in selected.Where(key => !closings.Any(closed => PresentUnderKey(graph, closed.ToDisplayString(FullyQualified), key))))
				{
					diagnostics.Add(new DiagnosticInfo(
						Diagnostics.DecoratedKeyNotRegistered,
						LocationInfo.From(decorator.Location),
						new EquatableArray<string>([
							Display(decorator.Service.ToDisplayString(FullyQualified)),
							Display(decorator.Decorator.ToDisplayString(FullyQualified)),
							key is null ? "null" : KeyDisplay(key),
						])));
				}
			}
			else if (closings.Count == 0)
			{
				// AWT123: the open decorator names a service that has no closing in the graph to decorate.
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.DecoratedServiceNotRegistered,
					LocationInfo.From(decorator.Location),
					new EquatableArray<string>([
						Display(decorator.Service.ToDisplayString(FullyQualified)),
						Display(decorator.Decorator.ToDisplayString(FullyQualified)),
					])));
				continue;
			}

			foreach (INamedTypeSymbol closed in closings)
			{
				ITypeSymbol[] typeArguments = closed.TypeArguments.ToArray();

				// AWT171: the closing's type arguments must satisfy the decorator's type-parameter constraints.
				if (!ConstraintsSatisfied(decorator.Decorator, typeArguments))
				{
					ReportClosingConstraintViolation(decorator.Decorator, typeArguments, decorator.Location, diagnostics);
					continue;
				}

				// Only the keys this closing is registered under, so a key another closing lacks is no AWT210 here.
				string display = closed.ToDisplayString(FullyQualified);
				IReadOnlyList<string?>? keys = decorator.Keys?.Where(key => PresentUnderKey(graph, display, key)).ToList();
				decorators.Add(new DecorateRegistration(
					display,
					closed,
					decorator.Decorator.Construct(typeArguments),
					decorator.Order,
					decorator.DeclarationOrder,
					decorator.Location,
					keys));
			}
		}
	}

	/// <summary>
	///     Expands each open generic <c>[Composite(typeof(C&lt;&gt;), typeof(IService&lt;&gt;))]</c> into a closed
	///     <see cref="CompositeRegistration" /> for every closing of its service present in the coalesced graph under the
	///     composite's key, constructing the matching closing of the composite. Each synthesized composite then follows
	///     the closed <c>[Composite]</c> rules in <see cref="BuildComposites" />. Warns AWT171 and skips a closing whose
	///     type arguments violate the composite's constraints; reports AWT212 for a key that no closing has.
	/// </summary>
	private static void ExpandOpenComposites(
		List<CompositeRegistration> composites,
		List<OpenCompositeRegistration> open,
		List<(string Display, INamedTypeSymbol Symbol)> closedServices,
		CoalescedGraph graph,
		List<DiagnosticInfo> diagnostics)
	{
		foreach (OpenCompositeRegistration composite in open)
		{
			List<INamedTypeSymbol> closings = MatchingClosings(composite.Service, closedServices,
				display => PresentUnderKey(graph, display, composite.Key));

			// AWT212: a keyed open composite whose key no closing of the service is registered under.
			if (composite.Key is not null && closings.Count == 0)
			{
				ReportCompositeKeyNotRegistered(
					composite.Composite.ToDisplayString(FullyQualified), composite.Service.ToDisplayString(FullyQualified),
					composite.Key, composite.Location, diagnostics);
				continue;
			}

			foreach (INamedTypeSymbol closed in closings)
			{
				ITypeSymbol[] typeArguments = closed.TypeArguments.ToArray();

				// AWT171: the closing's type arguments must satisfy the composite's type-parameter constraints.
				if (!ConstraintsSatisfied(composite.Composite, typeArguments))
				{
					ReportClosingConstraintViolation(composite.Composite, typeArguments, composite.Location, diagnostics);
					continue;
				}

				composites.Add(new CompositeRegistration(
					closed.ToDisplayString(FullyQualified),
					closed,
					composite.Composite.Construct(typeArguments),
					composite.Lifetime,
					composite.Location,
					composite.Key));
			}
		}
	}

	/// <summary>
	///     The closed generic service symbols whose open form is <paramref name="openService" /> and which
	///     <paramref name="present" /> finds in the coalesced graph, in first-seen registration order. Only the keys
	///     the decorator/composite selects count, matching the closed forms: a closing registered only under other
	///     keys is left alone rather than reported.
	/// </summary>
	private static List<INamedTypeSymbol> MatchingClosings(
		INamedTypeSymbol openService,
		List<(string Display, INamedTypeSymbol Symbol)> closedServices,
		Func<string, bool> present)
	{
		List<INamedTypeSymbol> closings = new();
		foreach ((string display, INamedTypeSymbol symbol) in closedServices)
		{
			if (symbol is { IsGenericType: true, }
			    && SymbolEqualityComparer.Default.Equals(symbol.ConstructedFrom, openService)
			    && present(display))
			{
				closings.Add(symbol);
			}
		}

		return closings;
	}

	/// <summary>
	///     Whether the coalesced graph holds <paramref name="service" /> under <paramref name="key" /> (the unkeyed
	///     resolution key when <see langword="null" />), either as the single-dispatch winner or as a non-empty
	///     collection membership.
	/// </summary>
	private static bool PresentUnderKey(CoalescedGraph graph, string service, string? key)
	{
		ServiceKey serviceKey = new(service, key);
		return graph.ServiceToImpl.ContainsKey(serviceKey)
		       || (graph.ServiceMembers.TryGetValue(serviceKey, out List<string>? members) && members.Count > 0);
	}

	/// <summary>Whether the coalesced graph holds <paramref name="service" /> unkeyed or under any user key.</summary>
	private static bool PresentUnderAnyKey(CoalescedGraph graph, string service)
		=> PresentUnderKey(graph, service, null)
		   || graph.ServiceToImpl.Keys.Any(key => key.Service == service && IsUserKey(key.Key));

	/// <summary>
	///     Reports AWT171 (a warning) for a closing (of an open decorator or composite) whose type arguments violate
	///     the open definition's type-parameter constraints, naming the would-be closed type and the open definition.
	///     Unlike the open generic registration's AWT126 error, the base service still resolves - only this closing is
	///     left as-is - so a broadly-applied decorator/composite whose constraints exclude some closings degrades
	///     gracefully instead of failing the whole compilation.
	/// </summary>
	private static void ReportClosingConstraintViolation(
		INamedTypeSymbol openDefinition,
		ITypeSymbol[] typeArguments,
		Location? location,
		List<DiagnosticInfo> diagnostics)
		=> diagnostics.Add(new DiagnosticInfo(
			Diagnostics.DecoratorClosingConstraintViolation,
			LocationInfo.From(location),
			new EquatableArray<string>([
				Display(openDefinition.Construct(typeArguments).ToDisplayString(FullyQualified)),
				Display(openDefinition.ToDisplayString(FullyQualified)),
			])));

	/// <summary>
	///     The distinct closed generic service symbols across the raw registrations (explicit, scanned or expanded
	///     from open generic registrations), in first-seen order, deduped by display string. The symbol source for
	///     open decorator/composite expansion, since the coalesced graph keys its services only by string.
	/// </summary>
	private static List<(string Display, INamedTypeSymbol Symbol)> ClosedGenericServices(List<RawRegistration> raw)
	{
		List<(string, INamedTypeSymbol)> closedServices = new();
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (RawRegistration registration in raw)
		{
			if (registration.ServiceSymbol is { IsGenericType: true, IsUnboundGenericType: false, } closed
			    && seen.Add(registration.ServiceType))
			{
				closedServices.Add((registration.ServiceType, closed));
			}
		}

		return closedServices;
	}
}
