using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     Expands each open generic <c>[Decorate(typeof(D&lt;&gt;), typeof(IService&lt;&gt;))]</c> into a closed
	///     <see cref="DecorateRegistration" /> for every closing of its service already present in the coalesced graph
	///     (an unkeyed winner or collection member, however it was registered - explicit, scanned or open generic),
	///     constructing the matching closing of the decorator. The synthesized registrations carry the open form's
	///     <c>Order</c> and declaration order, so the existing <see cref="DecoratorChainBuilder" /> interleaves them
	///     with explicit closed decorators of the same closing. Reports AWT126 for a closing whose type arguments
	///     violate the decorator's constraints and AWT123 for an open decorator that matches no closing at all.
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
			List<INamedTypeSymbol> closings = MatchingClosings(decorator.Service, closedServices, graph);

			// AWT123: the open decorator names a service that has no closing in the graph to decorate.
			if (closings.Count == 0)
			{
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

				// AWT126: the closing's type arguments must satisfy the decorator's type-parameter constraints.
				if (!ConstraintsSatisfied(decorator.Decorator, typeArguments))
				{
					ReportClosingConstraintViolation(decorator.Decorator, typeArguments, decorator.Location, diagnostics);
					continue;
				}

				decorators.Add(new DecorateRegistration(
					closed.ToDisplayString(FullyQualified),
					closed,
					decorator.Decorator.Construct(typeArguments),
					decorator.Order,
					decorator.DeclarationOrder,
					decorator.Location));
			}
		}
	}

	/// <summary>
	///     Expands each open generic <c>[Composite(typeof(C&lt;&gt;), typeof(IService&lt;&gt;))]</c> into a closed
	///     <see cref="CompositeRegistration" /> for every closing of its service present in the coalesced graph,
	///     constructing the matching closing of the composite. Each synthesized composite then follows the closed
	///     <c>[Composite]</c> rules in <see cref="BuildComposites" />. Reports AWT126 for a closing whose type
	///     arguments violate the composite's constraints.
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
			foreach (INamedTypeSymbol closed in MatchingClosings(composite.Service, closedServices, graph))
			{
				ITypeSymbol[] typeArguments = closed.TypeArguments.ToArray();

				// AWT126: the closing's type arguments must satisfy the composite's type-parameter constraints.
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
					composite.Location));
			}
		}
	}

	/// <summary>
	///     The closed generic service symbols whose open form is <paramref name="openService" /> and which are present
	///     in the coalesced graph under the unkeyed resolution key (a single-dispatch winner or a collection member),
	///     in first-seen registration order. Only unkeyed closings are decorated/composed, matching the closed forms:
	///     a purely keyed closing is left alone rather than reported.
	/// </summary>
	private static List<INamedTypeSymbol> MatchingClosings(
		INamedTypeSymbol openService,
		List<(string Display, INamedTypeSymbol Symbol)> closedServices,
		CoalescedGraph graph)
	{
		List<INamedTypeSymbol> closings = new();
		foreach ((string display, INamedTypeSymbol symbol) in closedServices)
		{
			if (symbol is { IsGenericType: true, }
			    && SymbolEqualityComparer.Default.Equals(symbol.ConstructedFrom, openService)
			    && PresentWithNullKey(graph, display))
			{
				closings.Add(symbol);
			}
		}

		return closings;
	}

	/// <summary>
	///     Whether the coalesced graph holds <paramref name="service" /> under the unkeyed resolution key, either as
	///     the single-dispatch winner or as a non-empty collection membership.
	/// </summary>
	private static bool PresentWithNullKey(CoalescedGraph graph, string service)
	{
		ServiceKey key = new(service, null);
		return graph.ServiceToImpl.ContainsKey(key)
		       || (graph.ServiceMembers.TryGetValue(key, out List<string>? members) && members.Count > 0);
	}

	/// <summary>
	///     Reports AWT126 for a closing (of an open decorator or composite) whose type arguments violate the open
	///     definition's type-parameter constraints, naming the would-be closed type and the open definition, mirroring
	///     the open generic registration's constraint report.
	/// </summary>
	private static void ReportClosingConstraintViolation(
		INamedTypeSymbol openDefinition,
		ITypeSymbol[] typeArguments,
		Location? location,
		List<DiagnosticInfo> diagnostics)
		=> diagnostics.Add(new DiagnosticInfo(
			Diagnostics.OpenGenericConstraintViolation,
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
