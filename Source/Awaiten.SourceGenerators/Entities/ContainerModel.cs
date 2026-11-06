using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     The fully-resolved, equatable model of a single <c>[Container]</c> partial class, ready for
///     emission. <see cref="HasBaseErrors" /> marks a container whose extended base has an error that only the
///     base reports, so the graph is still broken and only its shell is emitted.
/// </summary>
internal sealed record ContainerModel(
	string? Namespace,
	EquatableArray<TypeDeclaration> ContainingTypes,
	string TypeName,
	string HintName,
	EquatableArray<InstanceModel> Instances,
	EquatableArray<DiagnosticInfo> Diagnostics,
	bool Strict,
	bool SyncAfterInit,
	bool HasAsyncDisposable,
	EquatableArray<ServiceMembers> Collections = default,
	EquatableArray<string> VarianceCandidates = default,
	EquatableArray<KeyedServiceMembers> KeyedCollections = default,
	bool HasBaseErrors = false)
{
	public bool HasErrors
	{
		get
		{
			if (HasBaseErrors)
			{
				return true;
			}

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