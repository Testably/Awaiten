using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>The marker <see cref="HandleApplianceScanModule" /> scans for.</summary>
public interface IHandleAppliance;

/// <summary>The convention both the kettle module and the scanning module declare.</summary>
public interface IHandleWarmable
{
	/// <summary>How often a container warmed the instance.</summary>
	int Warmings { get; }

	/// <summary>Warms the instance.</summary>
	Task WarmAsync();
}

/// <summary>A brewer whose internal implementation takes the internal kettle its module registers.</summary>
public interface IHandleBrewer
{
	/// <summary>The internal kettle the brewer received.</summary>
	object Kettle { get; }
}

internal interface IHandleKettle;

internal sealed class HandleKettle : IHandleAppliance, IHandleKettle, IHandleWarmable
{
	public int Warmings { get; private set; }

	public Task WarmAsync()
	{
		Warmings++;
		return Task.CompletedTask;
	}
}

internal sealed class HandleBrewer(IHandleKettle kettle) : IHandleBrewer
{
	public object Kettle => kettle;
}

/// <summary>
///     A <c>[Module]</c> that registers an internal kettle under an internal service, which its brewer takes, and under
///     the marker <see cref="HandleApplianceScanModule" /> scans for.
/// </summary>
[Module]
[AsyncInitializer<IHandleWarmable>(nameof(IHandleWarmable.WarmAsync))]
[Singleton<HandleKettle, IHandleKettle>]
[Singleton<HandleKettle, IHandleAppliance>]
[Singleton<HandleBrewer, IHandleBrewer>]
public static partial class HandleKettleModule;

/// <summary>A service <see cref="HandleBoilerModule" /> registers its boiler under.</summary>
public interface IHandleHeater;

internal sealed class HandleBoiler : IHandleAppliance, IHandleHeater;

/// <summary>
///     A <c>[Module]</c> that registers an internal boiler under two public services, which one handle shares. The
///     marker registration is an overridable default, so it yields to the kettle module's without AWT155.
/// </summary>
[Module]
[Singleton<HandleBoiler, IHandleHeater>]
[Singleton<HandleBoiler, IHandleAppliance>(Fallback = Fallback.Silent)]
public static partial class HandleBoilerModule;

/// <summary>The marker <see cref="HandleLampScanModule" /> scans for.</summary>
public interface IHandleLamp;

/// <summary>A user of the internal bulb and the torch that their modules register under internal services.</summary>
public interface IHandleLampUser
{
	/// <summary>The internal bulb the user received.</summary>
	object Bulb { get; }

	/// <summary>The torch the user received.</summary>
	object Torch { get; }
}

internal interface IHandleBulbSource;

internal interface IHandleTorchSource;

internal sealed class HandleBulb : IHandleLamp, IHandleBulbSource;

/// <summary>A public lamp that <see cref="HandleBulbModule" /> registers only under an internal service.</summary>
public sealed class HandleTorch : IHandleLamp, IHandleTorchSource;

internal sealed class HandleLampUser(IHandleBulbSource bulb, IHandleTorchSource torch) : IHandleLampUser
{
	public object Bulb => bulb;

	public object Torch => torch;
}

/// <summary>A <c>[Module]</c> that registers an internal bulb and a public torch only under internal services.</summary>
[Module]
[Singleton<HandleBulb, IHandleBulbSource>]
[Singleton<HandleTorch, IHandleTorchSource>]
[Singleton<HandleLampUser, IHandleLampUser>]
public static partial class HandleBulbModule;

/// <summary>A <c>[Module]</c> whose scan matches the bulb and the torch that <see cref="HandleBulbModule" /> builds through handles.</summary>
[Module]
[Scan<IHandleLamp>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
public static partial class HandleLampScanModule;

/// <summary>A <c>[Module]</c> whose scan matches the kettle and the boiler the other modules build through handles.</summary>
[Module]
[AsyncInitializer<IHandleWarmable>(nameof(IHandleWarmable.WarmAsync))]
[Scan<IHandleAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
public static partial class HandleApplianceScanModule;
