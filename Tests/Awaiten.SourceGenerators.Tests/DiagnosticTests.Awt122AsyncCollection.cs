namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt122AsyncCollection
	{
		[Fact]
		public async Task ReportsWhenACollectionMemberIsAsyncInitialized()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AsyncPlugin : IPlugin, IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }
			                                       public sealed class Host { public Host(IEnumerable<IPlugin> plugins) { } }

			                                       [Container]
			                                       [Singleton<AsyncPlugin, IPlugin>]
			                                       [Singleton<Host>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT122*").AsWildcard()
				.Because("a synchronously materialized collection cannot await an async-initialized member");
		}

		[Fact]
		public async Task DoesNotReportForASynchronousCollection()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class Alpha : IPlugin { }
			                                       public sealed class Beta : IPlugin { }
			                                       public sealed class Host { public Host(IEnumerable<IPlugin> plugins) { } }

			                                       [Container]
			                                       [Singleton<Alpha, IPlugin>]
			                                       [Singleton<Beta, IPlugin>]
			                                       [Singleton<Host>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT122*").AsWildcard()
				.Because("a collection whose members are all synchronous is materialized without any async initialization");
		}

		[Fact]
		public async Task DoesNotReportUnderSyncResolveAfterInit()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AsyncPlugin : IPlugin, IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }
			                                       public sealed class Host { public Host(IEnumerable<IPlugin> plugins) { } }

			                                       [Container(SyncResolveAfterInit = true)]
			                                       [Singleton<AsyncPlugin, IPlugin>]
			                                       [Singleton<Host>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT122*").AsWildcard()
				.Because("SyncResolveAfterInit permits synchronous resolution of an async-tainted member after warm-up");
		}
	}
}
