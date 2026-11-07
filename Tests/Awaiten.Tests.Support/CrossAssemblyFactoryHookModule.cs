using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>A heater the module builds through a public factory, preheats in an internal hook and starts by its convention.</summary>
public sealed class FactoryHookedHeater : ISelfCompiledStartable
{
	internal FactoryHookedHeater()
	{
	}

	/// <summary>The steps that ran on the heater, in order.</summary>
	public List<string> Steps { get; } = new();

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		Steps.Add("convention");
		return Task.CompletedTask;
	}
}

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
[AsyncInitializer<ISelfCompiledStartable>(nameof(ISelfCompiledStartable.StartAsync))]
[Singleton<IFactoryHookedLamp>(Factory = nameof(CreateLamp), OnActivated = nameof(Light))]
[Singleton<IFactoryHookedRouter>(Factory = nameof(CreateRouter), OnActivated = nameof(ConnectAsync))]
[Singleton<FactoryHookedHeater>(Factory = nameof(CreateHeater), OnActivated = nameof(Preheat))]
public static partial class FactoryHookModule
{
	/// <summary>Creates the heater.</summary>
	public static FactoryHookedHeater CreateHeater() => new();

	internal static void Preheat(FactoryHookedHeater heater) => heater.Steps.Add("hook");

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
