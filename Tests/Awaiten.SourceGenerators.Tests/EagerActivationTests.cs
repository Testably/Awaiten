namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Eager singleton activation (<c>[Singleton&lt;T&gt;(Eager = true)]</c>): the root constructor warms
///     eager singletons through their cached resolvers in registration order, leaving lazy ones out. An
///     async-initialized eager singleton is AWT161 in strict mode; <c>SyncResolveAfterInit</c> allows it
///     by emitting a blocking synchronous resolver.
/// </summary>
public class EagerActivationTests
{
	[Fact]
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

		await That(source).Contains("public Root() : base()");
		await That(source).Contains("ResolveWarm(this);")
			.Because("the eager singleton is constructed in the root constructor through its cached resolver");
	}

	[Fact]
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

	[Fact]
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

	[Fact]
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

	[Fact]
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

		                                       [Container(SyncResolveAfterInit = true)]
		                                       [Singleton<AsyncWarm>(Eager = true)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("SyncResolveAfterInit emits a blocking synchronous resolver, so eager construction is allowed");
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("ResolveAsyncWarm(this);")
			.Because("the eager async singleton is constructed at build time through its (blocking) synchronous resolver");
	}
}
