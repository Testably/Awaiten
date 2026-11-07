namespace Awaiten.Tests.ContainerSupport;

/// <summary>A service whose implementation takes an internal dependency.</summary>
public interface IFriendGreeter
{
	/// <summary>The internal dependency the greeter was constructed with.</summary>
	object Formatter { get; }
}

/// <summary>The first service of an internal implementation registered under two services.</summary>
public interface IFriendFront;

/// <summary>The second service of an internal implementation registered under two services.</summary>
public interface IFriendBack;

/// <summary>A clock registered under a key.</summary>
public interface IFriendClock;

/// <summary>A service whose implementation takes a keyed dependency.</summary>
public interface IFriendReport
{
	/// <summary>The keyed clock the report was constructed with.</summary>
	IFriendClock Clock { get; }
}

/// <summary>The clock registered under the key "utc".</summary>
public sealed class FriendUtcClock : IFriendClock;

internal interface IFriendFormatter;

internal sealed class FriendFormatter : IFriendFormatter;

internal sealed class FriendGreeter(IFriendFormatter formatter) : IFriendGreeter
{
	public object Formatter => formatter;
}

internal sealed class FriendShared : IFriendFront, IFriendBack;

internal sealed class FriendReport([FromKey("utc")] IFriendClock clock) : IFriendReport
{
	public IFriendClock Clock => clock;
}

/// <summary>
///     A <c>[Module]</c> whose registrations its generated factories cannot support. This assembly grants
///     Awaiten.Tests <c>InternalsVisibleTo</c>, so the module builds without factories for them and a container there
///     constructs the implementations itself.
/// </summary>
[Module]
[Singleton<FriendFormatter, IFriendFormatter>]
[Singleton<FriendGreeter, IFriendGreeter>]
[Singleton<FriendShared, IFriendFront>]
[Singleton<FriendShared, IFriendBack>]
[Singleton<FriendUtcClock, IFriendClock>(Key = "utc")]
[Singleton<FriendReport, IFriendReport>]
public static partial class SupportFriendModule;
