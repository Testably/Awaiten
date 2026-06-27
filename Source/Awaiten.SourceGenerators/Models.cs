using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Awaiten.SourceGenerators;

/// <summary>
///     The lifetime of a registration. <see cref="Scoped" /> is declarable but resolved as
///     <see cref="Singleton" /> until real scope semantics arrive in a later phase.
/// </summary>
internal enum Lifetime
{
	Singleton,
	Transient,
	Scoped,
}

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

/// <summary>
///     An equatable description of a diagnostic to report, deferred until the source-output stage.
/// </summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> MessageArgs)
{
	public Diagnostic ToDiagnostic()
		=> Diagnostic.Create(Descriptor, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, MessageArgs.AsArray());
}

/// <summary>
///     A single registration on a container: an implementation, the service type it is resolved as,
///     the implementation's simple name (used to name generated members), its lifetime, and the
///     service types of its selected constructor's parameters.
/// </summary>
internal sealed record RegistrationModel(
	string ServiceType,
	string ImplementationType,
	string Name,
	Lifetime Lifetime,
	EquatableArray<string> ConstructorParameterServiceTypes);

/// <summary>
///     A type declaration that encloses the container (outermost first), so the generator can
///     re-open it as a <c>partial</c> when the container is a nested type.
/// </summary>
internal sealed record TypeDeclaration(string Keyword, string Name);

/// <summary>
///     The fully-resolved, equatable model of a single <c>[Container]</c> partial class, ready for
///     emission.
/// </summary>
internal sealed record ContainerModel(
	string? Namespace,
	EquatableArray<TypeDeclaration> ContainingTypes,
	string TypeName,
	string HintName,
	EquatableArray<RegistrationModel> Registrations,
	EquatableArray<DiagnosticInfo> Diagnostics)
{
	public bool HasErrors
	{
		get
		{
			foreach (DiagnosticInfo diagnostic in Diagnostics.AsArray())
			{
				if (diagnostic.Descriptor.DefaultSeverity == DiagnosticSeverity.Error)
				{
					return true;
				}
			}

			return false;
		}
	}
}
