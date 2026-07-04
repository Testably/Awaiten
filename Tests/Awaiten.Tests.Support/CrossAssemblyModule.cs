namespace Awaiten.Tests.Support;

/// <summary>A service the cross-assembly module registers strongly, so a container that imports the module resolves it.</summary>
public interface ICrossAssemblyClock;

/// <summary>The module's own clock, contributed as an overridable <c>Default</c> that an importer can replace.</summary>
public sealed class SupportClock : ICrossAssemblyClock;

/// <summary>A service the module produces through a static <c>Factory</c> member, called across the assembly boundary.</summary>
public sealed class SupportGreeter
{
	/// <summary>Initializes the greeter with the greeting the module's factory hands it.</summary>
	/// <param name="greeting">The greeting the module's factory supplies.</param>
	public SupportGreeter(string greeting) => Greeting = greeting;

	/// <summary>The greeting the module's factory supplied - asserted on to prove the module method actually ran.</summary>
	public string Greeting { get; }
}

/// <summary>A service the module exposes as a pre-built <c>Instance</c> member across the assembly boundary.</summary>
public sealed class SupportCache;

/// <summary>A plain strong registration the module contributes, resolved as-is by an importer.</summary>
public sealed class SupportLogger;

/// <summary>
///     A <c>[Module]</c> compiled into this referenced support assembly, so a container in the test assembly can
///     <c>[Import]</c> it and exercise the real metadata-import path end to end at runtime: a strong registration,
///     an overridable <c>Default</c>, and <c>Factory</c>/<c>Instance</c> members the generated container must call
///     qualified with (and accessible on) this cross-assembly module type.
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
