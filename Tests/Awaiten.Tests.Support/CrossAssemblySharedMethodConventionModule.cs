using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>A convention type of <see cref="SharedMethodConventionModule" />.</summary>
public interface ISharedMethodStartable
{
	/// <summary>Starts the instance.</summary>
	/// <param name="cancellationToken">The token of the resolve that builds the instance.</param>
	Task StartAsync(CancellationToken cancellationToken);
}

/// <summary>A second convention type of <see cref="SharedMethodConventionModule" />, with a method of the same name.</summary>
public interface ISharedMethodPowered
{
	/// <summary>Starts the instance.</summary>
	/// <param name="cancellationToken">The token of the resolve that builds the instance.</param>
	Task StartAsync(CancellationToken cancellationToken);

	/// <summary>Calibrates the instance.</summary>
	/// <param name="cancellationToken">The token of the resolve that builds the instance.</param>
	Task CalibrateAsync(CancellationToken cancellationToken);
}

/// <summary>The marker <see cref="SharedMethodConventionModule" /> scans for.</summary>
public interface ISharedMethodPlugin;

/// <summary>A device that counts how often each convention method ran on it.</summary>
public interface ISharedMethodDevice
{
	/// <summary>How often <c>StartAsync</c> ran.</summary>
	int Starts { get; }

	/// <summary>How often <c>CalibrateAsync</c> ran.</summary>
	int Calibrations { get; }
}

internal sealed class SharedMethodDevice : ISharedMethodPlugin, ISharedMethodDevice, ISharedMethodStartable, ISharedMethodPowered
{
	public int Starts { get; private set; }

	public int Calibrations { get; private set; }

	public Task StartAsync(CancellationToken cancellationToken)
	{
		Starts++;
		return Task.CompletedTask;
	}

	public Task CalibrateAsync(CancellationToken cancellationToken)
	{
		Calibrations++;
		return Task.CompletedTask;
	}
}

/// <summary>
///     A <c>[Module]</c> whose scan matches an internal device that one method initializes for two of its conventions,
///     while a third convention of the second type runs another method.
/// </summary>
[Module]
[AsyncInitializer<ISharedMethodStartable>(nameof(ISharedMethodStartable.StartAsync))]
[AsyncInitializer<ISharedMethodPowered>(nameof(ISharedMethodPowered.StartAsync))]
[AsyncInitializer<ISharedMethodPowered>(nameof(ISharedMethodPowered.CalibrateAsync))]
[Scan<ISharedMethodPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton)]
public static partial class SharedMethodConventionModule;
