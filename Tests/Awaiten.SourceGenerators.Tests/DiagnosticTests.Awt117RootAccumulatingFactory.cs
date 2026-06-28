using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt117RootAccumulatingFactory
	{
		[Fact]
		public async Task ReportsWhenASingletonHoldsAFuncOverADisposableTransient()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool : IDisposable { public void Dispose() { } }
			                                       public sealed class Depot { public Depot(Func<Tool> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT117"))).IsTrue()
				.Because("a singleton holding a Func over a disposable transient accumulates instances on the root");
		}

		[Fact]
		public async Task ReportsForADisposableParameterizedServiceReachedThroughAFunc()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool : IDisposable { public Tool([Arg] int id) { } public void Dispose() { } }
			                                       public sealed class Depot { public Depot(Func<int, Tool> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT117"))).IsTrue()
				.Because("a disposable parameterized service built through a singleton-held Func accumulates on the root");
		}

		[Fact]
		public async Task DoesNotReportWhenTheFactoryHandsBackAnOwnedHandle()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool : IDisposable { public void Dispose() { } }
			                                       public sealed class Depot { public Depot(Func<Owned<Tool>> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT117"))).IsFalse()
				.Because("Func<Owned<T>> hands each instance back as a disposal handle, so nothing accumulates");
		}

		[Fact]
		public async Task DoesNotReportForADisposableTransientThatIsNotReachedThroughARootBoundFunc()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool : IDisposable { public void Dispose() { } }
			                                       public sealed class ScopedConsumer { public ScopedConsumer(Func<Tool> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Scoped<ScopedConsumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT117"))).IsFalse()
				.Because("a Func held by a scoped (not root-owned) consumer is bounded by that scope's lifetime, not the root's");
		}

		[Fact]
		public async Task DoesNotReportForANonDisposableTransientFactory()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool { }
			                                       public sealed class Depot { public Depot(Func<Tool> tools) { } }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Singleton<Depot>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT117"))).IsFalse()
				.Because("a non-disposable transient leaves nothing to accumulate");
		}
	}
}
