using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	// AWT156 is reported by AwaitenAnalyzer (not the generator) so a team that deliberately disposes its
	// container with 'await using' can suppress it in source; these tests therefore drive the analyzer.
	public class Awt156AsyncOnlyDisposal
	{
		[Fact]
		public async Task ReportsForAnAsyncOnlyDisposableService()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
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
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsTrue()
				.Because("an IAsyncDisposable-only service forces DisposeAsync on the container, which a synchronous Dispose() cannot honor");
		}

		[Fact]
		public async Task DoesNotReportWhenTheServiceAlsoImplementsIDisposable()
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
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsFalse()
				.Because("a service that also implements IDisposable is torn down on either disposal path");
		}

		[Fact]
		public async Task DoesNotReportForASynchronousOnlyDisposable()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Connection : IDisposable { public void Dispose() { } }

			                                       [Container]
			                                       [Singleton<Connection>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsFalse()
				.Because("a synchronous IDisposable is torn down by either disposal path");
		}

		[Fact]
		public async Task DoesNotReportForAPreBuiltInstance()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Connection : IAsyncDisposable { public ValueTask DisposeAsync() => default; }

			                                       [Container]
			                                       [Singleton<Connection>(Instance = nameof(Shared))]
			                                       public static partial class MyContainer
			                                       {
			                                       	public static Connection Shared { get; } = new Connection();
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsFalse()
				.Because("a pre-built Instance is the caller's to own and is never disposed by the container");
		}

		[Fact]
		public async Task ReportsForAFactoryWhoseDeclaredReturnTypeIsAsyncOnlyDisposable()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Connection : IAsyncDisposable { public ValueTask DisposeAsync() => default; }

			                                       [Container]
			                                       [Singleton<Connection>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                       	public static Connection Create() => new Connection();
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsTrue()
				.Because("the container owns a factory's output, and the declared return type reveals it is only asynchronously disposable");
		}

		[Fact]
		public async Task DoesNotReportWhenAFactoryHidesTheAsyncDisposableBehindItsDeclaredReturnType()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IConnection { }
			                                       public sealed class Connection : IConnection, IAsyncDisposable { public ValueTask DisposeAsync() => default; }

			                                       [Container]
			                                       [Singleton<IConnection>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                       	public static IConnection Create() => new Connection();
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT156"))).IsFalse()
				.Because("the static flags read the declared return type, so a hidden async-only disposable is left to the generated drain's runtime backstop");
		}
	}
}
