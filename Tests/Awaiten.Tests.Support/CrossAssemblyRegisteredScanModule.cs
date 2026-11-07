namespace Awaiten.Tests.Support;

/// <summary>The marker of the appliances <see cref="RegisteredScanModule" /> scans for.</summary>
public interface ISupportAppliance;

/// <summary>The service <see cref="RegisteredScanModule" /> registers the scanned kettle under.</summary>
public interface ISupportKettle;

/// <summary>A public implementation the module both registers and matches with its own scan.</summary>
public sealed class SupportKettle : ISupportAppliance, ISupportKettle;

/// <summary>
///     A <c>[Module]</c> in an assembly that grants no <c>InternalsVisibleTo</c>, which both registers a public
///     implementation and matches it with its own scan, so every container constructs it and shares one instance.
/// </summary>
[Module]
[Scan<ISupportAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
[Singleton<SupportKettle, ISupportKettle>]
public static partial class RegisteredScanModule;
