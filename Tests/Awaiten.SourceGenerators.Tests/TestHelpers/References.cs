using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators.Tests.TestHelpers;

/// <summary>
///     Builds the metadata references for an in-memory test compilation: every loaded assembly plus
///     the Awaiten attributes and any extra types the caller needs resolvable.
/// </summary>
internal static class References
{
	public static List<PortableExecutableReference> For(Type[] types) =>
		AppDomain.CurrentDomain.GetAssemblies()
			.Where(x => !x.IsDynamic && !string.IsNullOrWhiteSpace(x.Location))
			.Select(x => x.Location)
			.Concat([
				typeof(ContainerAttribute).Assembly.Location,
				..types.Select(t => t.Assembly.Location),
			])
			.Distinct()
			.Select(loc => MetadataReference.CreateFromFile(loc))
			.ToList();
}
