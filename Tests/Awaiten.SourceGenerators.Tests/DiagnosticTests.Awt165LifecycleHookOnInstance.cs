using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt165LifecycleHookOnInstance
	{
		[Fact]
		public async Task ReportsWhenALifecycleHookIsSetOnAPreBuiltInstance()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Instance = nameof(Shared), OnRelease = nameof(Release))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static readonly Service Shared = new();

			                                       	private static void Release(Service service) { }
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT165"))).IsTrue()
				.Because("the container does not own a pre-built instance, so the hook would never run");
		}

		[Fact]
		public async Task DoesNotReportForAPreBuiltInstanceWithoutHooks()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Instance = nameof(Shared))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static readonly Service Shared = new();
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT165"))).IsFalse()
				.Because("a pre-built instance without lifecycle hooks is valid");
		}
	}
}
