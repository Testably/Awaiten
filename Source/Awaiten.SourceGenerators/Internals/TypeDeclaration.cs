namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     A type declaration that encloses the container (outermost first), so the generator can
///     re-open it as a <c>partial</c> when the container is a nested type.
/// </summary>
internal sealed record TypeDeclaration(string Keyword, string Name);