namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt121ParameterizedAsyncInitialization
	{
		[Fact]
		public async Task ReportsWhenAParameterizedServiceIsAlsoAsyncInitializable()
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

			                                       [Container]
			                                       [Transient<Robot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT121*").AsWildcard()
				.Because("a parameterized service is reachable only through a synchronous Func<…, T> that cannot await InitializeAsync");
		}

		[Fact]
		public async Task ReportsEvenInPragmaticMode()
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

			                                       public sealed class Factory { public Factory(Func<int, Robot> f) { } }

			                                       [Container(SyncResolveAfterInit = true)]
			                                       [Transient<Robot>]
			                                       [Singleton<Factory>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			// SyncResolveAfterInit would otherwise construct the parameterized service synchronously and hand it
			// back without ever calling InitializeAsync; the combination is rejected regardless of the mode.
			await That(result.Diagnostics).Contains("*AWT121*").AsWildcard()
				.Because("the unsupported combination is rejected in both strict and pragmatic modes");
		}

		[Fact]
		public async Task DoesNotReport_ForANonParameterizedAsyncInitializableService()
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

			                                       [Container]
			                                       [Singleton<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT121*").AsWildcard();
		}
	}
}
