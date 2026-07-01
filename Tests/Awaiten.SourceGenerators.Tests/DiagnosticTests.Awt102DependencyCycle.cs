using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt102DependencyCycle
	{
		[Fact]
		public async Task IsBrokenByAFuncDependency()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class A { public A(Func<B> b) { } }
			                                       public sealed class B { public B(A a) { } }

			                                       [Container]
			                                       [Singleton<A>]
			                                       [Singleton<B>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT102*").AsWildcard()
				.Because("the Func<B> defers resolution, so the A -> B -> A edge is not a hard cycle");
		}

		[Fact]
		public async Task ReportsWithThePath()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class A { public A(B b) { } }
			                                       public sealed class B { public B(A a) { } }

			                                       [Container]
			                                       [Singleton<A>]
			                                       [Singleton<B>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			string[] cycleDiagnostics = result.Diagnostics.Where(d => d.Contains("AWT102")).ToArray();
			await That(cycleDiagnostics).IsNotEmpty();
			await That(cycleDiagnostics).Contains("*->*").AsWildcard();
		}

		[Fact]
		public async Task IsClosedByABareOwnedDependency()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class A { public A(Owned<B> b) { } }
			                                       public sealed class B { public B(Owned<A> a) { } }

			                                       [Container]
			                                       [Singleton<A>]
			                                       [Singleton<B>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT102*").AsWildcard()
				.Because("a bare Owned<T> resolves its target at construction time, so the A -> B -> A cycle overflows rather than being broken");
		}

		[Fact]
		public async Task IsClosedByABareTaskDependency()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class A { public A(Task<B> b) { } }
			                                       public sealed class B { public B(Task<A> a) { } }

			                                       [Container]
			                                       [Singleton<A>]
			                                       [Singleton<B>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT102*").AsWildcard()
				.Because("a bare Task<T> over a synchronous target is Task.FromResult(resolver()), resolved eagerly, so the A -> B -> A cycle overflows");
		}

		[Fact]
		public async Task IsClosedByABareTaskDependencyEvenWhenTheTargetIsAsyncInitialized()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class A : IAsyncInitializable
			                                       {
			                                           public A(Task<B> b) { }
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }
			                                       public sealed class B : IAsyncInitializable
			                                       {
			                                           public B(Task<A> a) { }
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<A>]
			                                       [Singleton<B>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT102*").AsWildcard()
				.Because("the async resolver constructs (and so resolves the bare Task<T>) in its synchronous prefix, before the memoized task is published, so an async target overflows the same way");
		}

		[Fact]
		public async Task IsBrokenByAFuncOfTaskDependency()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class A : IAsyncInitializable
			                                       {
			                                           public A(Func<Task<B>> b) { }
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }
			                                       public sealed class B { public B(A a) { } }

			                                       [Container]
			                                       [Singleton<A>]
			                                       [Singleton<B>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT102*").AsWildcard()
				.Because("a Func<Task<B>> stores a closure and defers resolution, so the A -> B -> A edge is not a construction cycle");
		}
	}
}
