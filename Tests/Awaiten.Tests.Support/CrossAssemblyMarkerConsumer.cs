namespace Awaiten.Tests.Support;

/// <summary>
///     A service whose consumer-side markers (<c>[FromKey]</c>, <c>[Inject]</c>) are applied in this referenced
///     assembly. The markers bind to the internal <c>Awaiten.FromKeyAttribute</c>/<c>Awaiten.InjectAttribute</c>
///     injected into this assembly, distinct from the copies the main test assembly gets. A container in
///     <c>Awaiten.Tests</c> registers and resolves it, exercising that the generator's by-metadata-name
///     classification still reads markers written across the assembly boundary despite the per-assembly identity.
/// </summary>
public interface ICrossAssemblyChannel
{
	string Name { get; }
}

public sealed class PrimaryChannel : ICrossAssemblyChannel
{
	public string Name => "primary";
}

public sealed class BackupChannel : ICrossAssemblyChannel
{
	public string Name => "backup";
}

public sealed class CrossAssemblyMarkerConsumer
{
	public CrossAssemblyMarkerConsumer([FromKey("backup")] ICrossAssemblyChannel selected) => Selected = selected;

	/// <summary>The keyed dependency selected by the cross-assembly <c>[FromKey("backup")]</c>.</summary>
	public ICrossAssemblyChannel Selected { get; }

	/// <summary>An <c>[Inject]</c> property whose attribute is likewise declared in this referenced assembly.</summary>
	[Inject]
	public ICrossAssemblyChannel? Primary { get; set; }
}
