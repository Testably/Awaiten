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

			await That(diagnostics.Count(d => d.Contains("AWT156"))).IsEqualTo(1)
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
		public async Task DoesNotReportForASynchronousUsingOfAScopeWhenTheAsyncOnlyDisposableIsASingleton()
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

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsFalse()
				.Because("a singleton always tracks on the Root - even when first resolved inside a child scope - so the scope's synchronous drain can never reach it");
		}

		[Fact]
		public async Task ReportsForASynchronousUsingOfAScopeWhenTheAsyncOnlyDisposableIsScoped()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Connection : IAsyncDisposable { public ValueTask DisposeAsync() => default; }

			                                       [Container]
			                                       [Scoped<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }

			                                       public static class Consumer
			                                       {
			                                       	public static async Task Use()
			                                       	{
			                                       		await using MyContainer.Root root = new();
			                                       		using MyContainer.Scope scope = root.CreateScope();
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics.Count(d => d.Contains("AWT156"))).IsEqualTo(1)
				.Because("a scoped async-only service is tracked on the scope that resolves it, so the scope's synchronous using is the same latent throw");
		}

		[Fact]
		public async Task ReportsForASynchronousParenthesizedUsingStatement()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(AsyncOnlyContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static void Use()
			                                       	{
			                                       		using (MyContainer.Root root = new())
			                                       		{
			                                       		}
			                                       	}
			                                       }
			                                       """);

			string[] reports = diagnostics.Where(d => d.Contains("AWT156")).ToArray();
			await That(reports).HasCount(1)
				.Because("the parenthesized using statement disposes the Root synchronously, exactly like the declaration form");
			await That(reports[0]).StartsWith("(18,27)")
				.Because("the diagnostic points at the declarator, not at the whole statement including its body block");
		}

		[Fact]
		public async Task ReportsForASynchronousUsingStatementOverAnExistingVariable()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(AsyncOnlyContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static void Use()
			                                       	{
			                                       		MyContainer.Root root = new();
			                                       		using (root)
			                                       		{
			                                       		}
			                                       	}
			                                       }
			                                       """);

			string[] reports = diagnostics.Where(d => d.Contains("AWT156")).ToArray();
			await That(reports).HasCount(1)
				.Because("a using statement over an already-declared Root disposes it synchronously all the same");
			await That(reports[0]).StartsWith("(19,10)")
				.Because("the diagnostic points at the resource expression, not at the whole statement including its body block");
		}

		[Fact]
		public async Task ReportsOncePerDeclaratorOfAMultiDeclaratorUsingDeclaration()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(AsyncOnlyContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static void Use()
			                                       	{
			                                       		using MyContainer.Root a = new(), b = new();
			                                       	}
			                                       }
			                                       """);

			string[] reports = diagnostics.Where(d => d.Contains("AWT156")).ToArray();
			await That(reports).HasCount(2)
				.Because("each declarator is its own synchronously disposed Root");
			await That(reports[0]).StartsWith("(18,26)")
				.Because("the first diagnostic points at the first declarator");
			await That(reports[1]).StartsWith("(18,37)")
				.Because("the second diagnostic points at the second declarator, not at the shared whole-statement span twice");
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

			await That(diagnostics.Count(d => d.Contains("AWT156"))).IsEqualTo(1)
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

		[Fact]
		public async Task DoesNotReportForAUserAuthoredScopeTypeNestedInTheContainer()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Connection : IAsyncDisposable { public ValueTask DisposeAsync() => default; }

			                                       [Container]
			                                       [Singleton<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       	public sealed class FakeScope : IAwaitenScope
			                                       	{
			                                       		public object Resolve(Type serviceType) => throw new NotSupportedException();
			                                       		public bool TryResolve(Type serviceType, out object? instance) { instance = null; return false; }
			                                       		public Task<object> ResolveAsync(Type serviceType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			                                       		public IAwaitenScope CreateScope() => this;
			                                       		public Task<IAwaitenScope> CreateScopeAsync(CancellationToken cancellationToken = default) => Task.FromResult<IAwaitenScope>(this);
			                                       		public void Dispose() { }
			                                       	}
			                                       }

			                                       public static class Consumer
			                                       {
			                                       	public static void Use()
			                                       	{
			                                       		using MyContainer.FakeScope fake = new();
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsFalse()
				.Because("a user-authored IAwaitenScope nested in the container class is not the generated Root/Scope; only those own the container's instances");
		}

		[Fact]
		public async Task DoesNotReportForAContainerDeclaredInAReferencedAssembly()
		{
			string[] diagnostics = await Analyzer.RunWithReferencedAssembly<AwaitenAnalyzer>(AsyncOnlyContainer, """
			                                       using MyCode;

			                                       namespace Consuming;

			                                       public static class Consumer
			                                       {
			                                       	public static void Use()
			                                       	{
			                                       		using MyContainer.Root root = new();
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsFalse()
				.Because("a metadata container's graph cannot be rebuilt faithfully from the consuming compilation (its default [Scan] would sweep the wrong assembly), so it stays invisible to this check - a documented false negative with the runtime throw as the backstop");
		}
	}
}
