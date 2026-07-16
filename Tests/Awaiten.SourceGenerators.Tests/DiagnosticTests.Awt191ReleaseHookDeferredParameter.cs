using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	/// <summary>
	///     A release hook's dependencies are captured at construction, but a <c>Func&lt;T&gt;</c>/<c>Lazy&lt;T&gt;</c>
	///     parameter captures only a resolver delegate, and the hook runs during its owner's teardown, when the
	///     resolvers throw <c>ObjectDisposedException</c> - so the deferred value could never produce its target.
	///     AWT191 rejects the four deferred-delegate kinds on release hooks; an activation hook (which runs while the
	///     owner is alive) may take them freely.
	/// </summary>
	public class Awt191ReleaseHookDeferredParameter
	{
		[Fact]
		public async Task ReportsForAFuncReleaseHookParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool { }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Transient<Service>(OnRelease = nameof(Released))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Released(Service service, Func<Tool> tool) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT191"))).IsTrue()
				.Because("a Func<T> release capture holds a resolver delegate that is dead by the time the hook runs at teardown");
		}

		[Fact]
		public async Task ReportsForALazyReleaseHookParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool { }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Tool>]
			                                       [Transient<Service>(OnRelease = nameof(Released))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Released(Service service, Lazy<Tool> tool) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT191"))).IsTrue()
				.Because("an unmaterialized Lazy<T> release capture defers resolution past the owner's teardown, like a Func<T>");
		}

		[Fact]
		public async Task ReportsForAFuncTaskReleaseHookParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class Tool { }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Transient<Service>(OnRelease = nameof(Released))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Released(Service service, Func<Task<Tool>> tool) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT191"))).IsTrue()
				.Because("the async factory form defers resolution exactly like the synchronous Func<T>");
		}

		[Fact]
		public async Task DoesNotReportForAFuncActivationHookParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Tool { }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [Transient<Tool>]
			                                       [Transient<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service, Func<Tool> tool) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT191"))).IsFalse()
				.Because("an activation hook runs while the owner is alive, so its Func<T> parameter is invokable");
		}

		[Fact]
		public async Task DoesNotReportForADirectReleaseHookParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Pool { }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Pool>]
			                                       [Transient<Service>(OnRelease = nameof(Released))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Released(Service service, Pool pool) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT191"))).IsFalse()
				.Because("a direct release dependency is resolved at construction and captured by value, which is the supported shape");
		}
	}
}
