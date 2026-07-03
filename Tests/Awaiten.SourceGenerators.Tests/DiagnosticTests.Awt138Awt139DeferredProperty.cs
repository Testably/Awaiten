using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt138Awt139DeferredProperty
	{
		[Fact]
		public async Task ReportsAwt138WhenDeferredPropertyIsInitOnly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           // A deferred property is assigned after construction, which an init-only accessor forbids.
			                                           [Inject(Deferred = true)] public Bus Bus { get; init; }
			                                       }

			                                       [Container]
			                                       [Singleton<Bus>]
			                                       [Singleton<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT138"))).IsTrue()
				.Because("a deferred property is assigned after construction, so it needs a set accessor - an init-only accessor can only be assigned in an object initializer");
		}

		[Fact]
		public async Task ReportsAwt139WhenADeferredCycleIncludesATransient()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // A transient is not cached, so a mutual deferred cycle through it cannot terminate at runtime.
			                                       public sealed class Left { [Inject(Deferred = true)] public Right Right { get; set; } }
			                                       public sealed class Right { [Inject(Deferred = true)] public Left Left { get; set; } }

			                                       [Container]
			                                       [Transient<Left>]
			                                       [Transient<Right>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT139"))).IsTrue()
				.Because("a deferred property breaks a cycle only when the owning instance is cached; a transient is rebuilt on each resolve, so the cycle would recurse forever");
		}

		[Fact]
		public async Task ASingletonDeferredCycle_DoesNotReportAwt139()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // Both sides are cached, so the mutual deferred cycle terminates - the supported case.
			                                       public sealed class Left { [Inject(Deferred = true)] public Right Right { get; set; } }
			                                       public sealed class Right { [Inject(Deferred = true)] public Left Left { get; set; } }

			                                       [Container]
			                                       [Singleton<Left>]
			                                       [Singleton<Right>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT139"))).IsFalse()
				.Because("a singleton (or scoped) deferred cycle is cached before it is wired, so it terminates and is supported");
		}
	}
}
