using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     The fully-resolved, equatable model of a single <c>[Container]</c> partial class, ready for
///     emission.
/// </summary>
internal sealed record ContainerModel(
	string? Namespace,
	EquatableArray<TypeDeclaration> ContainingTypes,
	string TypeName,
	string HintName,
	EquatableArray<InstanceModel> Instances,
	EquatableArray<DiagnosticInfo> Diagnostics,
	bool Strict,
	bool SyncResolveAfterInit,
	bool HasAsyncDisposable,
	EquatableArray<ServiceMembers> Collections = default)
{
	public bool HasErrors
	{
		get
		{
			foreach (DiagnosticInfo diagnostic in Diagnostics.AsArray())
			{
				if (diagnostic.EffectiveSeverity == DiagnosticSeverity.Error)
				{
					return true;
				}
			}

			return false;
		}
	}
}