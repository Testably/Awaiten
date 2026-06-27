using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt106DisposableTransient
	{
		[Fact]
		public async Task DoesNotReportForADisposableScoped()
		{
			GeneratorResult result = Generator.Run("""
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT106"))).IsFalse()
				.Because("a scoped instance is released with its scope, it does not accumulate");
		}

		[Fact]
		public async Task DoesNotReportForADisposableSingleton()
		{
			GeneratorResult result = Generator.Run("""
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT106"))).IsFalse()
				.Because("a singleton is released once with the container, it does not accumulate");
		}

		[Fact]
		public async Task DoesNotReportForANonDisposableTransient()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Resource { }

			                                       [Container]
			                                       [Transient<Resource>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT106"))).IsFalse()
				.Because("a transient with nothing to dispose cannot leak");
		}

		[Fact]
		public async Task ReportsForADisposableTransient()
		{
			GeneratorResult result = Generator.Run("""
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT106"))).IsTrue();
		}
	}
}
