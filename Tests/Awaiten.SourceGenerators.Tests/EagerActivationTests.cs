namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Eager singleton activation (<c>[Singleton&lt;T&gt;(Eager = true)]</c>): the root constructor warms
///     eager singletons through their cached resolvers in registration order, leaving lazy ones out. An
///     async-initialized eager singleton is AWT161 in strict mode; <c>AsyncSafety.SyncAfterInit</c> allows it
///     by emitting a blocking synchronous resolver.
/// </summary>
public class EagerActivationTests
{
	[Test]
	public async Task EagerSingleton_IsConstructedInTheGeneratedRootConstructor()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Warm { }

		                                       [Container]
		                                       [Singleton<Warm>(Eager = true)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("public Root(global::Awaiten.IExternalResolver? externalResolver) : base(externalResolver)");
		await That(source).Contains("ResolveWarm(this);")
			.Because("the eager singleton is constructed in the root constructor through its cached resolver");
	}

	[Test]
	public async Task EagerSingletons_AreConstructedInRegistrationOrder()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class First { }
		                                       public sealed class Second { }

		                                       [Container]
		                                       [Singleton<First>(Eager = true)]
		                                       [Singleton<Second>(Eager = true)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source.IndexOf("ResolveFirst(this);", System.StringComparison.Ordinal)
			< source.IndexOf("ResolveSecond(this);", System.StringComparison.Ordinal)).IsTrue()
			.Because("eager singletons are constructed in registration order");
	}

	[Test]
	public async Task LazySingleton_IsNotConstructedInTheRootConstructor()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Cold { }

		                                       [Container]
		                                       [Singleton<Cold>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("ResolveCold(this);")
			.Because("a lazy singleton is constructed on first resolve, not at container build time");
	}

	[Test]
	public async Task EagerAsyncInitializedSingleton_InStrictMode_IsAwt161()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class AsyncWarm : IAsyncInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }

		                                       [Container]
		                                       [Singleton<AsyncWarm>(Eager = true)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT161*").AsWildcard()
			.Because("an async-initialized eager singleton has no synchronous construction path in strict mode");
	}

	[Test]
	public async Task EagerAsyncInitializedSingleton_InPragmaticMode_IsAllowed()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class AsyncWarm : IAsyncInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }

		                                       [Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
		                                       [Singleton<AsyncWarm>(Eager = true)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("AsyncSafety.SyncAfterInit emits a blocking synchronous resolver, so eager construction is allowed");
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("ResolveAsyncWarm(this);")
			.Because("the eager async singleton is constructed at build time through its (blocking) synchronous resolver");
	}

	public sealed class Decorated
	{
		[Test]
		public async Task EagerDecoratedSingleton_IsConstructedThroughItsOutermostDecorator()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClient { }
			                                       public sealed class Client : IClient { }
			                                       public sealed class Retrying : IClient { public Retrying(IClient inner) { } }
			                                       public sealed class Monitoring : IClient { public Monitoring(IClient inner) { } }

			                                       [Container]
			                                       [Singleton<Client, IClient>(Eager = true)]
			                                       [Decorate<Retrying, IClient>]
			                                       [Decorate<Monitoring, IClient>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

			await That(source).Contains("ResolveMonitoring(this);")
				.Because("the outermost decorator builds the whole chain, so the service is built as its consumers see it");
			await That(source).DoesNotContain("ResolveClient(this);")
				.Because("the undecorated instance is built as the innermost link of the chain, not on its own");
			await That(source).DoesNotContain("ResolveRetrying(this);")
				.Because("an inner decorator is built by the decorator wrapping it");
		}

		[Test]
		public async Task EagerSingleton_WithAsyncInitializedDecorator_InStrictMode_IsAwt161()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IClient { }
			                                       public sealed class Client : IClient { }
			                                       public sealed class AsyncMonitoring : IClient, IAsyncInitializable
			                                       {
			                                           public AsyncMonitoring(IClient inner) { }
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<Client, IClient>(Eager = true)]
			                                       [Decorate<AsyncMonitoring, IClient>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).HasCount(1);
			await That(result.Diagnostics).Contains("*AWT161*AsyncMonitoring*").AsWildcard()
				.Because("the eager service is built through its async-initialized decorator, which has no synchronous construction path in strict mode");
		}

		[Test]
		public async Task EagerAsyncInitializedSingleton_WithDecorator_InStrictMode_IsReportedOnce()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IClient { }
			                                       public sealed class AsyncClient : IClient, IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }
			                                       public sealed class Monitoring : IClient { public Monitoring(IClient inner) { } }

			                                       [Container]
			                                       [Singleton<AsyncClient, IClient>(Eager = true)]
			                                       [Decorate<Monitoring, IClient>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).HasCount(1);
			await That(result.Diagnostics).Contains("*AWT161*AsyncClient*").AsWildcard()
				.Because("the eager singleton is reported once, naming the async-initialized service itself");
		}
	}
}
