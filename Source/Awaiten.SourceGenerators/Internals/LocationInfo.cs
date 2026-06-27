using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     An equatable snapshot of a <see cref="Location" /> that does not capture any Roslyn symbol or
///     syntax instance, so it can flow through the incremental pipeline without breaking caching.
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
	public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

	public static LocationInfo? From(Location? location)
	{
		if (location?.SourceTree is null)
		{
			return null;
		}

		return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
	}
}