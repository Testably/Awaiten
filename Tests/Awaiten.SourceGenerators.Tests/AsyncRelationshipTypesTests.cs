namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The asynchronous relationship types <c>Task&lt;T&gt;</c>, <c>Func&lt;…, Task&lt;T&gt;&gt;</c> and
///     <c>Lazy&lt;Task&lt;T&gt;&gt;</c>. Like the synchronous Func/Lazy relationships they defer resolution and
///     launder async taint, so a synchronously-resolvable consumer can hold one over an async service without
///     tripping AWT119/AWT120, and they resolve the target through its async resolver.
///     <c>Func&lt;TArg…, Task&lt;T&gt;&gt;</c> also forwards runtime <c>[Arg]</c>s to a parameterized async service.
/// </summary>
public class AsyncRelationshipTypesTests
{
	[Test]
	public async Task AsyncRelationships_ResolveAnAsyncServiceWithoutTaintingAStrictConsumer()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Connection : IAsyncInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Pool
		                                       {
		                                           public Pool(Func<Task<Connection>> factory, Lazy<Task<Connection>> lazy, Task<Connection> task) { }
		                                       }

		                                       [Container]
		                                       [Singleton<Connection>]
		                                       [Singleton<Pool>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// The async relationships launder the taint (like Func/Lazy), so Pool stays synchronously resolvable (no AWT119/AWT120).
		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new __Bucket(typeof(global::MyCode.Pool),")
			.Because("Pool is not async-tainted, so it keeps a synchronous by-type dispatch entry");
		await That(source).Contains("new global::System.Func<global::System.Threading.Tasks.Task<global::MyCode.Connection>>(() => Root.ResolveConnectionAsync(__s.__root, default))");
		await That(source).Contains("new global::System.Lazy<global::System.Threading.Tasks.Task<global::MyCode.Connection>>(() => Root.ResolveConnectionAsync(__s.__root, default))");
	}

	[Test]
	public async Task ParameterizedFuncOfTask_ForwardsArgumentsThroughTheAsyncResolver()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Robot : IAsyncInitializable
		                                       {
		                                           public Robot([Arg] int id) { }
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Factory
		                                       {
		                                           public Factory(Func<int, Task<Robot>> make) { }
		                                       }

		                                       [Container]
		                                       [Transient<Robot>]
		                                       [Singleton<Factory>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// A parameterized async service is legal now that Func<TArg…, Task<T>> exists: there is no AWT121.
		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::System.Func<int, global::System.Threading.Tasks.Task<global::MyCode.Robot>>((a0) => ResolveRobotAsync(__s, a0, default))")
			.Because("the async factory forwards the runtime argument to the async parameterized resolver");
		await That(source).Contains("internal static async global::System.Threading.Tasks.Task<global::MyCode.Robot> ResolveRobotAsync(Scope __s, int a0, global::System.Threading.CancellationToken cancellationToken)")
			.Because("the async parameterized resolver takes the runtime arguments alongside the token");
		await That(source).Contains("InitializeAsync(cancellationToken).ConfigureAwait(false)")
			.Because("the async parameterized resolver awaits the service's initialization");
	}

	[Test]
	public async Task ParameterizedFuncOfTask_WithMismatchedArguments_ReportsAwt113()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Robot : IAsyncInitializable
		                                       {
		                                           public Robot([Arg] int id) { }
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Factory
		                                       {
		                                           public Factory(Func<string, Task<Robot>> make) { }
		                                       }

		                                       [Container]
		                                       [Transient<Robot>]
		                                       [Singleton<Factory>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT113*").AsWildcard()
			.Because("a Func<string, Task<Robot>> requests a string but Robot's [Arg] parameter expects an int");
	}

	[Test]
	public async Task PragmaticMode_ParameterizedAsync_SyncResolverForwardsArgumentsAndBlocksOnTheAsyncResolver()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Robot : IAsyncInitializable
		                                       {
		                                           public Robot([Arg] int id) { }
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Factory
		                                       {
		                                           public Factory(Func<int, Robot> make) { }
		                                       }

		                                       [Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
		                                       [Transient<Robot>]
		                                       [Singleton<Factory>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// Pragmatic mode allows the sync Func<int, Robot> (AWT119 suppressed); its resolver forwards the argument
		// and blocks on the async resolver so initialization still runs, never building a second uninitialized instance.
		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("internal static global::MyCode.Robot ResolveRobot(Scope __s, int a0)")
			.Because("the pragmatic sync parameterized resolver keeps the runtime-argument signature");
		await That(source).Contains("return ResolveRobotAsync(__s, a0, default).GetAwaiter().GetResult();")
			.Because("it forwards the argument and blocks on the single async (initializing) resolver");
	}

	[Test]
	public async Task BareTaskOfAParameterizedService_ReportsAwt115()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Robot : IAsyncInitializable
		                                       {
		                                           public Robot([Arg] int id) { }
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Factory
		                                       {
		                                           public Factory(Task<Robot> robot) { }
		                                       }

		                                       [Container]
		                                       [Transient<Robot>]
		                                       [Singleton<Factory>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT115*").AsWildcard()
			.Because("a bare Task<Robot> supplies no runtime arguments, so a parameterized service must instead be reached through a Func<TArg…, Task<T>>");
	}

	[Test]
	public async Task AsyncOwned_FuncOfTaskOfOwned_AsyncResolvesIntoAThrowawayScopeAndIsExemptFromAwt118()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Connection : IAsyncInitializable, IDisposable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                           public void Dispose() { }
		                                       }
		                                       public sealed class Pool { public Pool(Func<Task<Owned<Connection>>> open) { } }

		                                       [Container]
		                                       [Transient<Connection>]
		                                       [Singleton<Pool>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// The owned form is leak-free, so a root-held async factory over a disposable transient is not AWT118.
		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("new global::System.Func<global::System.Threading.Tasks.Task<global::Awaiten.Owned<global::MyCode.Connection>>>")
			.Because("the consumer binds a Func<Task<Owned<Connection>>> factory");
		await That(source).Contains("__OwnedAsync<global::MyCode.Connection>")
			.Because("the async owned handle resolves the service into a throwaway child scope through the async owned helper");
		await That(source).Contains("ResolveConnectionAsync(__o, __ct)")
			.Because("the throwaway scope awaits the service's async resolver (initialization included)");
	}

	[Test]
	public async Task AsyncDisposableService_EmitsDisposeAsyncAndAnAwaitingDrain()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Connection : IAsyncInitializable, IAsyncDisposable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                           public ValueTask DisposeAsync() => default;
		                                       }

		                                       [Container]
		                                       [Singleton<Connection>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("public async global::System.Threading.Tasks.ValueTask DisposeAsync()")
			.Because("a DisposeAsync drain is emitted alongside the synchronous Dispose");
		await That(source).Contains("await __asyncDisposable.DisposeAsync().ConfigureAwait(false)")
			.Because("the async drain awaits IAsyncDisposable, preferring it over a synchronous Dispose");
		await That(source).Contains("global::System.InvalidOperationException")
			.Because("the synchronous Dispose throws when it meets an IAsyncDisposable-only service rather than blocking");
	}

	[Test]
	public async Task CreateScopeAsync_ReturnsTheConcreteScopeAndImplementsTheInterfaceExplicitly()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Order;

		                                       [Container]
		                                       [Scoped<Order>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("public global::System.Threading.Tasks.Task<Scope> CreateScopeAsync(global::System.Threading.CancellationToken cancellationToken = default)")
			.Because("like CreateScope, the async counterpart hands back the concrete Scope, which await using can dispose");
		await That(source).Contains("global::System.Threading.Tasks.Task<global::Awaiten.IAwaitenScope> global::Awaiten.IAwaitenScope.CreateScopeAsync(global::System.Threading.CancellationToken cancellationToken)")
			.Because("the IAwaitenScope member is implemented explicitly over the concrete one");
	}
}
