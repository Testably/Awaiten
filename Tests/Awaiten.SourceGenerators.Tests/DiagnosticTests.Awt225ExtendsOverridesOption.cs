namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt225ExtendsOverridesOption
	{
		[Test]
		[Arguments("AsyncSafety = AsyncSafety.SyncAfterInit", "AsyncSafety")]
		[Arguments("LifetimeSafety = LifetimeSafety.Loose", "LifetimeSafety")]
		[Arguments("AsyncSafety = AsyncSafety.Strict", "AsyncSafety")]
		public async Task ReportsWhenAnExtendingContainerSetsAValidationOption(string option, string name)
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         [Container]
			                                         public static partial class Shop
			                                         {
			                                         }

			                                         [Container(Extends = typeof(Shop), {{option}})]
			                                         public static partial class TestShop
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).Contains($"*AWT225*TestShop*sets {name}*Shop*").AsWildcard()
				.Because("the test graph must be validated exactly like the production graph, even when the value matches the default");
		}

		[Test]
		public async Task DoesNotReportWhenTheOptionIsSetOnTheBase()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       [Container(AsyncSafety = AsyncSafety.SyncAfterInit, LifetimeSafety = LifetimeSafety.Loose)]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
		}

		[Test]
		public async Task TheExtendingContainerValidatesWithTheOptionsOfItsBase()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System;
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IInitializable { Task InitializeAsync(CancellationToken cancellationToken); }

			                                       public sealed class Connection : IInitializable
			                                       {
			                                           public System.Threading.Tasks.Task InitializeAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
			                                       }

			                                       public sealed class Consumer
			                                       {
			                                           public Consumer(Func<Connection> connection) { }
			                                       }

			                                       [Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
			                                       [AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
			                                       [Singleton<Connection>]
			                                       public static partial class Shop
			                                       {
			                                       }

			                                       [Container(Extends = typeof(Shop))]
			                                       [AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
			                                       [Singleton<Consumer>]
			                                       public static partial class TestShop
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a Func over an async-initialized service is allowed under the base's SyncAfterInit, which the extending container inherits");
		}
	}
}
