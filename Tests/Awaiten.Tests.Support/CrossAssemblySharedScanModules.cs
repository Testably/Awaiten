namespace Awaiten.Tests.Support;

/// <summary>The marker <see cref="SharedFactoryScanModule" /> scans for.</summary>
public interface ISharedFactoryAppliance;

/// <summary>The service <see cref="SharedFactoryScanModule" /> registers its kettle under.</summary>
public interface ISharedFactoryKettle;

/// <summary>A public implementation its module builds through a factory and matches with its own scan.</summary>
public sealed class SharedFactoryKettle : ISharedFactoryAppliance, ISharedFactoryKettle;

/// <summary>A <c>[Module]</c> that registers an implementation through a factory and matches it with its own scan.</summary>
[Module]
[Scan<ISharedFactoryAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
[Singleton<SharedFactoryKettle, ISharedFactoryKettle>(Factory = nameof(Create))]
public static partial class SharedFactoryScanModule
{
	/// <summary>Builds the kettle for the registration.</summary>
	public static SharedFactoryKettle Create() => new();
}

/// <summary>The marker <see cref="SharedScanModule" /> scans for.</summary>
public interface ISharedAppliance;

/// <summary>The service <see cref="SharedRegistrationModule" /> registers the kettle under.</summary>
public interface ISharedKettle;

/// <summary>A public implementation one module registers and another matches with its scan.</summary>
public sealed class SharedKettle : ISharedAppliance, ISharedKettle;

/// <summary>A <c>[Module]</c> that registers the kettle <see cref="SharedScanModule" /> matches.</summary>
[Module]
[Singleton<SharedKettle, ISharedKettle>]
public static partial class SharedRegistrationModule;

/// <summary>A <c>[Module]</c> whose scan matches the kettle another module or a container registers.</summary>
[Module]
[Scan<ISharedAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
public static partial class SharedScanModule;

/// <summary>The marker <see cref="SharedHeaterScanModule" /> scans for.</summary>
public interface ISharedHeater;

/// <summary>A public implementation two modules match with their scans under different services.</summary>
public sealed class SharedBoiler : ISharedAppliance, ISharedHeater;

/// <summary>A <c>[Module]</c> whose scan matches the boiler <see cref="SharedScanModule" /> matches too.</summary>
[Module]
[Scan<ISharedHeater>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
public static partial class SharedHeaterScanModule;
