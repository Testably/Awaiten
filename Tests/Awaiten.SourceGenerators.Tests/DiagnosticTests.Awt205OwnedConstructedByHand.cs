using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt205OwnedConstructedByHand
	{
		private const string CupContainer = """
		                                    using Awaiten;
		                                    using System;

		                                    namespace MyCode;

		                                    public sealed class Cup : IDisposable { public void Dispose() { } }

		                                    [Container]
		                                    [Transient<Cup>]
		                                    public static partial class CafeContainer
		                                    {
		                                    }

		                                    """;

		[Test]
		public async Task ReportsANewOwnedOverTheRoot()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>(CupContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static void Use(CafeContainer.Root root)
			                                       	{
			                                       		using Owned<Cup> cup = new Owned<Cup>(root, new Cup());
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics).Contains("*AWT205*Owned<Cup>*Func<Owned<Cup>>*").AsWildcard()
				.Because("disposing a handle built over the root disposes the root, so the container alone creates Owned<T>");
		}

		[Test]
		public async Task ReportsATargetTypedNewOwned()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>(CupContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static void Use(IAwaitenScope scope)
			                                       	{
			                                       		Owned<Cup> cup = new(scope, new Cup());
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics).Contains("*AWT205*").AsWildcard()
				.Because("a target-typed new calls the same constructor as the explicit form");
		}

		[Test]
		public async Task DoesNotReportTheContainersOwnHandles()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>(CupContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static void Use(CafeContainer.Root root)
			                                       	{
			                                       		using Owned<Cup> cup = root.Resolve<Owned<Cup>>();
			                                       		Func<Owned<Cup>> factory = root.Resolve<Func<Owned<Cup>>>();
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics).DoesNotContain("*AWT205*").AsWildcard()
				.Because("the generated container constructs every handle it hands out, and generated code is exempt");
		}

		[Test]
		public async Task DoesNotReportADefaultHandle()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>(CupContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static void Use()
			                                       	{
			                                       		Owned<Cup> empty = new Owned<Cup>();
			                                       		Owned<Cup> other = default;
			                                       		empty.Dispose();
			                                       		other.Dispose();
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics).DoesNotContain("*AWT205*").AsWildcard()
				.Because("a default handle holds no scope, so disposing it is a no-op that cannot end anything's lifetime");
		}

		[Test]
		public async Task CanBeSuppressedInSource()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>(CupContainer + """
			                                       public static class Consumer
			                                       {
			                                       	public static void Use(IAwaitenScope scope)
			                                       	{
			                                       #pragma warning disable AWT205
			                                       		Owned<Cup> cup = new(scope, new Cup());
			                                       #pragma warning restore AWT205
			                                       	}
			                                       }
			                                       """);

			await That(diagnostics.Any(diagnostic => diagnostic.Contains("AWT205"))).IsFalse()
				.Because("a deliberate construction, such as a test double, is a warning the author can suppress");
		}
	}
}
