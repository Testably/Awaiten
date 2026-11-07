namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     <c>[AsyncInitializer&lt;T&gt;(method)]</c> conventions: a container or module declares which domain type and
///     method initialize a service, so the domain type never references Awaiten. A matching service is async-initialized
///     like one with an asynchronous <c>OnActivated</c> hook.
/// </summary>
public class AsyncInitializerConventionTests
{
	[Test]
	public async Task AnInterfaceConvention_AwaitsTheMethodThroughTheInterface()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IStartable { Task StartAsync(CancellationToken cancellationToken); }
		                                       public sealed class PaymentTerminal : IStartable
		                                       {
		                                           Task IStartable.StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }

		                                       [Container]
		                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
		                                       [Singleton<PaymentTerminal>]
		                                       public static partial class MyContainer;
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("an explicit interface implementation is reachable through the convention type");
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"])
			.Contains("await ((global::MyCode.IStartable)created).StartAsync(cancellationToken).ConfigureAwait(false);")
			.Because("the container calls the method through the convention type and passes the resolve-time token");
	}

	[Test]
	public async Task AMatchingService_IsAsyncInitialized()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IStartable { Task StartAsync(CancellationToken cancellationToken); }
		                                       public sealed class PaymentTerminal : IStartable
		                                       {
		                                           public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Till { public Till(Func<PaymentTerminal> terminal) { } }

		                                       [Container]
		                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
		                                       [Singleton<PaymentTerminal>]
		                                       [Singleton<Till>]
		                                       public static partial class MyContainer;
		                                       """);

		await That(result.Diagnostics).Contains("*AWT119*PaymentTerminal*").AsWildcard()
			.Because("a service a convention initializes is an async source, so a synchronous Func over it is rejected");
	}

	[Test]
	public async Task ABaseClassConvention_CallsTheOverride()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public abstract class Machine { public virtual Task WarmUpAsync() => Task.CompletedTask; }
		                                       public sealed class Grinder : Machine { public override Task WarmUpAsync() => Task.CompletedTask; }

		                                       [Container]
		                                       [AsyncInitializer<Machine>(nameof(Machine.WarmUpAsync))]
		                                       [Singleton<Grinder>]
		                                       public static partial class MyContainer;
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"])
			.Contains("await ((global::MyCode.Machine)created).WarmUpAsync().ConfigureAwait(false);")
			.Because("a base class convention is called through the base class, so the override runs");
	}

	[Test]
	public async Task AValueTaskConvention_IsAwaited()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IWarmable { ValueTask WarmAsync(); }
		                                       public sealed class Grinder : IWarmable { public ValueTask WarmAsync() => default; }

		                                       [Container]
		                                       [AsyncInitializer<IWarmable>(nameof(IWarmable.WarmAsync))]
		                                       [Singleton<Grinder>]
		                                       public static partial class MyContainer;
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"])
			.Contains("await ((global::MyCode.IWarmable)created).WarmAsync().ConfigureAwait(false);");
	}

	[Test]
	public async Task SeveralConventions_RunInDeclarationOrder_AfterTheActivationHook()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IConnectable { Task ConnectAsync(); }
		                                       public interface ICalibratable { Task CalibrateAsync(); }
		                                       public sealed class Grinder : ICalibratable, IConnectable
		                                       {
		                                           public Task ConnectAsync() => Task.CompletedTask;
		                                           public Task CalibrateAsync() => Task.CompletedTask;
		                                       }

		                                       [Container]
		                                       [AsyncInitializer<IConnectable>(nameof(IConnectable.ConnectAsync))]
		                                       [AsyncInitializer<ICalibratable>(nameof(ICalibratable.CalibrateAsync))]
		                                       [Singleton<Grinder>(OnActivated = nameof(Configure))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task Configure(Grinder grinder) => Task.CompletedTask;
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		int hook = source.IndexOf("await Configure(created)", StringComparison.Ordinal);
		int connect = source.IndexOf("ConnectAsync()", StringComparison.Ordinal);
		int calibrate = source.IndexOf("CalibrateAsync()", StringComparison.Ordinal);
		await That(hook).IsGreaterThan(-1).Because("the asynchronous hook is awaited");
		await That(connect).IsGreaterThan(hook).Because("the OnActivated hook runs before every convention");
		await That(calibrate).IsGreaterThan(connect).Because("the conventions run in the order the container declares them, not in the order the type implements them");
	}

	[Test]
	public async Task AMethodReachableThroughSeveralConventions_RunsOnce()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IStartable { Task StartAsync(); }
		                                       public interface IMachine : IStartable { }
		                                       public sealed class Grinder : IMachine { public Task StartAsync() => Task.CompletedTask; }

		                                       [Container]
		                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
		                                       [AsyncInitializer<IMachine>(nameof(IMachine.StartAsync))]
		                                       [Singleton<Grinder>]
		                                       public static partial class MyContainer;
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a convention type finds the method on an interface it extends");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("StartAsync()").Exactly(1)
			.Because("both conventions reach IStartable.StartAsync, which runs at most once per instance");
	}

	[Test]
	public async Task TwoInterfaceMethodsImplementedByOneMethod_RunItOnce_AtTheFirstConventionsPosition()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IStartable { Task StartAsync(); }
		                                       public interface ICalibratable { Task CalibrateAsync(); }
		                                       public interface IPowered { Task StartAsync(); }
		                                       public sealed class Grinder : IStartable, ICalibratable, IPowered
		                                       {
		                                       	public Task StartAsync() => Task.CompletedTask;
		                                       	public Task CalibrateAsync() => Task.CompletedTask;
		                                       }

		                                       [Container]
		                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
		                                       [AsyncInitializer<ICalibratable>(nameof(ICalibratable.CalibrateAsync))]
		                                       [AsyncInitializer<IPowered>(nameof(IPowered.StartAsync))]
		                                       [Singleton<Grinder>]
		                                       public static partial class MyContainer;
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("StartAsync()").Exactly(1)
			.Because("Grinder.StartAsync implements both IStartable.StartAsync and IPowered.StartAsync, and runs at most once per instance");
		await That(source.IndexOf("((global::MyCode.IStartable)", StringComparison.Ordinal)).IsLessThan(source.IndexOf("CalibrateAsync()", StringComparison.Ordinal))
			.Because("the method runs at the position of the first convention that reaches it");
	}

	[Test]
	public async Task ABaseClassMethodAndAnInterfaceMethodImplementedByOneOverride_RunItOnce()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public abstract class Appliance { public virtual Task WarmUpAsync() => Task.CompletedTask; }
		                                       public interface IWarmable { Task WarmUpAsync(); }
		                                       public sealed class Oven : Appliance, IWarmable { public override Task WarmUpAsync() => Task.CompletedTask; }

		                                       [Container]
		                                       [AsyncInitializer<Appliance>(nameof(Appliance.WarmUpAsync))]
		                                       [AsyncInitializer<IWarmable>(nameof(IWarmable.WarmUpAsync))]
		                                       [Singleton<Oven>]
		                                       public static partial class MyContainer;
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("WarmUpAsync()").Exactly(1)
			.Because("the override of Appliance.WarmUpAsync also implements IWarmable.WarmUpAsync, so it runs once");
	}

	[Test]
	public async Task ExplicitImplementationsOfTwoInterfaceMethods_BothRun()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IStartable { Task StartAsync(); }
		                                       public interface IPowered { Task StartAsync(); }
		                                       public sealed class Grinder : IStartable, IPowered
		                                       {
		                                       	Task IStartable.StartAsync() => Task.CompletedTask;
		                                       	Task IPowered.StartAsync() => Task.CompletedTask;
		                                       }

		                                       [Container]
		                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
		                                       [AsyncInitializer<IPowered>(nameof(IPowered.StartAsync))]
		                                       [Singleton<Grinder>]
		                                       public static partial class MyContainer;
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("StartAsync()").Exactly(2)
			.Because("each explicit implementation is a method of its own");
	}

	[Test]
	public async Task ANonMatchingService_IsNotInitialized()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IStartable { Task StartAsync(); }
		                                       public sealed class Grinder { }

		                                       [Container]
		                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
		                                       [Singleton<Grinder>]
		                                       public static partial class MyContainer;
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("StartAsync")
			.Because("a convention runs only on the services whose type it matches");
	}

	[Test]
	public async Task AFactoryRegistration_IsMatchedByItsDeclaredReturnType()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IStartable { Task StartAsync(); }
		                                       public sealed class Grinder : IStartable { public Task StartAsync() => Task.CompletedTask; }

		                                       [Container]
		                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
		                                       [Singleton<Grinder>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Grinder Create() => new();
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("((global::MyCode.IStartable)created).StartAsync()")
			.Because("the convention matches what the factory declares it returns");
	}

	public class Parameters
	{
		[Test]
		public async Task AreResolvedFromTheGraph_AndTheTokenIsTheResolveTimeToken()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public interface IStartable { Task StartAsync(Clock clock, CancellationToken cancellationToken); }
			                                       public sealed class Grinder : IStartable
			                                       {
			                                           public Task StartAsync(Clock clock, CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			                                       [Singleton<Clock>]
			                                       [Singleton<Grinder>]
			                                       public static partial class MyContainer;
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("((global::MyCode.IStartable)created).StartAsync(*, cancellationToken)").AsWildcard()
				.Because("the clock resolves from the graph and the token receives the token of the asynchronous resolve");
		}

		[Test]
		public async Task AMissingParameter_IsReported()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public interface IStartable { Task StartAsync(Clock clock); }
			                                       public sealed class Grinder : IStartable { public Task StartAsync(Clock clock) => Task.CompletedTask; }

			                                       [Container]
			                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			                                       [Singleton<Grinder>]
			                                       public static partial class MyContainer;
			                                       """);

			await That(result.Diagnostics).Contains("*AWT101*Clock*").AsWildcard()
				.Because("a convention's parameter is a dependency of the service, checked like a constructor parameter");
		}

		[Test]
		public async Task ACaptiveParameter_IsReported()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Order { }
			                                       public interface IStartable { Task StartAsync(Order order); }
			                                       public sealed class Grinder : IStartable { public Task StartAsync(Order order) => Task.CompletedTask; }

			                                       [Container]
			                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			                                       [Scoped<Order>]
			                                       [Singleton<Grinder>]
			                                       public static partial class MyContainer;
			                                       """);

			await That(result.Diagnostics).Contains("*AWT105*Grinder*Order*").AsWildcard()
				.Because("a singleton whose convention takes a scoped service would capture it");
		}

		[Test]
		public async Task ACycleThroughAParameter_IsReported()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Menu { public Menu(Grinder grinder) { } }
			                                       public interface IStartable { Task StartAsync(Menu menu); }
			                                       public sealed class Grinder : IStartable { public Task StartAsync(Menu menu) => Task.CompletedTask; }

			                                       [Container]
			                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			                                       [Singleton<Menu>]
			                                       [Singleton<Grinder>]
			                                       public static partial class MyContainer;
			                                       """);

			await That(result.Diagnostics).Contains("*AWT102*").AsWildcard()
				.Because("the convention resolves Menu while building Grinder, which Menu needs itself");
		}

		[Test]
		public async Task ATaintedParameter_TaintsOnlyThroughTheService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Clock { }
			                                       public interface IStartable { Task StartAsync(Func<Clock> clock); }
			                                       public sealed class Grinder : IStartable { public Task StartAsync(Func<Clock> clock) => Task.CompletedTask; }

			                                       [Container]
			                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			                                       [Singleton<Clock>]
			                                       [Singleton<Grinder>]
			                                       public static partial class MyContainer;
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a convention takes the relationship types a hook allows");
		}
	}

	public class Scope
	{
		[Test]
		public async Task AContainerConvention_DoesNotApplyToAModuleRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IStartable { Task StartAsync(); }
			                                       public sealed class Grinder : IStartable { public Task StartAsync() => Task.CompletedTask; }

			                                       [Module]
			                                       [Singleton<Grinder>]
			                                       public static partial class GrinderModule;

			                                       [Container]
			                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			                                       [Import(typeof(GrinderModule))]
			                                       public static partial class MyContainer;
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("StartAsync")
				.Because("a registration is judged by the conventions of the module that declares it, and the module declares none");
		}

		[Test]
		public async Task AModuleConvention_AppliesToItsOwnRegistrations()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IStartable { Task StartAsync(); }
			                                       public sealed class Grinder : IStartable { public Task StartAsync() => Task.CompletedTask; }
			                                       public sealed class Kettle : IStartable { public Task StartAsync() => Task.CompletedTask; }

			                                       [Module]
			                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			                                       [Singleton<Grinder>]
			                                       public static partial class GrinderModule;

			                                       [Container]
			                                       [Import(typeof(GrinderModule))]
			                                       [Singleton<Kettle>]
			                                       public static partial class MyContainer;
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("StartAsync()").Exactly(1)
				.Because("the module's convention initializes the module's grinder but not the container's own kettle");
		}

		[Test]
		public async Task AModuleDecorator_IsJudgedByTheModulesConventions()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IStartable { Task StartAsync(); }
			                                       public interface ICalibratable { Task CalibrateAsync(); }
			                                       public interface IGreeter { }
			                                       public sealed class Greeter : IGreeter { }
			                                       public sealed class LoudGreeter : IGreeter, IStartable, ICalibratable
			                                       {
			                                           public LoudGreeter(IGreeter inner) { }
			                                           public Task StartAsync() => Task.CompletedTask;
			                                           public Task CalibrateAsync() => Task.CompletedTask;
			                                       }

			                                       [Module]
			                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			                                       [Decorate<LoudGreeter, IGreeter>]
			                                       public static partial class GreetingModule;

			                                       [Container]
			                                       [AsyncInitializer<ICalibratable>(nameof(ICalibratable.CalibrateAsync))]
			                                       [Import(typeof(GreetingModule))]
			                                       [Singleton<Greeter, IGreeter>]
			                                       public static partial class MyContainer;
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("StartAsync()")
				.Because("the module declares the decorator, so the module's convention judges it");
			await That(source).DoesNotContain("CalibrateAsync()")
				.Because("a container's conventions do not reach a decorator a module declares");
		}

		[Test]
		public async Task AnExtendingContainer_InheritsTheBaseConventions()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IStartable { Task StartAsync(); }
			                                       public interface ICalibratable { Task CalibrateAsync(); }
			                                       public sealed class Grinder : IStartable, ICalibratable
			                                       {
			                                           public Task StartAsync() => Task.CompletedTask;
			                                           public Task CalibrateAsync() => Task.CompletedTask;
			                                       }
			                                       public sealed class Kettle : IStartable, ICalibratable
			                                       {
			                                           public Task StartAsync() => Task.CompletedTask;
			                                           public Task CalibrateAsync() => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			                                       [Singleton<Grinder>]
			                                       public static partial class BaseShop;

			                                       [Container(Extends = typeof(BaseShop))]
			                                       [AsyncInitializer<ICalibratable>(nameof(ICalibratable.CalibrateAsync))]
			                                       [Singleton<Kettle>]
			                                       public static partial class Shop;
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.Shop.g.cs"];
			await That(source).Contains("StartAsync()").Exactly(2)
				.Because("the base's convention judges the base's grinder and, inherited, the extending container's kettle");
			await That(source).Contains("CalibrateAsync()").Exactly(1)
				.Because("the extending container's own convention judges its own kettle but not the grinder the base declares");
			await That(source.IndexOf("StartAsync()", source.IndexOf("Kettle created", StringComparison.Ordinal), StringComparison.Ordinal))
				.IsLessThan(source.IndexOf("CalibrateAsync()", StringComparison.Ordinal))
				.Because("the inherited conventions run before the extending container's own");
		}
	}

	public class CrossAssembly
	{
		private const string Library = """
		                               using Awaiten;
		                               using System.Threading;
		                               using System.Threading.Tasks;

		                               namespace Lib;

		                               public interface IStartable { Task StartAsync(CancellationToken cancellationToken); }
		                               public interface ITerminal { }
		                               internal sealed class Terminal : ITerminal, IStartable
		                               {
		                                   public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                               }
		                               public interface IPlugin { }
		                               public interface IPrinter { }
		                               internal sealed class Printer : IPlugin, IPrinter, IStartable
		                               {
		                                   public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                               }

		                               [Module]
		                               [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
		                               [Singleton<Terminal, ITerminal>]
		                               [Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton)]
		                               public static partial class TerminalModule;
		                               """;

		[Test]
		public async Task AModuleConvention_InitializesItsSelfCompiledRegistrationsAndScanMatches()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(TerminalModule))]
				public static partial class MyContainer;
				""");

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("await ((global::Lib.IStartable)created).StartAsync(cancellationToken).ConfigureAwait(false);").Exactly(2)
				.Because("the consumer reads the module's convention off its metadata and runs it on the internal terminal and the scanned printer it reaches through their generated factories");
		}

		[Test]
		public async Task TheModuleRecordsWhichConventionsAScanMatchHas()
		{
			GeneratorResult result = Generator.Run(Library);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.ModuleScan.Lib.TerminalModule.g.cs"])
				.Contains("AsyncInitializers = new global::System.Type[] { typeof(global::Lib.IStartable) }")
				.Because("a consumer cannot see the internal match, so the module records the conventions it matches");
		}

		[Test]
		public async Task AMethodTwoConventionsReach_RunsOnceBehindAGeneratedFactory()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;
				using System.Threading.Tasks;

				namespace Lib;

				public interface IStartable { Task StartAsync(); }
				public interface IPowered { Task StartAsync(); Task CalibrateAsync(); }
				public interface ITerminal { }
				internal sealed class Terminal : ITerminal, IStartable, IPowered
				{
				    public Task StartAsync() => Task.CompletedTask;
				    public Task CalibrateAsync() => Task.CompletedTask;
				}
				public interface IPlugin { }
				public interface IPrinter { }
				internal sealed class Printer : IPlugin, IPrinter, IStartable, IPowered
				{
				    public Task StartAsync() => Task.CompletedTask;
				    public Task CalibrateAsync() => Task.CompletedTask;
				}

				[Module]
				[AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
				[AsyncInitializer<IPowered>(nameof(IPowered.StartAsync))]
				[AsyncInitializer<IPowered>(nameof(IPowered.CalibrateAsync))]
				[Singleton<Terminal, ITerminal>]
				[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton)]
				public static partial class TerminalModule;
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(TerminalModule))]
				public static partial class MyContainer;
				""");

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("StartAsync()").Exactly(2)
				.Because("one method implements IStartable.StartAsync and IPowered.StartAsync, so it runs once on the terminal and once on the printer, although IPowered also brings CalibrateAsync");
			await That(source).Contains("((global::Lib.IPowered)created).CalibrateAsync()").Exactly(2);
		}

		[Test]
		public async Task AModuleConvention_InitializesADecoratorAndACompositeBuiltThroughGeneratedFactories()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;
				using System.Collections.Generic;
				using System.Threading.Tasks;

				namespace Lib;

				public interface IStartable { Task StartAsync(); }
				public interface IGreeter { }
				internal sealed class Greeter : IGreeter { }
				internal sealed class LoudGreeter(IGreeter inner) : IGreeter, IStartable { public Task StartAsync() => Task.CompletedTask; }
				public interface IChannel { }
				internal sealed class Printer : IChannel { }
				internal sealed class AllChannels(IEnumerable<IChannel> channels) : IChannel, IStartable { public Task StartAsync() => Task.CompletedTask; }

				[Module]
				[AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
				[Singleton<Greeter, IGreeter>]
				[Decorate<LoudGreeter, IGreeter>]
				[Singleton<Printer, IChannel>]
				[Composite<AllChannels, IChannel>]
				public static partial class GreetingModule;
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(GreetingModule))]
				public static partial class MyContainer;
				""");

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("await ((global::Lib.IStartable)created).StartAsync().ConfigureAwait(false);").Exactly(2)
				.Because("the consumer matches the module's convention against the internal decorator and composite it builds through their generated factories");
		}

		[Test]
		public async Task AModuleConvention_InitializesTheResultOfAWrappedFactoryMember()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;
				using System.Threading.Tasks;

				namespace Lib;

				public interface IStartable { Task StartAsync(); }
				public sealed class Clock : IStartable { public Task StartAsync() => Task.CompletedTask; }

				[Module]
				[AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
				[Singleton<Clock>(Factory = nameof(CreateClock))]
				public static partial class ClockModule
				{
				    internal static Clock CreateClock() => new();
				}
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(ClockModule))]
				public static partial class MyContainer;
				""");

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("((global::Lib.IStartable)created).StartAsync()")
				.Because("the convention matches what the module's wrapper declares it returns");
		}

		[Test]
		public async Task ABaseConvention_InitializesItsPrivateImplementationInAnotherAssembly()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;
				using System.Runtime.CompilerServices;
				using System.Threading.Tasks;

				[assembly: InternalsVisibleTo("TestAssembly")]

				namespace Lib;

				public interface IStartable { Task StartAsync(); }
				public interface ITerminal { }

				[Container]
				[AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
				[Singleton<Shop.Terminal, ITerminal>]
				public static partial class Shop
				{
				    private sealed class Terminal : ITerminal, IStartable { public Task StartAsync() => Task.CompletedTask; }
				}
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container(Extends = typeof(Shop))]
				public static partial class TestShop;
				""");

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.TestShop.g.cs"]).Contains("await ((global::Lib.IStartable)created).StartAsync().ConfigureAwait(false);")
				.Because("the extending container reads the base's convention and runs it on the private terminal the base's factory builds");
		}

		internal const string FriendLibrary = """
		                                     using Awaiten;
		                                     using System.Runtime.CompilerServices;
		                                     using System.Threading.Tasks;

		                                     [assembly: InternalsVisibleTo("TestAssembly")]

		                                     namespace Lib;

		                                     internal interface IStartable { Task StartAsync(); }
		                                     public interface ITerminal { }
		                                     internal sealed class Terminal : ITerminal, IStartable { public Task StartAsync() => Task.CompletedTask; }

		                                     [Module]
		                                     [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
		                                     [Singleton<Terminal, ITerminal>]
		                                     public static partial class TerminalModule;
		                                     """;

		[Test]
		public async Task AnInternalConventionWithInternalsVisibleTo_LeavesTheRegistrationToAFriend()
		{
			GeneratorResult module = Generator.Run(FriendLibrary);
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(FriendLibrary, """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(TerminalModule))]
				public static partial class MyContainer;
				""");

			await That(module.Diagnostics).IsEmpty()
				.Because("an assembly granted InternalsVisibleTo can call the internal convention, so the module skips the factory instead of failing");
			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("await ((global::Lib.IStartable)created).StartAsync().ConfigureAwait(false);")
				.Because("the friend constructs the terminal itself and runs the internal convention");
		}
	}
}
