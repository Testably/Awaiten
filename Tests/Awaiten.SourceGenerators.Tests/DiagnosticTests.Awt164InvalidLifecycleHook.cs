using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt164InvalidLifecycleHook
	{
		[Fact]
		public async Task ReportsWhenTheHookMethodHasTheWrongParameterType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnActivated = nameof(Missing))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Missing(int wrongType) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT164"))).IsTrue()
				.Because("the named method does not accept the registered implementation type");
		}

		[Fact]
		public async Task ReportsWhenNoMethodOfThatNameExists()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnRelease = nameof(Service))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT164"))).IsTrue()
				.Because("no container method carries the named release hook");
		}

		[Fact]
		public async Task ReportsWhenTheHookMethodReturnsAValue()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static int Started(Service service) => 0;
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT164"))).IsTrue()
				.Because("a lifecycle hook must be a void method");
		}

		[Fact]
		public async Task DoesNotReportForAVoidMethodAcceptingTheImplementation()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnActivated = nameof(Started), OnRelease = nameof(Stopping))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service) { }
			                                       	private static void Stopping(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT164"))).IsFalse()
				.Because("a void method accepting the implementation (or a base type) is a usable hook");
		}
	}
}
