using System;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>A user of an internal heater whose async initialization fails.</summary>
public interface IFailingHeaterUser;

/// <summary>Counts what happens to the internal heater of <see cref="CrossAssemblyFailingHeaterModule" />.</summary>
public static class FailingHeaterProbe
{
	private static int _constructed;
	private static int _disposed;

	/// <summary>How often the heater was constructed.</summary>
	public static int Constructed => Volatile.Read(ref _constructed);

	/// <summary>How often the heater was disposed.</summary>
	public static int Disposed => Volatile.Read(ref _disposed);

	internal static void Construct() => Interlocked.Increment(ref _constructed);

	internal static void Dispose() => Interlocked.Increment(ref _disposed);
}

internal interface IFailingStartable
{
	Task StartAsync(CancellationToken cancellationToken);
}

internal sealed class FailingHeater : IFailingStartable, IDisposable
{
	public FailingHeater() => FailingHeaterProbe.Construct();

	public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("heater failed");

	public void Dispose() => FailingHeaterProbe.Dispose();
}

internal sealed class FailingHeaterUser(FailingHeater heater) : IFailingHeaterUser
{
	public object Heater => heater;
}

/// <summary>A <c>[Module]</c> whose internal transient heater fails its async initialization.</summary>
[Module]
[AsyncInitializer<IFailingStartable>(nameof(IFailingStartable.StartAsync))]
[Transient<FailingHeater>]
[Transient<FailingHeaterUser, IFailingHeaterUser>]
public static partial class CrossAssemblyFailingHeaterModule;

/// <summary>A flavor a module registers under a key beside an internal service.</summary>
public interface IKeyedFlavor;

/// <summary>A user of the internal service of the keyed flavor.</summary>
public interface IKeyedFlavorUser
{
	/// <summary>The flavor the user received through the internal service.</summary>
	object Flavor { get; }
}

internal interface IKeyedFlavorSource;

internal sealed class KeyedFlavor : IKeyedFlavor, IKeyedFlavorSource;

internal sealed class KeyedFlavorUser(IKeyedFlavorSource source) : IKeyedFlavorUser
{
	public object Flavor => source;
}

/// <summary>A <c>[Module]</c> that registers an internal implementation under a key and under an internal service.</summary>
[Module]
[Singleton<KeyedFlavor, IKeyedFlavor>(Key = "vanilla")]
[Singleton<KeyedFlavor, IKeyedFlavorSource>]
[Singleton<KeyedFlavorUser, IKeyedFlavorUser>]
public static partial class CrossAssemblyKeyedFlavorModule;

/// <summary>A user of internal services built with the token of the resolution.</summary>
public interface ITokenUser
{
	/// <summary>The token the internal service's initializer received.</summary>
	CancellationToken InitializerToken { get; }

	/// <summary>The token the async factory of the other internal service received.</summary>
	CancellationToken FactoryToken { get; }
}

internal interface ITokenStartable
{
	Task StartAsync(CancellationToken cancellationToken);
}

internal sealed class TokenStarted : ITokenStartable
{
	public CancellationToken Token { get; private set; }

	public Task StartAsync(CancellationToken cancellationToken)
	{
		Token = cancellationToken;
		return Task.CompletedTask;
	}
}

internal sealed class TokenBuilt(CancellationToken token)
{
	public CancellationToken Token => token;
}

internal sealed class TokenUser(TokenStarted started, TokenBuilt built) : ITokenUser
{
	public CancellationToken InitializerToken => started.Token;

	public CancellationToken FactoryToken => built.Token;
}

/// <summary>A <c>[Module]</c> whose internal services take the token through an initializer and an async factory.</summary>
[Module]
[AsyncInitializer<ITokenStartable>(nameof(ITokenStartable.StartAsync))]
[Transient<TokenStarted>]
[Transient<TokenBuilt>(Factory = nameof(BuildAsync))]
[Transient<TokenUser, ITokenUser>]
public static partial class CrossAssemblyTokenModule
{
	internal static async Task<TokenBuilt> BuildAsync(CancellationToken cancellationToken)
	{
		await Task.Yield();
		return new TokenBuilt(cancellationToken);
	}
}

/// <summary>A user of an internal service whose factory returns an instance that only disposes asynchronously.</summary>
public interface IAsyncValveUser
{
	/// <summary>Whether the container disposed the valve.</summary>
	bool ValveDisposed { get; }
}

internal class Valve;

internal sealed class AsyncValve : Valve, IAsyncDisposable
{
	public bool Disposed { get; private set; }

	public ValueTask DisposeAsync()
	{
		Disposed = true;
		return default;
	}
}

internal sealed class AsyncValveUser(Valve valve) : IAsyncValveUser
{
	public bool ValveDisposed => ((AsyncValve)valve).Disposed;
}

/// <summary>A <c>[Module]</c> whose factory returns an async-only disposable behind a type that does not show it.</summary>
[Module]
[Singleton<Valve>(Factory = nameof(CreateValve))]
[Singleton<AsyncValveUser, IAsyncValveUser>]
public static partial class CrossAssemblyAsyncValveModule
{
	internal static Valve CreateValve() => new AsyncValve();
}
