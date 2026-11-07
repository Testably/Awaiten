using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     The fully-resolved, equatable model of a single <c>[Container]</c> partial class, ready for
///     emission. <see cref="HasBaseErrors" /> marks a container whose extended base has an error that only the
///     base reports, so the graph is still broken and only its shell is emitted. <see cref="ImportServices" />,
///     <see cref="ImportedServices" /> and <see cref="ImportedDefinitions" /> are the types the container leaves to
///     the host: <c>[ImportServices]</c>, the closed <c>[ImportService]</c> types and the unbound generic
///     definitions of the open ones. <see cref="Factories" /> are the factories the container generates for its private
///     implementations, so a container that extends it can build them.
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
	bool HasBaseErrors = false,
	bool ImportServices = false,
	EquatableArray<string> ImportedServices = default,
	EquatableArray<string> ImportedDefinitions = default,
	EquatableArray<RegistrationFactory> Factories = default)
{
	public bool Imports => ImportServices || ImportedServices.Count > 0 || ImportedDefinitions.Count > 0;

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