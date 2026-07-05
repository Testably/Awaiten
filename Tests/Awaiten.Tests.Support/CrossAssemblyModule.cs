namespace Awaiten.Tests.Support;

/// <summary>A service the cross-assembly module registers strongly, so a container that imports the module resolves it.</summary>
public interface ICrossAssemblyClock;

/// <summary>The module's own clock, contributed as an overridable <c>Default</c> that an importer can replace.</summary>
public sealed class SupportClock : ICrossAssemblyClock;

/// <summary>A service the module produces through a static <c>Factory</c> member, called across the assembly boundary.</summary>
public sealed class SupportGreeter
{
	public SupportGreeter(string greeting) => Greeting = greeting;

	/// <summary>Asserted on to prove the module's factory method actually ran across the assembly boundary.</summary>
	public string Greeting { get; }
}

/// <summary>A service the module exposes as a pre-built <c>Instance</c> member across the assembly boundary.</summary>
public sealed class SupportCache;

/// <summary>A plain strong registration the module contributes, resolved as-is by an importer.</summary>
public sealed class SupportLogger;

/// <summary>
///     A <c>[Module]</c> in a referenced assembly, so an importing container exercises the real cross-assembly
///     metadata-import path: a strong registration, an overridable <c>Default</c>, and <c>Factory</c>/<c>Instance</c>
///     members the generated container must call qualified on this module type.
/// </summary>
[Module]
[Singleton<SupportLogger>]
[Singleton<SupportClock, ICrossAssemblyClock>(Default = true)]
[Singleton<SupportGreeter>(Factory = nameof(CreateGreeter))]
[Singleton<SupportCache>(Instance = nameof(Cache))]
public static class CrossAssemblySupportModule
{
	/// <summary>The pre-built cache the module exposes as an <c>Instance</c> registration.</summary>
	public static SupportCache Cache { get; } = new();

	/// <summary>Produces the greeter the module registers as a <c>Factory</c>, stamping a greeting to prove it ran.</summary>
	public static SupportGreeter CreateGreeter() => new("hello from the support module");
}
