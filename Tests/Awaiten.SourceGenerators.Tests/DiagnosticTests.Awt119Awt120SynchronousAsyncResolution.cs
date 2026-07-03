using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt119Awt120SynchronousAsyncResolution
	{
		[Fact]
		public async Task Awt119_ReportsWhenAFuncTargetsAnAsyncInitializedService()
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

			                                       public sealed class Consumer { public Consumer(Func<Connection> connection) { } }

			                                       [Container]
			                                       [Singleton<Consumer>]
			                                       [Singleton<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT119*").AsWildcard()
				.Because("a synchronous Func over an async-initialized service resolves it without awaiting initialization");
		}

		[Fact]
		public async Task Awt119_ReportsForAnInjectedFuncMember()
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

			                                       // An injected member resolves through the same synchronous expression as a constructor
			                                       // parameter, and the Func wrapper launders the async taint off the owner - so without the
			                                       // member check the generated closure would call a synchronous resolver strict mode never emits.
			                                       public sealed class Consumer { [Inject] public Func<Connection> Connection { get; set; } }

			                                       [Container]
			                                       [Singleton<Consumer>]
			                                       [Singleton<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT119*").AsWildcard()
				.Because("an injected Func member over an async-initialized service is the same synchronous-resolution fault as a Func constructor parameter");
		}

		[Fact]
		public async Task Awt119_ReportsForADeferredFuncMember()
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

			                                       // A deferred Func member is still built synchronously at wiring time; the Func launders the
			                                       // taint, so the owner stays synchronous and its wiring would call a synchronous resolver that
			                                       // strict mode never generates for the async-tainted target.
			                                       public sealed class Consumer { [Inject(Deferred = true)] public Func<Connection> Connection { get; set; } }

			                                       [Container]
			                                       [Singleton<Consumer>]
			                                       [Singleton<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT119*").AsWildcard()
				.Because("a deferred Func member over an async-initialized service escapes the taint fixpoint but still resolves synchronously, so it must be reported instead of failing to compile in generated code");
		}

		[Fact]
		public async Task Awt119_ReportsForALazyRelationship()
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

			                                       public sealed class Consumer { public Consumer(Lazy<Connection> connection) { } }

			                                       [Container]
			                                       [Singleton<Consumer>]
			                                       [Singleton<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT119*").AsWildcard()
				.Because("a synchronous Lazy over an async-initialized service resolves it without awaiting initialization");
		}

		[Fact]
		public async Task Awt119_ReportsForAnOwnedRelationship()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Connection : IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       public sealed class Consumer { public Consumer(Owned<Connection> connection) { } }

			                                       [Container]
			                                       [Singleton<Consumer>]
			                                       [Transient<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT119*").AsWildcard()
				.Because("a synchronous Owned handle over an async-initialized service resolves it without awaiting initialization");
		}

		[Fact]
		public async Task Awt120_ReportsWithThePathWhenAFuncReachesAnAsyncServiceTransitively()
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

			                                       public sealed class Repository { public Repository(Connection connection) { } }
			                                       public sealed class Consumer { public Consumer(Func<Repository> repository) { } }

			                                       [Container]
			                                       [Singleton<Consumer>]
			                                       [Singleton<Repository>]
			                                       [Singleton<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT120") && d.Contains("Repository") && d.Contains("Connection"))).IsTrue()
				.Because("the diagnostic names the dependency path from the synchronously-resolved service to the async-initialized one");
		}

		[Fact]
		public async Task DoesNotReport_ForADirectDependencyOnAnAsyncService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Connection : IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       public sealed class Consumer { public Consumer(Connection connection) { } }

			                                       [Container]
			                                       [Singleton<Consumer>]
			                                       [Singleton<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			// A direct dependency is awaited on the async construction path (the consumer is itself
			// async-tainted), so it is not a synchronous-resolution error.
			await That(result.Diagnostics).DoesNotContain("*AWT119*").AsWildcard();
			await That(result.Diagnostics).DoesNotContain("*AWT120*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotReport_WhenSyncResolveAfterInitIsSet()
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

			                                       public sealed class Consumer { public Consumer(Func<Connection> connection) { } }

			                                       [Container(SyncResolveAfterInit = true)]
			                                       [Singleton<Consumer>]
			                                       [Singleton<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT119*").AsWildcard()
				.Because("pragmatic mode allows synchronous resolution of async-initialized services after warm-up");
			await That(result.Diagnostics).DoesNotContain("*AWT120*").AsWildcard();
		}
	}
}
