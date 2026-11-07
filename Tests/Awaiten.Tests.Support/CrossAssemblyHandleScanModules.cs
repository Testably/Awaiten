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

/// <summary>A <c>[Module]</c> that registers an internal boiler under two public services, which one handle shares.</summary>
[Module]
[Singleton<HandleBoiler, IHandleHeater>]
[Singleton<HandleBoiler, IHandleAppliance>]
public static partial class HandleBoilerModule;

/// <summary>A <c>[Module]</c> whose scan matches the kettle and the boiler the other modules build through handles.</summary>
[Module]
[AsyncInitializer<IHandleWarmable>(nameof(IHandleWarmable.WarmAsync))]
[Scan<IHandleAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
public static partial class HandleApplianceScanModule;
