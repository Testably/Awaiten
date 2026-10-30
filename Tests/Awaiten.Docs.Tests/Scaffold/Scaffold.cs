// The usings of a project with implicit usings and the `using Awaiten;` from "Getting started". Every other
// namespace has to be shown on the page, as a reader could not guess it.
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
global using Awaiten;
global using CoffeeShop;
global using Scaffold;
global using static Scaffold.Prelude;
global using Buffer = Scaffold.Buffer;

namespace Scaffold;

// The variables and helpers that the statements of a page use without declaring them.
public static class Prelude
{
	public static IAwaitenRoot shop = default!;
	public static IAwaitenRoot root = default!;
	public static CancellationToken cancellationToken;
	public static CancellationToken ct;
	public static Receipt receipt = default!;

	public static Task HeatBoilerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// The coffee-shop types that the pages reuse without declaring them. Every registrable type has a constructor
// without dependencies, so that any registration of it resolves, and the interface members have a default
// implementation, so that a page can implement an interface with an empty class.
public interface IMachine;
public interface IWarmable;

public sealed class EspressoMachine : IMachine, IWarmable
{
	public Cup Pull() => new();
	public void Calibrate() { }
	public void Calibrate(Settings settings) { }
	public void Purge() { }
}

public sealed class Grinder;
public sealed class Cup;
public sealed class Order;
public sealed class Customer;
public sealed class Till;
public sealed class Tab;
public sealed class Menu;

public sealed class MenuBoard
{
	public string Special { get; } = "";
	public static MenuBoard Load() => new();
	public static MenuBoard LoadFromDisk() => new();
}

public sealed class Settings
{
	public bool PreferFilter { get; init; }
}

public sealed class BrewSession : IDisposable
{
	public void Brew() { }
	public void Dispose() { }
}

public sealed class Valve : IAsyncInitializable, IAsyncDisposable
{
	public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public ValueTask DisposeAsync() => default;
}

public interface IBrewer;
public sealed class EspressoBrewer : IBrewer;
public sealed class FilterBrewer : IBrewer;

public enum MilkKind { Oat, Whole, Soy }
public interface IMilk;
public sealed class OatMilk : IMilk;
public sealed class WholeMilk : IMilk;
public sealed class SoyMilk : IMilk;

public interface ISyrup;
public sealed class VanillaSyrup : ISyrup;
public sealed class CaramelSyrup : ISyrup;
public sealed class HazelnutSyrup : ISyrup;

public interface IDrink;
public sealed class Espresso : IDrink;
public sealed class Cappuccino : IDrink;

public static class DrinkExtensions
{
	public static void Prime(this IDrink drink) { }
}

public interface IClock;
public sealed class Clock : IClock;

public interface ITimeSystem;
public sealed class RealTimeSystem : ITimeSystem;
public sealed class MockTimeSystem : ITimeSystem;

public sealed class StripeGateway : IPaymentGateway
{
	public Receipt Charge(decimal amount) => new(amount);
}

public interface IPaymentTerminal
{
	Task ChargeAsync(decimal amount) => Task.CompletedTask;
}

public sealed class PaymentTerminal : IPaymentTerminal
{
	public Task HandshakeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class CardTerminal : IPaymentTerminal;
public sealed class CashTerminal : IPaymentTerminal;
public sealed class SpareTerminal : IPaymentTerminal;
public sealed class LoggingTerminal(IPaymentTerminal inner) : IPaymentTerminal;
public sealed class RetryingTerminal(IPaymentTerminal inner) : IPaymentTerminal;

public interface IChannel;

public interface IReceiptChannel : IChannel
{
	Task SendAsync(Receipt receipt) => Task.CompletedTask;
}

public sealed class PrinterChannel : IReceiptChannel;
public sealed class EmailChannel : IReceiptChannel;
public sealed class SmsChannel : IReceiptChannel;
public sealed class KitchenDisplayChannel : IReceiptChannel;
public sealed class CompositeReceiptChannel(IEnumerable<IReceiptChannel> channels) : IReceiptChannel;
public sealed class BackupReceiptChannel(IEnumerable<IReceiptChannel> channels) : IReceiptChannel;

public interface IReceiptPrinter;
public sealed class ReceiptPrinter : IReceiptPrinter;
public sealed class ThermalPrinter : IReceiptPrinter;
public sealed class WidePrinter : IReceiptPrinter;
public sealed class DriveThroughRegister(IReceiptPrinter printer);

public interface IReader;
public interface IWriter;
public sealed class Ledger : IReader, IWriter;

public interface ILogger
{
	void Log(string message) { }
}

public sealed class Prefix;

public sealed class Logger : ILogger
{
	public Logger(string name, Prefix? prefix = null) { }
	public Logger(Type consumer, string name) { }
}

public static class BrewerLibrary
{
	public static IBrewer Create() => new EspressoBrewer();
}

public abstract class DomainEvent;
public sealed class OrderPlaced : DomainEvent;
public sealed class OrderPaid : DomainEvent;

public sealed class DbConnection;

public interface IRepository<T>;

public sealed class Repository<T> : IRepository<T>
{
	public Repository() { }
	public Repository(DbConnection connection) { }
}

public sealed class FastRepository<T> : IRepository<T>;

public interface IRepository<TKey, TValue>;
public interface IMap<TKey, TValue>;

public interface IHandler;

public interface IHandler<T>
{
	Task HandleAsync(T request) => Task.CompletedTask;
}

public sealed class Handler<T> : IHandler<T>;
public sealed class EmailHandler<T> : IHandler<T>;
public sealed class SmsHandler<T> : IHandler<T>;
public sealed class LoggingBehavior<T>(IHandler<T> inner) : IHandler<T>;
public sealed class CompositeHandler<T>(IEnumerable<IHandler<T>> inner) : IHandler<T>;

public interface IConnection<T>;
public sealed class Connection<T> : IConnection<T>;

public interface ICache;
public sealed class MemoryCache : ICache;
public interface ICache<T>;
public sealed class MemoryCache<T> : ICache<T>;
public sealed class Cache<T> : ICache<T>;
public sealed class OrderCache : ICache<Order>;

public sealed class BufferPool
{
	public Buffer Rent() => new();
	public void Return(Buffer buffer) { }
}

// Named like System.Buffer, which the implicit usings import too, so an alias picks this one.
public sealed class Buffer;

public sealed class CupPool
{
	public Cup Rent() => new();
	public void Return(Cup cup) { }
}

public sealed class Store;
public sealed class PlaceOrder;
public sealed class Refund;
public sealed class Orders;
public sealed class Payments;

// The markers of the scan diagnostics, which the pages implement themselves.
public interface IPlugin;
public interface IRoaster;
public interface IGrinder;
public interface IEquipment;
public sealed class RenamedPlugin;
public static class OtherLibMarker;

[Module]
[Singleton<Grinder>]
public static class BaseModule;

public interface IServiceLocator
{
	object? Resolve(Type? type);
}
