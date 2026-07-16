using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt190AmbiguousLifecycleHook
	{
		[Fact]
		public async Task ReportsWhenTwoOverloadsBothAcceptTheInstance()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }
			                                       public sealed class Settings { }

			                                       [Container]
			                                       [Singleton<Settings>]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service) { }
			                                       	private static void Started(Service service, Settings settings) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT190"))).IsTrue()
				.Because("two accessible methods named Started accept the instance, so the container cannot choose one");
		}

		[Fact]
		public async Task ReportsWhenOverloadsDifferOnlyInTheInstanceParameterType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnRelease = nameof(Stopping))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Stopping(Service service) { }
			                                       	private static void Stopping(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT190"))).IsTrue()
				.Because("both the exact and the object overload accept the instance, so the release hook choice is order-dependent");
		}

		[Fact]
		public async Task DoesNotReportWhenOnlyOneOverloadAcceptsTheInstance()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service) { }
			                                       	private static void Started(int unrelated) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT190") || d.Contains("AWT164"))).IsFalse()
				.Because("the int overload does not accept the instance, so there is exactly one usable hook and no ambiguity");
		}

		[Fact]
		public async Task DoesNotReportWhenTheSameNameServesDistinctRegistrationsOneMatchEach()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Alpha { }
			                                       public sealed class Beta { }

			                                       [Container]
			                                       [Singleton<Alpha>(OnActivated = nameof(Started))]
			                                       [Singleton<Beta>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT190"))).IsFalse()
				.Because("a single shared object hook is one match per registration, so neither registration is ambiguous");
		}
	}
}
