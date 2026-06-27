using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Awaiten.SourceGenerators;

/// <summary>
///     The lifetime of a registration. A <see cref="Singleton" /> is created once and shared for the
///     container's life; a <see cref="Transient" /> is created per request; a <see cref="Scoped" />
///     instance is created once per scope (the container acts as the root scope).
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
		=> Diagnostic.Create(
			Descriptor,
			Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None,
			MessageArgs.AsArray().Cast<object?>().ToArray());
}

/// <summary>
///     A single constructed instance on a container: one implementation, the implementation's simple
///     name (used to name generated members), its lifetime, the (one or more) service types it is
///     exposed as, the service types of its selected constructor's parameters, whether it needs
///     disposing, and whether it is a reference type (only reference-type cache fields can be marked
///     <c>volatile</c> for the lock-free fast path). Registrations of the same implementation are
///     coalesced into one instance, so a multi-service registration shares a single object.
/// </summary>
internal sealed record InstanceModel(
	string ImplementationType,
	string Name,
	Lifetime Lifetime,
	EquatableArray<string> ServiceTypes,
	EquatableArray<string> ConstructorParameterServiceTypes,
	bool IsDisposable,
	bool IsReferenceType);

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
	EquatableArray<InstanceModel> Instances,
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
