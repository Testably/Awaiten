// Fixtures for cross-assembly [Scan] filter tests. The implementers below deliberately vary by simple
// name (*Handler vs *Service) and by namespace (Primary vs Legacy) so a scan's NamePatterns,
// NamespacePatterns and Exclude filters can each be exercised at runtime against a referenced assembly.
// A dedicated marker keeps these out of the ICrossAssemblyPlugin counts the other cross-assembly tests assert.

namespace Awaiten.Tests.Support.Filtering
{
	/// <summary>Marker for the cross-assembly scan-filter fixtures; its implementers span the namespaces below.</summary>
	public interface IScanFilterCandidate;
}

namespace Awaiten.Tests.Support.Filtering.Primary
{
	/// <summary>A handler in the Primary namespace: matches a <c>*Handler</c> name filter and the Primary namespace filter.</summary>
	public sealed class OrderHandler : IScanFilterCandidate;

	/// <summary>A service in the Primary namespace: fails a <c>*Handler</c> name filter but passes the Primary namespace filter.</summary>
	public sealed class PaymentService : IScanFilterCandidate;
}

namespace Awaiten.Tests.Support.Filtering.Legacy
{
	/// <summary>A handler in the Legacy namespace: matches a <c>*Handler</c> name filter but fails a Primary-only namespace filter.</summary>
	public sealed class LegacyHandler : IScanFilterCandidate;
}
