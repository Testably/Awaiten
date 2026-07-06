using System;
using System.Collections.Generic;
using System.Linq;
using Awaiten.Tests.Support.Filtering;
using Awaiten.Tests.Support.Filtering.Legacy;
using Awaiten.Tests.Support.Filtering.Primary;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of the <c>[Scan]</c> name, namespace and exclude filters against a referenced assembly:
///     each filter axis narrows the marker-assignable candidates in <c>Awaiten.Tests.Support</c> before they are
///     registered, so the resolved marker collection contains exactly the surviving concrete types. The three
///     fixtures (<see cref="OrderHandler" />, <see cref="PaymentService" />, <see cref="LegacyHandler" />) vary by
///     simple name and namespace so every axis can be exercised end-to-end.
/// </summary>
public partial class ScanFilteringTests
{
	[Fact]
	public async Task NamePatterns_RegisterOnlyCandidatesMatchingAnIncludeName()
	{
		using NamePatternContainer.Root container = new();

		List<Type> registered = container.Resolve<IEnumerable<IScanFilterCandidate>>().Select(c => c.GetType()).ToList();

		// "*Handler" keeps the two handlers and drops PaymentService, across the assembly boundary.
		await That(registered).HasCount(2);
		await That(registered).Contains(typeof(OrderHandler)).And.Contains(typeof(LegacyHandler));
	}

	[Fact]
	public async Task NamePatterns_IncludeAndExcludeCombineToASingleMatch()
	{
		using NameIncludeExcludeContainer.Root container = new();

		List<Type> registered = container.Resolve<IEnumerable<IScanFilterCandidate>>().Select(c => c.GetType()).ToList();

		// "*Handler" includes both handlers, then "!Legacy*" removes the legacy one, leaving only OrderHandler.
		await That(registered).HasCount(1);
		await That(registered).Contains(typeof(OrderHandler));
	}

	[Fact]
	public async Task NamespacePatterns_RestrictToTheNamedNamespace()
	{
		using NamespacePatternContainer.Root container = new();

		List<Type> registered = container.Resolve<IEnumerable<IScanFilterCandidate>>().Select(c => c.GetType()).ToList();

		// The Primary namespace holds OrderHandler and PaymentService; LegacyHandler sits in the sibling Legacy namespace.
		await That(registered).HasCount(2);
		await That(registered).Contains(typeof(OrderHandler)).And.Contains(typeof(PaymentService));
	}

	[Fact]
	public async Task NamespacePatterns_ExcludeALegacyNamespaceByTrailingSegment()
	{
		using NamespaceExcludeContainer.Root container = new();

		List<Type> registered = container.Resolve<IEnumerable<IScanFilterCandidate>>().Select(c => c.GetType()).ToList();

		// "!**.Legacy" drops everything under a Legacy namespace segment, keeping the two Primary candidates.
		await That(registered).HasCount(2);
		await That(registered).Contains(typeof(OrderHandler)).And.Contains(typeof(PaymentService));
	}

	[Fact]
	public async Task Exclude_RemovesTheExactCrossAssemblyType()
	{
		using ExcludeTypeContainer.Root container = new();

		List<Type> registered = container.Resolve<IEnumerable<IScanFilterCandidate>>().Select(c => c.GetType()).ToList();

		// Exclude drops PaymentService by exact identity, leaving both handlers.
		await That(registered).HasCount(2);
		await That(registered).Contains(typeof(OrderHandler)).And.Contains(typeof(LegacyHandler));
	}

	[Fact]
	public async Task Filters_ComposeAcrossNameAndNamespaceWithAnd()
	{
		using ComposedFilterContainer.Root container = new();

		List<Type> registered = container.Resolve<IEnumerable<IScanFilterCandidate>>().Select(c => c.GetType()).ToList();

		// "*Handler" AND the Primary namespace: PaymentService fails the name filter, LegacyHandler the namespace
		// filter, so only OrderHandler survives both.
		await That(registered).HasCount(1);
		await That(registered).Contains(typeof(OrderHandler));
	}

	[Container]
	[Scan(typeof(IScanFilterCandidate), InAssembliesOf = new[] { typeof(IScanFilterCandidate) }, As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton, NamePatterns = new[] { "*Handler" })]
	public static partial class NamePatternContainer;

	[Container]
	[Scan(typeof(IScanFilterCandidate), InAssembliesOf = new[] { typeof(IScanFilterCandidate) }, As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton, NamePatterns = new[] { "*Handler", "!Legacy*" })]
	public static partial class NameIncludeExcludeContainer;

	[Container]
	[Scan(typeof(IScanFilterCandidate), InAssembliesOf = new[] { typeof(IScanFilterCandidate) }, As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton, NamespacePatterns = new[] { "Awaiten.Tests.Support.Filtering.Primary" })]
	public static partial class NamespacePatternContainer;

	[Container]
	[Scan(typeof(IScanFilterCandidate), InAssembliesOf = new[] { typeof(IScanFilterCandidate) }, As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton, NamespacePatterns = new[] { "!**.Legacy" })]
	public static partial class NamespaceExcludeContainer;

	[Container]
	[Scan(typeof(IScanFilterCandidate), InAssembliesOf = new[] { typeof(IScanFilterCandidate) }, As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton, Exclude = new[] { typeof(PaymentService) })]
	public static partial class ExcludeTypeContainer;

	[Container]
	[Scan(typeof(IScanFilterCandidate), InAssembliesOf = new[] { typeof(IScanFilterCandidate) }, As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton, NamePatterns = new[] { "*Handler" }, NamespacePatterns = new[] { "**.Primary" })]
	public static partial class ComposedFilterContainer;
}
