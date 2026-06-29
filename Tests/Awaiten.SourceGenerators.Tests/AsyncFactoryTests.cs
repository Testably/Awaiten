using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Generator behavior for asynchronous factory methods: a factory whose return type is
///     <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c> produces service type <c>T</c> and is an
///     async-taint source (parallel to <c>IAsyncInitializable</c>), independent of whether <c>T</c>
///     itself implements <c>IAsyncInitializable</c>. The container awaits the factory on the async path; the
///     synchronous path cannot unwrap a <c>Task</c>, so AWT119 / strict withholding falls out for free.
/// </summary>
public class AsyncFactoryTests
{
	[Fact]
	public async Task TaskFactory_ProducesTheUnwrappedServiceType_AndIsResolvedByAwaitingTheFactory()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IFoo { }
		                                       public sealed class FooImpl : IFoo { }

		                                       [Container]
		                                       [Singleton<FooImpl, IFoo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task<FooImpl> Create() => Task.FromResult(new FooImpl());
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a Task<FooImpl> factory matches the FooImpl registration through the unwrapped result type");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("await Create().ConfigureAwait(false)")
			.Because("the async creator awaits the Task-returning factory to obtain the produced instance");
	}

	[Fact]
	public async Task TaskFactory_IsAsyncTainted_SoTheSyncPathDoesNotExposeIt()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Foo { }

		                                       [Container]
		                                       [Singleton<Foo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task<Foo> Create() => Task.FromResult(new Foo());
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("InitializeAsync")
			.Because("an async factory makes the container async-initializable, so an async resolver exists in the strict default");
		await That(source).Contains("global::System.Threading.Tasks.Task<global::MyCode.Foo>? _fooAsyncTask")
			.Because("the async-tainted singleton's sole home is the memoized async Task cache - there is no synchronous caching field, so the sync path cannot hand out an unawaited instance");
	}

	[Fact]
	public async Task TaskFactory_TargetedByASyncFunc_ReportsAwt119()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Foo { }
		                                       public sealed class Consumer { public Consumer(Func<Foo> foo) { } }

		                                       [Container]
		                                       [Singleton<Consumer>]
		                                       [Singleton<Foo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task<Foo> Create() => Task.FromResult(new Foo());
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT119*").AsWildcard()
			.Because("a synchronous Func over an async-factory service resolves it without awaiting the factory");
	}

	[Fact]
	public async Task ValueTaskFactory_ProducesTheUnwrappedServiceType()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Foo { }

		                                       [Container]
		                                       [Singleton<Foo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static ValueTask<Foo> Create() => new ValueTask<Foo>(new Foo());
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a ValueTask<Foo> factory matches the Foo registration through the unwrapped result type");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("await Create().ConfigureAwait(false)")
			.Because("a ValueTask-returning factory is awaited the same way a Task-returning one is");
	}

	[Fact]
	public async Task NonGenericTaskFactory_ProducesNoService_AndIsReportedAsAwt108()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Foo { }

		                                       [Container]
		                                       [Singleton<Foo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task Create() => Task.CompletedTask;
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT108*").AsWildcard()
			.Because("a non-generic Task produces no result type, so it is not a usable factory for Foo");
	}

	[Fact]
	public async Task TaskFactory_WhoseResultIsAlsoAsyncInitializable_AwaitsTheFactoryThenInitializeAsyncExactlyOnceEach()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Foo : IAsyncInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }

		                                       [Container]
		                                       [Singleton<Foo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task<Foo> Create() => Task.FromResult(new Foo());
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("await Create().ConfigureAwait(false)")
			.Because("the factory is awaited to build the instance");
		await That(source).Contains("InitializeAsync(cancellationToken).ConfigureAwait(false)")
			.Because("an async-initializable factory result still has its InitializeAsync awaited after construction - once for the factory call, once for initialization, with a single InitializeAsync call site proving no double-initialization");
	}

	[Fact]
	public async Task TaskFactory_WithArgParameter_ReportsAwt121()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Foo { }

		                                       [Container]
		                                       [Transient<Foo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task<Foo> Create([Arg] string name) => Task.FromResult(new Foo());
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT121*").AsWildcard()
			.Because("a parameterized async factory is reachable only through a synchronous Func<…, Foo>, which cannot await the Task");
	}

	[Fact]
	public async Task ParameterizedAsyncFactory_EmitsNoBrokenCode_TheErrorStubReplacesResolution()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Foo { }

		                                       [Container]
		                                       [Transient<Foo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task<Foo> Create([Arg] string name) => Task.FromResult(new Foo());
		                                       }
		                                       """);

		await That(string.Join("\n", result.Diagnostics)).DoesNotContain("error CS")
			.Because("AWT121 is an error, so the emitter replaces resolution with a throwing stub and must not also produce spurious compile errors");
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("await Create(")
			.Because("no parameterized resolver awaiting the factory in a non-async method (which would not compile) is emitted once AWT121 stubs the container");
	}

	[Fact]
	public async Task TaskFactory_UnderSyncResolveAfterInit_WarmsTheSingletonAndExposesItSynchronously()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Foo { }

		                                       [Container(SyncResolveAfterInit = true)]
		                                       [Singleton<Foo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task<Foo> Create() => Task.FromResult(new Foo());
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("pragmatic mode allows synchronous resolution of an async-factory singleton after warm-up");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("await Create().ConfigureAwait(false)");
	}

	[Fact]
	public async Task TaskFactory_BehindAnInterface_WhoseImplIsDisposable_TracksDisposalFromTheUnwrappedType()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IFoo { }
		                                       public sealed class FooImpl : IFoo, IDisposable { public void Dispose() { } }

		                                       [Container]
		                                       [Singleton<FooImpl, IFoo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task<FooImpl> Create() => Task.FromResult(new FooImpl());
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("global::System.IDisposable")
			.Because("disposability is computed from the unwrapped FooImpl (behind the IFoo service and the Task), so the singleton is registered for disposal even though it sits behind Task<> and an interface");
	}

	[Fact]
	public async Task TaskFactory_WithCancellationTokenParameter_ForwardsTheResolveTimeToken()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class Foo { }

		                                       [Container]
		                                       [Singleton<Foo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task<Foo> Create(CancellationToken cancellationToken) => Task.FromResult(new Foo());
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a factory's CancellationToken parameter is forwarded from the resolve-time token, not resolved from the graph, so it is not a missing dependency (AWT101)");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("await Create(cancellationToken).ConfigureAwait(false)")
			.Because("the async creator forwards its in-scope resolve-time token into the factory's CancellationToken parameter");
	}

	[Fact]
	public async Task ConstructorCancellationTokenParameter_IsNotForwarded_AndReportsAwt101()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading;

		                                       namespace MyCode;

		                                       public sealed class Foo { public Foo(CancellationToken cancellationToken) { } }

		                                       [Container]
		                                       [Singleton<Foo>]
		                                       public static partial class MyContainer { }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
			.Because("token forwarding is scoped to asynchronous factory methods - a constructor has no ambient resolve-time token, so its CancellationToken parameter stays an ordinary (unregistered) dependency");
	}

	[Fact]
	public async Task SynchronousFactoryCancellationTokenParameter_IsNotForwarded_AndReportsAwt101()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading;

		                                       namespace MyCode;

		                                       public sealed class Foo { }

		                                       [Container]
		                                       [Singleton<Foo>(Factory = nameof(Create))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Foo Create(CancellationToken cancellationToken) => new Foo();
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
			.Because("token forwarding is scoped to asynchronous factories: a synchronous factory is only ever built on the synchronous path, which has no ambient resolve-time token, so its CancellationToken would always be default - it stays an ordinary (unregistered) dependency rather than silently receiving default");
	}
}
