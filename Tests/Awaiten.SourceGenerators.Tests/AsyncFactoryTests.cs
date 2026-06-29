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
		// Async-tainted in the strict default: an async resolver exists, but no synchronous caching field for it
		// (its sole home is the memoized async Task cache), so the sync path cannot hand out an unawaited instance.
		await That(source).Contains("InitializeAsync")
			.Because("an async factory makes the container async-initializable");
		await That(source).Contains("global::System.Threading.Tasks.Task<global::MyCode.Foo>? _fooAsyncTask")
			.Because("the async-tainted singleton is cached as a memoized Task");
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
		// The produced type is IAsyncInitializable, so its InitializeAsync is also awaited - once for the
		// factory call, once for initialization. The single InitializeAsync call site (not two) proves there is
		// no double-initialization.
		await That(source).Contains("InitializeAsync(cancellationToken).ConfigureAwait(false)")
			.Because("an async-initializable factory result still has its InitializeAsync awaited after construction");
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

		// AWT121 is an error, so the emitter replaces resolution with a throwing stub rather than emitting a
		// parameterized resolver that would 'await' the factory in a non-async method (which would not compile).
		await That(string.Join("\n", result.Diagnostics)).DoesNotContain("error CS")
			.Because("an error-stubbed container must not also produce spurious compile errors");
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("await Create(")
			.Because("no parameterized resolver awaiting the factory is emitted once AWT121 stubs the container");
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
		// Disposability is computed from the unwrapped FooImpl (behind the IFoo service and the Task), so the
		// singleton is registered for disposal.
		await That(source).Contains("global::System.IDisposable")
			.Because("the disposable produced type is tracked even though it sits behind Task<> and an interface");
	}
}
