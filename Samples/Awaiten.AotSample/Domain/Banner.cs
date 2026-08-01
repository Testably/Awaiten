namespace Awaiten.AotSample.Domain;

/// <summary>
///     A host-owned (external) service, registered directly in the service collection.
/// </summary>
public sealed class Banner
{
	public string Text { get; } = "Awaiten on AOT";
}
