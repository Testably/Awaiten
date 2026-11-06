using System.Runtime.CompilerServices;
using Awaiten.Tests.Support;

[assembly: InternalsVisibleTo("Awaiten.Tests")]

namespace Awaiten.Tests.ContainerSupport;

/// <summary>A volatile service the production container registers and a test container replaces.</summary>
public interface ISupportBaseClock;

/// <summary>The production clock of <see cref="SupportBaseContainer" />.</summary>
public sealed class SupportSystemClock : ISupportBaseClock;

/// <summary>A consumer of the clock, so a replacement must rewire it.</summary>
public sealed class SupportReport(ISupportBaseClock clock)
{
	/// <summary>The clock the report was built with.</summary>
	public ISupportBaseClock Clock { get; } = clock;
}

/// <summary>Marker for the production container's scan.</summary>
public interface ISupportBasePlugin;

/// <summary>A public scan match.</summary>
public sealed class SupportAlphaPlugin : ISupportBasePlugin;

/// <summary>A second public scan match.</summary>
public sealed class SupportBetaPlugin : ISupportBasePlugin;

/// <summary>An internal scan match, visible to the extending test assembly through [InternalsVisibleTo].</summary>
internal sealed class SupportInternalPlugin : ISupportBasePlugin;

/// <summary>A service the production container builds through an internal factory.</summary>
public sealed class SupportGreeting(string text)
{
	/// <summary>Asserted on to prove the internal factory of the base ran.</summary>
	public string Text { get; } = text;
}

/// <summary>A service with an internal constructor and an activation hook, both reached through [InternalsVisibleTo].</summary>
public sealed class SupportWarmup
{
	internal SupportWarmup()
	{
	}

	/// <summary>Set by the base's activation hook.</summary>
	public bool Warmed { get; internal set; }
}

/// <summary>A production container in a referenced assembly that a test container extends.</summary>
[Container]
[Import(typeof(CrossAssemblySupportModule))]
[Singleton<SupportSystemClock, ISupportBaseClock>]
[Singleton<SupportReport>]
[Singleton<SupportGreeting>(Factory = nameof(CreateGreeting))]
[Singleton<SupportWarmup>(OnActivated = nameof(Warm))]
[Scan<ISupportBasePlugin>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
public static partial class SupportBaseContainer
{
	internal static SupportGreeting CreateGreeting() => new("production");

	internal static void Warm(SupportWarmup warmup) => warmup.Warmed = true;
}
