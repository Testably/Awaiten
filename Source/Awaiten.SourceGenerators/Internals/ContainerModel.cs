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
	bool SyncResolveAfterInit)
{
	/// <summary>
	///     True when any instance requires asynchronous initialization (directly or transitively), so the
	///     container emits a memoizing async resolution path in addition to the synchronous one.
	/// </summary>
	public bool HasAsync
	{
		get
		{
			foreach (InstanceModel instance in Instances.AsArray())
			{
				if (instance.IsAsyncTainted)
				{
					return true;
				}
			}

			return false;
		}
	}

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