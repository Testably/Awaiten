using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>A lamp the module builds through a public factory and lights in an internal activation hook.</summary>
public interface IFactoryHookedLamp
{
	/// <summary>The clock the activation hook received, or <see langword="null" /> before it ran.</summary>
	IShapeClock? Clock { get; }
}

/// <summary>A router the module builds through a public factory and connects in an internal asynchronous activation hook.</summary>
public interface IFactoryHookedRouter
{
	/// <summary>The token the activation hook received, or <see langword="null" /> before it ran.</summary>
	CancellationToken? Token { get; }
}

internal sealed class FactoryHookedLamp : IFactoryHookedLamp
{
	public IShapeClock? Clock { get; set; }
}

internal sealed class FactoryHookedRouter : IFactoryHookedRouter
{
	public CancellationToken? Token { get; set; }
}

/// <summary>
///     A <c>[Module]</c> whose <c>Factory</c> registrations name <c>internal</c> hooks, so an importing container in
///     another assembly without <c>InternalsVisibleTo</c> runs them through the public wrappers the module generates.
/// </summary>
[Module]
[Singleton<IFactoryHookedLamp>(Factory = nameof(CreateLamp), OnActivated = nameof(Light))]
[Singleton<IFactoryHookedRouter>(Factory = nameof(CreateRouter), OnActivated = nameof(ConnectAsync))]
public static partial class FactoryHookModule
{
	/// <summary>Creates the lamp.</summary>
	public static IFactoryHookedLamp CreateLamp() => new FactoryHookedLamp();

	/// <summary>Creates the router.</summary>
	public static IFactoryHookedRouter CreateRouter() => new FactoryHookedRouter();

	internal static void Light(IFactoryHookedLamp lamp, IShapeClock clock) => ((FactoryHookedLamp)lamp).Clock = clock;

	internal static async Task ConnectAsync(IFactoryHookedRouter router, CancellationToken cancellationToken)
	{
		await Task.Yield();
		((FactoryHookedRouter)router).Token = cancellationToken;
	}
}
