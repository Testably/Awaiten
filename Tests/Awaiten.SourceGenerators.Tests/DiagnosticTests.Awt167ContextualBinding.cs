using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt167ContextualBinding
	{
		[Fact]
		public async Task ReportsWhenTheNamedConsumerHasNoDependencyOnTheService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class DefaultClock : IClock { }
			                                       public sealed class TestClock : IClock { }
			                                       public sealed class NoClockDependency { }

			                                       [Container]
			                                       [Singleton<DefaultClock, IClock>]
			                                       [Singleton<TestClock, IClock>(WhenInjectedInto = typeof(NoClockDependency))]
			                                       [Singleton<NoClockDependency>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT167"))).IsTrue()
				.Because("the named consumer has no constructor dependency on the service, so the binding is dead");
		}

		[Fact]
		public async Task DoesNotReportWhenTheNamedConsumerDependsOnTheService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class DefaultClock : IClock { }
			                                       public sealed class TestClock : IClock { }
			                                       public sealed class NeedsTest { public NeedsTest(IClock clock) { } }

			                                       [Container]
			                                       [Singleton<DefaultClock, IClock>]
			                                       [Singleton<TestClock, IClock>(WhenInjectedInto = typeof(NeedsTest))]
			                                       [Singleton<NeedsTest>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the contextual binding is consumed by the named consumer's constructor parameter");
		}

		[Fact]
		public async Task ReportsWhenTheConsumersOnlyDependencyIsSelectedByAnExplicitFromKey()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class DefaultClock : IClock { }
			                                       public sealed class SpecialClock : IClock { }
			                                       public sealed class TestClock : IClock { }
			                                       public sealed class Consumer { public Consumer([FromKey("special")] IClock clock) { } }

			                                       [Container]
			                                       [Singleton<DefaultClock, IClock>]
			                                       [Singleton<SpecialClock, IClock>(Key = "special")]
			                                       [Singleton<TestClock, IClock>(WhenInjectedInto = typeof(Consumer))]
			                                       [Singleton<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT167"))).IsTrue()
				.Because("the [FromKey] parameter takes precedence, so the contextual binding is never applied");
		}
	}
}
