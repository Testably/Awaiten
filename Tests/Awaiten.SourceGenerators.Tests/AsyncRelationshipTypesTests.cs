namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Generator behavior for the asynchronous relationship types <c>Task&lt;T&gt;</c>,
///     <c>Func&lt;…, Task&lt;T&gt;&gt;</c> and <c>Lazy&lt;Task&lt;T&gt;&gt;</c>: awaitable counterparts of the
///     synchronous <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c> relationships. They defer resolution and so
///     launder async taint - a synchronously-resolvable consumer can hold one over an async-initialized
///     service without becoming async-tainted and without tripping AWT119 / AWT120 - and they resolve their
///     target through its async resolver (awaiting initialization). A <c>Func&lt;TArg…, Task&lt;T&gt;&gt;</c>
///     additionally forwards runtime <c>[Arg]</c>s to a parameterized async service through its async
///     resolver, which is the correct (and only) path for an [Arg]-plus-async service.
/// </summary>
public class AsyncRelationshipTypesTests
{
	[Fact]
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

		// The async relationships launder the taint (like Func/Lazy), so Pool stays synchronously resolvable
		// and there is no AWT119/AWT120.
		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("{ typeof(global::MyCode.Pool),")
			.Because("Pool is not async-tainted, so it keeps a synchronous by-type dispatch entry");
		await That(source).Contains("new global::System.Func<global::System.Threading.Tasks.Task<global::MyCode.Connection>>(() => __root.ResolveConnectionAsync(default))");
		await That(source).Contains("new global::System.Lazy<global::System.Threading.Tasks.Task<global::MyCode.Connection>>(() => __root.ResolveConnectionAsync(default))");
	}

	[Fact]
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

		await That(source).Contains("new global::System.Func<int, global::System.Threading.Tasks.Task<global::MyCode.Robot>>((a0) => ResolveRobotAsync(a0, default))")
			.Because("the async factory forwards the runtime argument to the async parameterized resolver");
		await That(source).Contains("internal async global::System.Threading.Tasks.Task<global::MyCode.Robot> ResolveRobotAsync(int a0, global::System.Threading.CancellationToken cancellationToken)")
			.Because("the async parameterized resolver takes the runtime arguments alongside the token");
		await That(source).Contains("InitializeAsync(cancellationToken).ConfigureAwait(false)")
			.Because("the async parameterized resolver awaits the service's initialization");
	}

	[Fact]
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

	[Fact]
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

		                                       [Container(SyncResolveAfterInit = true)]
		                                       [Transient<Robot>]
		                                       [Singleton<Factory>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// In pragmatic mode the synchronous Func<int, Robot> is allowed (AWT119 is suppressed); its sync
		// parameterized resolver must forward the argument and block on the async resolver so initialization
		// still runs, never building a second uninitialized instance.
		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("internal global::MyCode.Robot ResolveRobot(int a0)")
			.Because("the pragmatic sync parameterized resolver keeps the runtime-argument signature");
		await That(source).Contains("return ResolveRobotAsync(a0, default).GetAwaiter().GetResult();")
			.Because("it forwards the argument and blocks on the single async (initializing) resolver");
	}

	[Fact]
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

	[Fact]
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
		await That(source).Contains("ResolveConnectionAsync(__ct)")
			.Because("the throwaway scope awaits the service's async resolver (initialization included)");
	}

	[Fact]
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
		await That(source).Contains("public class Scope : global::Awaiten.IAwaitenScope")
			.Because("the IAsyncDisposable surface is on the concrete Scope, not added to the IAwaitenScope interface");
		await That(source).Contains("global::System.IAsyncDisposable")
			.Because("the generated Scope implements IAsyncDisposable when the compilation can see the type");
		await That(source).Contains("public async global::System.Threading.Tasks.ValueTask DisposeAsync()")
			.Because("a DisposeAsync drain is emitted alongside the synchronous Dispose");
		await That(source).Contains("await __asyncDisposable.DisposeAsync().ConfigureAwait(false)")
			.Because("the async drain awaits IAsyncDisposable, preferring it over a synchronous Dispose");
		await That(source).Contains("global::System.InvalidOperationException")
			.Because("the synchronous Dispose throws when it meets an IAsyncDisposable-only service rather than blocking");
	}
}
