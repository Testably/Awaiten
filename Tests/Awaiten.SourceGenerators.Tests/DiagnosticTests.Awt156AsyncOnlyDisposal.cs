using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	// AWT156 fires at a synchronous disposal site (using / Dispose()) of a generated Root/Scope whose
	// container owns an IAsyncDisposable-only service - never at the registration, which is fully supported
	// as long as disposal is asynchronous. It is reported by AwaitenAnalyzer (not the generator) so a
	// deliberate synchronous site can be suppressed in source; these tests therefore drive the analyzer.
	public class Awt156AsyncOnlyDisposal
	{
		private const string AsyncOnlyContainer = """
		                                          using Awaiten;
		                                          using System;
		                                          using System.Threading.Tasks;

		                                          namespace MyCode;

		                                          public sealed class Connection : IAsyncDisposable { public ValueTask DisposeAsync() => default; }

		                                          [Container]
		                                          [Singleton<Connection>]
		                                          public static partial class MyContainer
		                                          {
		                                          }

		                                          """;

		[Fact]
		public async Task ReportsForASynchronousUsingOfTheRoot()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(AsyncOnlyContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static void Use()
			                                       	{
			                                       		using MyContainer.Root root = new();
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsTrue()
				.Because("a synchronous using of the Root throws at runtime when its drain reaches the IAsyncDisposable-only service");
		}

		[Fact]
		public async Task DoesNotReportForAnAwaitUsingOfTheRoot()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(AsyncOnlyContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static async Task Use()
			                                       	{
			                                       		await using MyContainer.Root root = new();
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsFalse()
				.Because("await using reaches DisposeAsync, which tears an async-only disposable down correctly");
		}

		[Fact]
		public async Task DoesNotReportForTheRegistrationAlone()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(AsyncOnlyContainer);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsFalse()
				.Because("registering an async-only disposable is fully supported; only a synchronous disposal site is at fault");
		}

		[Fact]
		public async Task DoesNotReportWhenTheContainerOwnsNoAsyncOnlyDisposable()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Connection : IAsyncDisposable, IDisposable
			                                       {
			                                       	public ValueTask DisposeAsync() => default;
			                                       	public void Dispose() { }
			                                       }

			                                       [Container]
			                                       [Singleton<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }

			                                       public static class Consumer
			                                       {
			                                       	public static void Use()
			                                       	{
			                                       		using MyContainer.Root root = new();
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsFalse()
				.Because("a service that also implements IDisposable is torn down on either disposal path");
		}

		[Fact]
		public async Task ReportsForASynchronousUsingOfAScope()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(AsyncOnlyContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static async Task Use()
			                                       	{
			                                       		await using MyContainer.Root root = new();
			                                       		using MyContainer.Scope scope = root.CreateScope();
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsTrue()
				.Because("a child scope owns the instances it creates, so its synchronous using is the same latent throw");
		}

		[Fact]
		public async Task ReportsForADirectDisposeCall()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(AsyncOnlyContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static void Use()
			                                       	{
			                                       		MyContainer.Root root = new();
			                                       		root.Dispose();
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsTrue()
				.Because("an explicit synchronous Dispose() on the Root is the same latent throw as a using statement");
		}

		[Fact]
		public async Task DoesNotReportForADisposeThroughTheScopeInterface()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(AsyncOnlyContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static void Use()
			                                       	{
			                                       		IAwaitenScope root = new MyContainer.Root();
			                                       		root.Dispose();
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsFalse()
				.Because("an interface-typed receiver does not reveal the container, so the runtime throw remains the backstop (a documented false negative)");
		}
	}
}
