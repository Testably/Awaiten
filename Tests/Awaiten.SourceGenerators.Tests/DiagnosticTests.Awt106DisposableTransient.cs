using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt106DisposableTransient
	{
		[Fact]
		public async Task DoesNotReportForADisposableScoped()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                           using Awaiten;
			                                                           using System;

			                                                           namespace MyCode;

			                                                           public sealed class Resource : IDisposable { public void Dispose() { } }

			                                                           [Container]
			                                                           [Scoped<Resource>]
			                                                           public partial class MyContainer
			                                                           {
			                                                           }
			                                                           """);

			await That(diagnostics.Any(d => d.Contains("AWT106"))).IsFalse()
				.Because("a scoped instance is released with its scope, it does not accumulate");
		}

		[Fact]
		public async Task DoesNotReportForADisposableSingleton()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                           using Awaiten;
			                                                           using System;

			                                                           namespace MyCode;

			                                                           public sealed class Resource : IDisposable { public void Dispose() { } }

			                                                           [Container]
			                                                           [Singleton<Resource>]
			                                                           public partial class MyContainer
			                                                           {
			                                                           }
			                                                           """);

			await That(diagnostics.Any(d => d.Contains("AWT106"))).IsFalse()
				.Because("a singleton is released once with the container, it does not accumulate");
		}

		[Fact]
		public async Task DoesNotReportForAnAbstractDisposableTransient()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                           using Awaiten;
			                                                           using System;

			                                                           namespace MyCode;

			                                                           public abstract class Resource : IDisposable { public void Dispose() { } }

			                                                           [Container]
			                                                           [Transient<Resource>]
			                                                           public partial class MyContainer
			                                                           {
			                                                           }
			                                                           """);

			await That(diagnostics.Any(d => d.Contains("AWT106"))).IsFalse()
				.Because("an abstract type cannot be instantiated, so it is rejected as AWT103 and never accumulates");
		}

		[Fact]
		public async Task DoesNotReportForANonDisposableTransient()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                           using Awaiten;

			                                                           namespace MyCode;

			                                                           public sealed class Resource { }

			                                                           [Container]
			                                                           [Transient<Resource>]
			                                                           public partial class MyContainer
			                                                           {
			                                                           }
			                                                           """);

			await That(diagnostics.Any(d => d.Contains("AWT106"))).IsFalse()
				.Because("a transient with nothing to dispose cannot leak");
		}

		[Fact]
		public async Task IsSuppressibleInSourceWithPragma()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                           using Awaiten;
			                                                           using System;

			                                                           namespace MyCode;

			                                                           public sealed class Resource : IDisposable { public void Dispose() { } }

			                                                           [Container]
			                                                           #pragma warning disable AWT106
			                                                           [Transient<Resource>]
			                                                           #pragma warning restore AWT106
			                                                           public partial class MyContainer
			                                                           {
			                                                           }
			                                                           """);

			await That(diagnostics.Any(d => d.Contains("AWT106"))).IsFalse()
				.Because("an analyzer diagnostic can be suppressed in-source at the registration");
		}

		[Fact]
		public async Task ReportsForADisposableTransient()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                           using Awaiten;
			                                                           using System;

			                                                           namespace MyCode;

			                                                           public sealed class Resource : IDisposable { public void Dispose() { } }

			                                                           [Container]
			                                                           [Transient<Resource>]
			                                                           public partial class MyContainer
			                                                           {
			                                                           }
			                                                           """);

			await That(diagnostics.Any(d => d.Contains("AWT106"))).IsTrue();
		}
	}
}
