namespace Awaiten.Tests.Support;

/// <summary>A kettle the module registers twice, through a public factory and self-compiled, with one internal hook.</summary>
public sealed class SharedHookKettle
{
	internal SharedHookKettle()
	{
	}

	/// <summary>How often the module's activation hook ran on this kettle.</summary>
	public int Boiled { get; internal set; }
}

/// <summary>
///     A <c>[Module]</c> whose <c>Factory</c> registration and self-compiled registration of the same type name the same
///     <c>internal</c> hook, so both share one generated wrapper.
/// </summary>
[Module]
[Singleton<SharedHookKettle>(Key = "made", Factory = nameof(CreateKettle), OnActivated = nameof(Boil))]
[Singleton<SharedHookKettle>(Key = "built", OnActivated = nameof(Boil))]
public static partial class SharedHookModule
{
	/// <summary>Creates the kettle.</summary>
	public static SharedHookKettle CreateKettle() => new();

	internal static void Boil(SharedHookKettle kettle) => kettle.Boiled++;
}
